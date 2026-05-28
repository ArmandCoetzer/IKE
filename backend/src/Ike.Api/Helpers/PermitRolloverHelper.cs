using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.DTOs.WorkAuthorizations;
using Ike.Api.Models;
using PermitStatus = Ike.Api.Models.PermitStatus;

namespace Ike.Api.Helpers;

/// <summary>
/// Creates next-day rollover permits on read interactions:
/// - Work Authorisation: create a new Draft WA after prior WA expires.
/// - Child permits: create a new Draft child only when latest permit of that type is Expired.
/// </summary>
public static class PermitRolloverHelper
{
    public static async Task EnsureLazyRolloverOnReadAsync(ApplicationDbContext db, Guid jobCardId, string? actingUserId, CancellationToken ct)
    {
        if (await db.JobCards.AsNoTracking().AnyAsync(j => j.Id == jobCardId && (!j.PermitsRequired || j.PaperPermitMode), ct))
            return;

        var all = await db.JobPermits
            .AsSplitQuery()
            .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .Include(p => p.MasterLinks)
            .Include(p => p.ChildLinks)
            .Where(p => p.JobCardId == jobCardId)
            .ToListAsync(ct);

        if (all.Count == 0) return;

        var utcNow = DateTime.UtcNow;
        var todaySa = IkeTimeHelper.TodayInSouthAfrica(utcNow);
        var changed = false;

        // WA rollover
        var waPermits = all.Where(IsWorkAuthorisation).OrderByDescending(p => p.PermitNumber).ToList();
        var expiredWaNeedingRollover = waPermits.FirstOrDefault(p =>
            IsExpiredStatus(p.Status) && p.ValidTo.HasValue && SaDate(p.ValidTo.Value) < todaySa);
        if (expiredWaNeedingRollover != null && !HasOpenWaAfter(waPermits, expiredWaNeedingRollover))
        {
            var newWa = await CreateWorkAuthorisationRolloverAsync(db, expiredWaNeedingRollover, actingUserId, ct);
            all.Add(newWa);
            changed = true;

            // Link existing children so both WAs can "see" them.
            var childrenOfExpiredWa = all.Where(p => !IsWorkAuthorisation(p) && IsLinkedToMaster(p, expiredWaNeedingRollover.Id)).ToList();
            foreach (var ch in childrenOfExpiredWa)
            {
                changed |= EnsureMasterChildLink(db, expiredWaNeedingRollover.Id, ch.Id);
                changed |= EnsureMasterChildLink(db, newWa.Id, ch.Id);
            }
        }

        // Child rollover by type (duplicate only when latest is expired)
        var latestSignedWa = LatestNonExpiredSignedWa(waPermits, todaySa);
        if (latestSignedWa != null)
        {
            var childByType = all
                .Where(p => !IsWorkAuthorisation(p) && p.PermitTemplate != null)
                .GroupBy(p => p.PermitTemplate!.PermitTypeId);
            foreach (var g in childByType)
            {
                var latest = g.OrderByDescending(x => x.PermitNumber).First();
                if (!IsExpiredStatus(latest.Status)) continue;
                if (!latest.ValidTo.HasValue || SaDate(latest.ValidTo.Value) >= todaySa) continue;
                if (g.Any(x => x.Id != latest.Id && IsOpenStatus(x.Status))) continue;

                var next = await CreateChildRolloverAsync(db, latest, latestSignedWa.Id, actingUserId, ct);
                all.Add(next);
                changed = true;

                foreach (var wa in waPermits.Where(w => IsExpiredStatus(w.Status) || w.Id == latestSignedWa.Id))
                    changed |= EnsureMasterChildLink(db, wa.Id, next.Id);
            }
        }

        if (changed)
            await db.SaveChangesAsync(ct);
    }

    private static async Task<JobPermit> CreateWorkAuthorisationRolloverAsync(ApplicationDbContext db, JobPermit expiredWa, string? actingUserId, CancellationToken ct)
    {
        var typeId = expiredWa.PermitTemplate!.PermitTypeId;
        var maxPermitNumber = await db.JobPermits
            .Include(p => p.PermitTemplate)
            .Where(p => p.PermitTemplate != null && p.PermitTemplate.PermitTypeId == typeId)
            .Select(p => (int?)p.PermitNumber)
            .MaxAsync(ct) ?? 0;

        var newPermitId = Guid.NewGuid();
        var snapshot = CloneWaPayloadWithoutSignatures(expiredWa.ChecklistSnapshotJson, newPermitId);
        var wa = new JobPermit
        {
            Id = newPermitId,
            PermitNumber = maxPermitNumber + 1,
            JobCardId = expiredWa.JobCardId,
            PermitTemplateId = expiredWa.PermitTemplateId,
            Status = PermitStatus.Draft,
            RequestedAt = DateTime.UtcNow,
            RequestedByUserId = actingUserId,
            ChecklistSnapshotJson = snapshot
        };
        db.JobPermits.Add(wa);
        return wa;
    }

    private static async Task<JobPermit> CreateChildRolloverAsync(
        ApplicationDbContext db,
        JobPermit expiredChild,
        Guid masterPermitId,
        string? actingUserId,
        CancellationToken ct)
    {
        var typeId = expiredChild.PermitTemplate!.PermitTypeId;
        var maxPermitNumber = await db.JobPermits
            .Include(p => p.PermitTemplate)
            .Where(p => p.PermitTemplate != null && p.PermitTemplate.PermitTypeId == typeId)
            .Select(p => (int?)p.PermitNumber)
            .MaxAsync(ct) ?? 0;

        var child = new JobPermit
        {
            Id = Guid.NewGuid(),
            PermitNumber = maxPermitNumber + 1,
            JobCardId = expiredChild.JobCardId,
            PermitTemplateId = expiredChild.PermitTemplateId,
            Status = PermitStatus.Draft,
            RequestedAt = DateTime.UtcNow,
            RequestedByUserId = actingUserId,
            MasterPermitId = masterPermitId,
            ChecklistSnapshotJson = expiredChild.ChecklistSnapshotJson,
            FormSnapshotJson = expiredChild.FormSnapshotJson
        };
        db.JobPermits.Add(child);
        return child;
    }

    private static bool IsWorkAuthorisation(JobPermit p) => p.PermitTemplate?.PermitType?.IsWorkAuthorisation == true;

    private static bool IsExpiredStatus(string? status) => string.Equals((status ?? "").Trim(), PermitStatus.Expired, StringComparison.OrdinalIgnoreCase);

    private static bool IsOpenStatus(string? status)
    {
        return PermitStatus.IsDraftLike(status)
            || PermitStatus.IsCapturedLike(status)
            || PermitStatus.IsActiveLike(status);
    }

    private static DateTime SaDate(DateTime utcLike) => IkeTimeHelper.TodayInSouthAfrica(utcLike.Kind == DateTimeKind.Utc ? utcLike : DateTime.SpecifyKind(utcLike, DateTimeKind.Utc));

    private static bool HasOpenWaAfter(List<JobPermit> waPermits, JobPermit expiredWa) =>
        waPermits.Any(w => w.PermitNumber > expiredWa.PermitNumber && IsOpenStatus(w.Status));

    private static JobPermit? LatestNonExpiredSignedWa(List<JobPermit> waPermits, DateTime todaySa)
    {
        return waPermits
            .Where(w => !IsExpiredStatus(w.Status))
            .Where(w => w.ValidTo == null || SaDate(w.ValidTo.Value) >= todaySa)
            .FirstOrDefault(w => WaAmendmentSignOffHelper.HasClientSignOffForPermit(w));
    }

    private static bool IsLinkedToMaster(JobPermit child, Guid masterId) =>
        child.MasterPermitId == masterId || child.ChildLinks.Any(l => l.MasterPermitId == masterId);

    private static bool EnsureMasterChildLink(ApplicationDbContext db, Guid masterId, Guid childId)
    {
        var exists = db.JobPermitMasterLinks.Local.Any(x => x.MasterPermitId == masterId && x.ChildPermitId == childId)
                     || db.JobPermitMasterLinks.Any(x => x.MasterPermitId == masterId && x.ChildPermitId == childId);
        if (exists) return false;
        db.JobPermitMasterLinks.Add(new JobPermitMasterLink
        {
            MasterPermitId = masterId,
            ChildPermitId = childId,
            LinkedAt = DateTime.UtcNow
        });
        return true;
    }

    private static string? CloneWaPayloadWithoutSignatures(string? json, Guid permitId)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;
        try
        {
            var dto = JsonSerializer.Deserialize<WorkAuthorizationMasterPermitDto>(json);
            if (dto == null) return json;
            dto.PermitGuid = permitId;
            dto.ModifiedDateUtc = null;
            dto.Declaration ??= new WorkAuthorizationDeclarationSectionDto();
            dto.Declaration.IssuingAuthority ??= new WorkAuthorizationSignatureDto();
            dto.Declaration.PerformingAuthority ??= new WorkAuthorizationSignatureDto();
            dto.Declaration.SiteAcknowledgement ??= new WorkAuthorizationSignatureDto();
            ClearSig(dto.Declaration.IssuingAuthority);
            ClearSig(dto.Declaration.PerformingAuthority);
            ClearSig(dto.Declaration.SiteAcknowledgement);
            return JsonSerializer.Serialize(dto);
        }
        catch
        {
            return json;
        }
    }

    private static void ClearSig(WorkAuthorizationSignatureDto sig)
    {
        sig.SignedDateTime = null;
        sig.SignatureImageBase64 = null;
        sig.SignatureImageUrl = null;
    }

    /// <summary>
    /// When a new Draft WA is requested after a predecessor no longer blocks, copy checklist payload with declaration signatures cleared (new permit id embedded).
    /// </summary>
    public static void ApplyReplacementDraftFromPredecessorWa(JobPermit newDraft, JobPermit predecessorWa)
    {
        newDraft.ChecklistSnapshotJson = CloneWaPayloadWithoutSignatures(predecessorWa.ChecklistSnapshotJson, newDraft.Id);
        newDraft.PendingWaAmendmentSignOff = false;
        newDraft.WaSignedBusinessContentHash = null;
    }

    /// <summary>
    /// Move <see cref="JobPermit.MasterPermitId"/> and <see cref="JobPermitMasterLink"/> rows from an expired WA to the replacement WA so existing child permits stay on the job under the new master.
    /// </summary>
    public static async Task RelinkChildPermitsFromExpiredWaToNewAsync(
        ApplicationDbContext db,
        Guid expiredWaId,
        Guid newWaId,
        DateTime linkedAt,
        CancellationToken ct)
    {
        var oldLinks = await db.JobPermitMasterLinks.Where(l => l.MasterPermitId == expiredWaId).ToListAsync(ct);
        var childIds = oldLinks.Select(l => l.ChildPermitId).Distinct().ToList();
        if (oldLinks.Count > 0)
            db.JobPermitMasterLinks.RemoveRange(oldLinks);

        foreach (var childId in childIds)
        {
            var already = await db.JobPermitMasterLinks.AnyAsync(l => l.MasterPermitId == newWaId && l.ChildPermitId == childId, ct);
            if (!already)
                db.JobPermitMasterLinks.Add(new JobPermitMasterLink { MasterPermitId = newWaId, ChildPermitId = childId, LinkedAt = linkedAt });
        }

        var fkChildren = await db.JobPermits.Where(p => p.MasterPermitId == expiredWaId).ToListAsync(ct);
        foreach (var ch in fkChildren)
            ch.MasterPermitId = newWaId;
    }

    /// <summary>Copy structured child data from the previous row of the same permit type when that row is expired (replacement request).</summary>
    public static void CopyChildDraftFromPredecessorIfExpired(JobPermit newDraft, JobPermit predecessorSameType, DateTime utcNow)
    {
        if (predecessorSameType.Id == newDraft.Id) return;
        if (!ChildPermitEligibleToCopyFrom(predecessorSameType, utcNow)) return;
        newDraft.FormSnapshotJson = predecessorSameType.FormSnapshotJson;
        newDraft.ChecklistSnapshotJson = predecessorSameType.ChecklistSnapshotJson;
    }

    private static bool ChildPermitEligibleToCopyFrom(JobPermit pred, DateTime utcNow)
    {
        if (PermitStatus.IsRejectedOrCancelled(pred.Status)) return false;
        if (PermitStatus.IsExpiredLike(pred.Status)) return true;
        if (pred.ValidTo.HasValue && pred.ValidTo.Value < utcNow) return true;
        return false;
    }
}
