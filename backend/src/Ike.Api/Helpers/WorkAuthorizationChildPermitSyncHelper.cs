using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.Models;
using Ike.Api.Services;
using PermitStatus = Ike.Api.Models.PermitStatus;

namespace Ike.Api.Helpers;

/// <summary>
/// Keeps child JobPermit rows in sync with the saved Work Authorisation checklist: removes unsigned rows when types drop off,
/// and creates Draft rows for required types once the job is In Progress and the WA has client sign-off.
/// </summary>
public static class WorkAuthorizationChildPermitSyncHelper
{
    /// <returns>True if any child permit rows were added or removed.</returns>
    public static async Task<bool> SyncAfterWorkAuthorisationSavedAsync(
        ApplicationDbContext db,
        Guid masterWaPermitId,
        IWorkAuthorizationPermitRulesService rules,
        IWebHostEnvironment env,
        IAuditService auditService,
        string? actingUserId,
        CancellationToken ct)
    {
        var master = await db.JobPermits
            .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .Include(p => p.Attachments)
            .FirstOrDefaultAsync(p => p.Id == masterWaPermitId, ct);
        if (master?.PermitTemplate?.PermitType?.IsWorkAuthorisation != true) return false;

        if (await PaidJobCardLockHelper.IsLockedAsync(db, master.JobCardId, ct)) return false;

        var job = await db.JobCards
            .Include(j => j.Site).ThenInclude(s => s!.Company)
            .FirstOrDefaultAsync(j => j.Id == master.JobCardId, ct);
        if (job == null || job.PaperPermitMode) return false;

        var all = await LoadAllJobPermitsAsync(db, master.JobCardId, ct);
        return await SyncMasterChildrenCoreAsync(db, job, master, all, rules, env, auditService, actingUserId, ct);
    }

    /// <returns>True if any child permit rows were added or removed.</returns>
    public static async Task<bool> SyncJobChildPermitsFromWorkAuthorisationAsync(
        ApplicationDbContext db,
        Guid jobCardId,
        IWorkAuthorizationPermitRulesService rules,
        IWebHostEnvironment env,
        IAuditService auditService,
        string? actingUserId,
        CancellationToken ct)
    {
        if (await PaidJobCardLockHelper.IsLockedAsync(db, jobCardId, ct)) return false;

        var job = await db.JobCards
            .Include(j => j.Site).ThenInclude(s => s!.Company)
            .FirstOrDefaultAsync(j => j.Id == jobCardId, ct);
        if (job == null || !job.PermitsRequired || job.PaperPermitMode) return false;
        if (!WaAmendmentSignOffHelper.JobStatusIndicatesWorkStarted(job.Status)) return false;

        var all = await LoadAllJobPermitsAsync(db, jobCardId, ct);
        var utcNow = DateTime.UtcNow;
        var master = all
            .Where(p => p.PermitTemplate?.PermitType?.IsWorkAuthorisation == true && !p.HiddenFromUiForHistory)
            .OrderByDescending(p => p.PermitNumber)
            .FirstOrDefault(p => MasterAllowsChildWork(p, utcNow));
        if (master == null) return false;
        if (!WaHasEffectiveClientSignOff(master)) return false;

        return await SyncMasterChildrenCoreAsync(db, job, master, all, rules, env, auditService, actingUserId, ct);
    }

    private static async Task<List<JobPermit>> LoadAllJobPermitsAsync(ApplicationDbContext db, Guid jobCardId, CancellationToken ct) =>
        await db.JobPermits
            .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .Include(p => p.Attachments)
            .Where(p => p.JobCardId == jobCardId)
            .ToListAsync(ct);

    private static bool MasterAllowsChildWork(JobPermit master, DateTime utcNow)
    {
        var s = (master.Status ?? "").Trim();
        if (s.Equals("rejected", StringComparison.OrdinalIgnoreCase) || s.Equals("cancelled", StringComparison.OrdinalIgnoreCase))
            return false;
        if (master.ValidTo.HasValue && master.ValidTo.Value < utcNow) return false;
        return true;
    }

    private static bool WaHasEffectiveClientSignOff(JobPermit master)
    {
        var dto = WorkAuthorizationRequestablePermitsHelper.DeserializeAndMergeMaster(master.ChecklistSnapshotJson, master.Id);
        var names = master.Attachments?.Select(a => a.FileName);
        return WaAmendmentSignOffHelper.WaPayloadIndicatesClientSignOff(dto, names)
               || WaAmendmentSignOffHelper.HasClientSignOffForPermit(master);
    }

    private static bool IsUnsignedRemovableStatus(string? status)
    {
        var s = (status ?? "").Trim();
        return s.Equals(PermitStatus.Draft, StringComparison.OrdinalIgnoreCase)
               || s.Equals(PermitStatus.Captured, StringComparison.OrdinalIgnoreCase)
               || s.Equals(PermitStatus.Signed, StringComparison.OrdinalIgnoreCase);
    }

    /// <returns>True if database was modified.</returns>
    private static async Task<bool> SyncMasterChildrenCoreAsync(
        ApplicationDbContext db,
        JobCard job,
        JobPermit master,
        List<JobPermit> all,
        IWorkAuthorizationPermitRulesService rules,
        IWebHostEnvironment env,
        IAuditService auditService,
        string? actingUserId,
        CancellationToken ct)
    {
        var companyId = job.Site?.CompanyId;
        var scoped = await WorkAuthorizationRequestablePermitsHelper
            .ActiveChildPermitTypesInSiteScope(db.PermitTypes.AsNoTracking(), companyId, job.Site?.Company)
            .ToListAsync(ct);

        var required = WorkAuthorizationRequestablePermitsHelper.GetRequiredChildPermitTypesFromChecklist(master, scoped, rules);
        var requiredTypeIds = required.Select(r => r.TypeId).ToHashSet();

        var toRemove = all.Where(c =>
                c.MasterPermitId == master.Id
                && c.PermitTemplate?.PermitType?.IsWorkAuthorisation != true
                && IsUnsignedRemovableStatus(c.Status)
                && c.PermitTemplate != null
                && !requiredTypeIds.Contains(c.PermitTemplate.PermitTypeId))
            .ToList();

        foreach (var ch in toRemove)
        {
            TryDeletePermitAttachmentFiles(env, ch.Attachments);
            if (job.ActiveJobPermitId == ch.Id)
                job.ActiveJobPermitId = null;
            db.JobPermits.Remove(ch);
            await auditService.LogAsync("PermitDeleted", "JobPermit", ch.Id.ToString(), "Removed: no longer required by Work Authorisation checklist", ct);
        }

        var removedAny = toRemove.Count > 0;
        if (removedAny)
            await db.SaveChangesAsync(ct);

        var utcNow = DateTime.UtcNow;
        var shouldAddDrafts = WaAmendmentSignOffHelper.JobStatusIndicatesWorkStarted(job.Status)
                              && WaHasEffectiveClientSignOff(master)
                              && MasterAllowsChildWork(master, utcNow);

        var addedAny = false;
        if (shouldAddDrafts)
        {
            all = await LoadAllJobPermitsAsync(db, job.Id, ct);

            foreach (var (typeId, _) in required)
            {
                if (WorkAuthorizationRequestablePermitsHelper.HasClosedChildForType(master.Id, typeId, all))
                    continue;
                if (WorkAuthorizationRequestablePermitsHelper.OpenOrTimeValidChildBlocksNewRequest(master.Id, typeId, all, utcNow))
                    continue;

                var template = await db.PermitTemplates
                    .Where(pt => pt.PermitTypeId == typeId && pt.IsActive)
                    .OrderBy(pt => pt.SiteId == null ? 0 : 1)
                    .ThenBy(pt => pt.CompanyId == null ? 0 : 1)
                    .FirstOrDefaultAsync(ct);
                if (template == null)
                {
                    var permitType = await db.PermitTypes.FindAsync([typeId], ct);
                    if (permitType == null) continue;
                    template = new PermitTemplate
                    {
                        Id = Guid.NewGuid(),
                        PermitTypeId = permitType.Id,
                        Name = permitType.Name,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    db.PermitTemplates.Add(template);
                }

                var maxPermitNumber = await MaxPermitNumberForTypeAsync(db, typeId, companyId, ct);
                var newPermit = new JobPermit
                {
                    Id = Guid.NewGuid(),
                    PermitNumber = maxPermitNumber + 1,
                    JobCardId = job.Id,
                    PermitTemplateId = template.Id,
                    Status = PermitStatus.Draft,
                    RequestedAt = DateTime.UtcNow,
                    RequestedByUserId = actingUserId,
                    MasterPermitId = master.Id
                };
                db.JobPermits.Add(newPermit);
                all.Add(newPermit);
                addedAny = true;

                var waIds = all
                    .Where(p => p.PermitTemplate?.PermitType?.IsWorkAuthorisation == true)
                    .Select(p => p.Id)
                    .Distinct()
                    .ToList();
                foreach (var waId in waIds)
                {
                    var exists = await db.JobPermitMasterLinks.AnyAsync(x => x.MasterPermitId == waId && x.ChildPermitId == newPermit.Id, ct);
                    if (!exists)
                    {
                        db.JobPermitMasterLinks.Add(new JobPermitMasterLink
                        {
                            MasterPermitId = waId,
                            ChildPermitId = newPermit.Id,
                            LinkedAt = DateTime.UtcNow
                        });
                    }
                }
            }

            if (addedAny)
                await db.SaveChangesAsync(ct);
        }

        return removedAny || addedAny;
    }

    private static async Task<int> MaxPermitNumberForTypeAsync(ApplicationDbContext db, Guid permitTypeId, Guid? clientCompanyId, CancellationToken ct)
    {
        var q = db.JobPermits
            .Include(p => p.PermitTemplate)
            .Include(p => p.JobCard).ThenInclude(j => j!.Site)
            .Where(p => p.PermitTemplate != null && p.PermitTemplate.PermitTypeId == permitTypeId);
        if (clientCompanyId.HasValue)
            q = q.Where(p => p.JobCard.Site != null && p.JobCard.Site.CompanyId == clientCompanyId.Value);
        return await q.Select(p => (int?)p.PermitNumber).MaxAsync(ct) ?? 0;
    }

    private static void TryDeletePermitAttachmentFiles(IWebHostEnvironment env, IEnumerable<JobPermitAttachment> attachments)
    {
        foreach (var att in attachments)
        {
            var rel = FilePathHelper.ValidateAndNormalize(att.FilePath);
            if (rel == null) continue;
            var fullPath = Path.Combine(env.ContentRootPath, rel);
            try
            {
                if (File.Exists(fullPath))
                    File.Delete(fullPath);
            }
            catch
            {
                // best-effort
            }
        }
    }
}
