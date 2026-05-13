using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Tradion.Api.Data;
using Tradion.Api.DTOs.WorkAuthorizations;
using Tradion.Api.Models;

namespace Tradion.Api.Helpers;

/// <summary>
/// Work Authorisation was edited after client sign-off: job standstill until amended WA is signed again.
/// Exception: child permits already client-signed-off and active/approved while job is In Progress may continue.
/// </summary>
public static class WaAmendmentSignOffHelper
{
    public const string WorkStandstillMessage =
        "The Work Authorisation was changed and must be signed off again by the client before other work can continue. "
        + "You may still work only under child permits that were already signed off and active while the job is in progress.";

    public const string PermitActionBlockedMessage =
        "This action is paused until the amended Work Authorisation is signed off again by the client.";

    public const string RequestPermitBlockedMessage =
        "You cannot request permits until the amended Work Authorisation is signed off again.";

    public const string WaExpiredStandstillMessage =
        "Work is paused because the Work Authorisation expired. Complete and sign off the new Work Authorisation to continue.";

    private static readonly JsonSerializerOptions WaJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static async Task<bool> JobHasPendingWaAmendmentAsync(ApplicationDbContext db, Guid jobCardId, CancellationToken ct)
    {
        if (await db.JobCards.AsNoTracking().AnyAsync(j => j.Id == jobCardId && j.PaperPermitMode, ct))
            return false;
        return await db.JobPermits.AsNoTracking()
            .AnyAsync(p => p.JobCardId == jobCardId && p.PendingWaAmendmentSignOff && !p.HiddenFromUiForHistory, ct);
    }

    public static async Task<bool> JobHasExpiredWorkAuthorisationStandstillAsync(ApplicationDbContext db, Guid jobCardId, CancellationToken ct)
    {
        var job = await db.JobCards.AsNoTracking()
            .Where(j => j.Id == jobCardId)
            .Select(j => new { j.PaperPermitMode, j.Status })
            .FirstOrDefaultAsync(ct);
        if (job == null)
            return false;
        if (job.PaperPermitMode || JobCardStatus.IsCompletedLike(job.Status))
            return false;
        var todaySa = TradionTimeHelper.TodayInSouthAfrica(DateTime.UtcNow);
        var permits = await db.JobPermits.AsNoTracking()
            .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .Where(p => p.JobCardId == jobCardId && !p.HiddenFromUiForHistory && p.PermitTemplate != null && p.PermitTemplate.PermitType.IsWorkAuthorisation)
            .ToListAsync(ct);
        if (permits.Count == 0) return false;

        var hasExpiredWa = permits.Any(p =>
            PermitStatus.IsExpiredLike(p.Status)
            || (p.ValidTo.HasValue && TradionTimeHelper.TodayInSouthAfrica(p.ValidTo.Value) < todaySa));
        if (!hasExpiredWa) return false;

        var hasSignedValidWa = permits.Any(p =>
            !PermitStatus.IsExpiredLike(p.Status)
            && (p.ValidTo == null || TradionTimeHelper.TodayInSouthAfrica(p.ValidTo.Value) >= todaySa)
            && HasClientSignOffForPermit(p));
        return !hasSignedValidWa;
    }

    /// <summary>Hash of WA payload with declaration signatures cleared (business content only).</summary>
    public static string ComputeBusinessContentHash(WorkAuthorizationMasterPermitDto dto)
    {
        var json = JsonSerializer.Serialize(CloneAndStripDeclarationSignatures(dto), WaJsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    public static WorkAuthorizationMasterPermitDto? DeserializeWaPayload(string? checklistSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(checklistSnapshotJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<WorkAuthorizationMasterPermitDto>(checklistSnapshotJson, WaJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Client/site acknowledgement present in JSON or signature attachment (same idea as WorkAuthorizations email gate).</summary>
    public static bool WaPayloadIndicatesClientSignOff(WorkAuthorizationMasterPermitDto? dto, IEnumerable<string>? attachmentFileNames)
    {
        if (attachmentFileNames != null && attachmentFileNames.Any(n => (n ?? string.Empty).Contains("signature", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (dto?.Declaration?.SiteAcknowledgement == null) return false;
        var site = dto.Declaration.SiteAcknowledgement;
        if (site.SignedDateTime.HasValue &&
            (!string.IsNullOrWhiteSpace(site.SignatureImageBase64) || !string.IsNullOrWhiteSpace(site.SignatureImageUrl)))
            return true;
        return false;
    }

    public static void StripSiteClientSignOffFromPayload(WorkAuthorizationMasterPermitDto dto)
    {
        dto.Declaration ??= new WorkAuthorizationDeclarationSectionDto();
        var prev = dto.Declaration.SiteAcknowledgement;
        dto.Declaration.SiteAcknowledgement = new WorkAuthorizationSignatureDto
        {
            Name = prev?.Name,
            Role = prev?.Role
        };
    }

    public static bool JobStatusIndicatesWorkStarted(string? jobStatus)
    {
        return JobCardStatus.IsInProgressLike(jobStatus);
    }

    /// <summary>Child permit may continue during WA amendment standstill.</summary>
    public static bool ChildPermitMayContinueDuringAmendment(
        JobPermit child,
        JobCard job,
        bool hasClientSignOff)
    {
        if (child.PermitTemplate?.PermitType?.IsWorkAuthorisation == true) return false;
        if (!child.MasterPermitId.HasValue) return false;
        if (!hasClientSignOff) return false;
        if (!JobStatusIndicatesWorkStarted(job.Status)) return false;
        var st = (child.Status ?? "").Trim();
        return PermitStatus.IsActiveLike(st);
    }

    public static bool HasClientSignOffForPermit(JobPermit p) =>
        p.PaperClientSignedOffAt.HasValue
        || HasClientSignOffStatic(p.Status, p.ChecklistSnapshotJson, p.Attachments?.Select(a => a.FileName));

    private static bool HasClientSignOffStatic(string? permitStatus, string? checklistSnapshotJson, IEnumerable<string>? attachmentFileNames)
    {
        var st = (permitStatus ?? "").Trim().ToLowerInvariant();
        if (PermitStatus.IsActiveLike(st))
            return true;
        if (attachmentFileNames != null && attachmentFileNames.Any(n => (n ?? string.Empty).ToLower().Contains("signature")))
            return true;
        if (string.IsNullOrWhiteSpace(checklistSnapshotJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(checklistSnapshotJson);
            if (!doc.RootElement.TryGetProperty("declaration", out var declaration)) return false;
            if (!declaration.TryGetProperty("siteAcknowledgement", out var siteAck)) return false;
            var hasSignedDate = siteAck.TryGetProperty("signedDateTime", out var signedDt) && signedDt.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(signedDt.GetString());
            var hasSigB64 = siteAck.TryGetProperty("signatureImageBase64", out var sigB64) && sigB64.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(sigB64.GetString());
            var hasSigUrl = siteAck.TryGetProperty("signatureImageUrl", out var sigUrl) && sigUrl.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(sigUrl.GetString());
            return hasSignedDate && (hasSigB64 || hasSigUrl);
        }
        catch
        {
            return false;
        }
    }

    private static WorkAuthorizationMasterPermitDto CloneAndStripDeclarationSignatures(WorkAuthorizationMasterPermitDto src)
    {
        var json = JsonSerializer.Serialize(src, WaJsonOptions);
        var clone = JsonSerializer.Deserialize<WorkAuthorizationMasterPermitDto>(json, WaJsonOptions) ?? new WorkAuthorizationMasterPermitDto();
        clone.ModifiedDateUtc = null;
        clone.Declaration ??= new WorkAuthorizationDeclarationSectionDto();
        clone.Declaration.IssuingAuthority ??= new WorkAuthorizationSignatureDto();
        clone.Declaration.PerformingAuthority ??= new WorkAuthorizationSignatureDto();
        clone.Declaration.SiteAcknowledgement ??= new WorkAuthorizationSignatureDto();
        StripSig(clone.Declaration.IssuingAuthority);
        StripSig(clone.Declaration.PerformingAuthority);
        StripSig(clone.Declaration.SiteAcknowledgement);
        return clone;
    }

    private static void StripSig(WorkAuthorizationSignatureDto sig)
    {
        sig.SignedDateTime = null;
        sig.SignatureImageBase64 = null;
        sig.SignatureImageUrl = null;
    }
}
