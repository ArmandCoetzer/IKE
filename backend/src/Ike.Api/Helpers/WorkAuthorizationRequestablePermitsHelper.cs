using System.Linq;
using System.Text.Json;
using Ike.Api.DTOs.WorkAuthorizations;
using Ike.Api.Models;
using Ike.Api.Services;
using PermitStatus = Ike.Api.Models.PermitStatus;

namespace Ike.Api.Helpers;

/// <summary>
/// Maps Work Authorisation checklist (saved JSON) to concrete permit types and filters what can still be requested.
/// </summary>
public static class WorkAuthorizationRequestablePermitsHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool PermitKeyMatchesTypeName(string key, string? permitTypeName)
    {
        if (string.IsNullOrWhiteSpace(permitTypeName)) return false;
        var n = permitTypeName.Trim().ToLowerInvariant();
        return key switch
        {
            WorkAuthorizationPermitKeys.HotWork => n.Contains("hot") && n.Contains("work"),
            WorkAuthorizationPermitKeys.Excavation => n.Contains("excavat"),
            WorkAuthorizationPermitKeys.WorkingAtHeights => n.Contains("height"),
            WorkAuthorizationPermitKeys.CleaningDegassing => n.Contains("degas") || (n.Contains("clean") && n.Contains("gas")),
            WorkAuthorizationPermitKeys.LiftingOperations => n.Contains("lift"),
            WorkAuthorizationPermitKeys.ConfinedSpaceEntry => n.Contains("confined"),
            WorkAuthorizationPermitKeys.RadiographicTesting => n.Contains("radiograph"),
            WorkAuthorizationPermitKeys.EnergyIsolation => (n.Contains("energy") && n.Contains("isol")) || n.Contains("lockout")
                || n.Contains("powered") || (n.Contains("power") && n.Contains("system")),
            _ => false
        };
    }

    /// <summary>
    /// Active non–work-auth permit types available for a job site: global, the site company, or the MSP when the site is a client subsidiary.
    /// </summary>
    public static IQueryable<PermitType> ActiveChildPermitTypesInSiteScope(
        IQueryable<PermitType> source,
        Guid? siteCompanyId,
        Company? siteCompany)
    {
        Guid? mspCompanyId = siteCompany?.Type == CompanyType.Client && siteCompany.ParentCompanyId.HasValue
            ? siteCompany.ParentCompanyId
            : null;
        return source.Where(pt => pt.IsActive && !pt.IsWorkAuthorisation
            && (pt.CompanyId == null || pt.CompanyId == siteCompanyId
                || (mspCompanyId.HasValue && pt.CompanyId == mspCompanyId.Value)));
    }

    public static WorkAuthorizationMasterPermitDto? DeserializeAndMergeMaster(string? json, Guid permitId)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<WorkAuthorizationMasterPermitDto>(json, JsonOptions);
            if (dto == null) return null;
            var waNum = string.IsNullOrWhiteSpace(dto.WorkAuthorizationNumber) ? "WA" : dto.WorkAuthorizationNumber;
            var template = WorkAuthorizationMasterPermitDefaults.CreateTemplate(permitId, waNum);
            WorkAuthorizationMasterPermitDefaults.MergeInto(dto, template);
            return dto;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Work permits the permit manager may still request for this master: from checklist rules, minus completed (Closed) or already-open valid rows.
    /// </summary>
    public static List<(Guid TypeId, string Name)> GetRequestableWorkPermitTypes(
        JobPermit masterPermit,
        IReadOnlyList<JobPermit> allPermitsOnJob,
        IReadOnlyList<PermitType> scopedActivePermitTypes,
        DateTime utcNow,
        IWorkAuthorizationPermitRulesService rules)
    {
        var result = new List<(Guid TypeId, string Name)>();
        var dto = DeserializeAndMergeMaster(masterPermit.ChecklistSnapshotJson, masterPermit.Id);
        if (dto == null) return result;

        var derived = rules.GetDerivedPermits(dto);
        foreach (var d in derived.Where(x => x.IsRequired))
        {
            var pt = scopedActivePermitTypes.FirstOrDefault(x =>
                !x.IsWorkAuthorisation && PermitKeyMatchesTypeName(d.PermitKey, x.Name));
            if (pt == null) continue;

            if (HasClosedChildForType(masterPermit.Id, pt.Id, allPermitsOnJob)) continue;
            if (OpenOrTimeValidChildBlocksNewRequest(masterPermit.Id, pt.Id, allPermitsOnJob, utcNow)) continue;

            result.Add((pt.Id, pt.Name ?? d.PermitLabel));
        }

        return result.DistinctBy(x => x.TypeId).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// All derived required child permit types from the saved checklist (no filtering by existing rows). Used to sync JobPermit rows.
    /// </summary>
    public static List<(Guid TypeId, string Name)> GetRequiredChildPermitTypesFromChecklist(
        JobPermit masterPermit,
        IReadOnlyList<PermitType> scopedActivePermitTypes,
        IWorkAuthorizationPermitRulesService rules)
    {
        var result = new List<(Guid TypeId, string Name)>();
        var dto = DeserializeAndMergeMaster(masterPermit.ChecklistSnapshotJson, masterPermit.Id);
        if (dto == null) return result;
        foreach (var d in rules.GetDerivedPermits(dto).Where(x => x.IsRequired))
        {
            var pt = scopedActivePermitTypes.FirstOrDefault(x =>
                !x.IsWorkAuthorisation && PermitKeyMatchesTypeName(d.PermitKey, x.Name));
            if (pt != null) result.Add((pt.Id, pt.Name ?? d.PermitLabel));
        }

        return result.DistinctBy(x => x.TypeId).ToList();
    }

    /// <summary>True if this master already has a completed (Done) child row for the permit type — do not auto-create another draft.</summary>
    public static bool HasClosedChildForType(Guid masterId, Guid permitTypeId, IReadOnlyList<JobPermit> all)
    {
        return all.Any(ch => ch.MasterPermitId == masterId
            && ch.PermitTemplate?.PermitTypeId == permitTypeId
            && string.Equals(ch.Status, PermitStatus.Closed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Blocks requesting the same type again while a non-closed row exists and is still within its validity window.</summary>
    public static bool OpenOrTimeValidChildBlocksNewRequest(Guid masterId, Guid permitTypeId, IReadOnlyList<JobPermit> all, DateTime utcNow)
    {
        foreach (var ch in all)
        {
            if (ch.MasterPermitId != masterId) continue;
            if (ch.PermitTemplate?.PermitTypeId != permitTypeId) continue;
            var s = (ch.Status ?? "").Trim();
            if (string.Equals(s, PermitStatus.Closed, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(s, PermitStatus.Expired, StringComparison.OrdinalIgnoreCase)) continue;
            if (s.Equals("rejected", StringComparison.OrdinalIgnoreCase)) continue;
            if (s.Equals("cancelled", StringComparison.OrdinalIgnoreCase)) continue;
            // Any non-terminal row for this type (including Draft) blocks listing it again until closed/expired or validity passed.
            if (string.Equals(s, PermitStatus.Draft, StringComparison.OrdinalIgnoreCase))
                return true;
            if (ch.ValidTo.HasValue && ch.ValidTo.Value < utcNow) continue;
            return true;
        }

        return false;
    }

    public static bool IsPermitTypeRequestableForMaster(
        Guid requestedPermitTypeId,
        JobPermit masterPermit,
        IReadOnlyList<JobPermit> allPermitsOnJob,
        IReadOnlyList<PermitType> scopedActivePermitTypes,
        DateTime utcNow,
        IWorkAuthorizationPermitRulesService rules)
    {
        var allowed = GetRequestableWorkPermitTypes(masterPermit, allPermitsOnJob, scopedActivePermitTypes, utcNow, rules);
        return allowed.Any(a => a.TypeId == requestedPermitTypeId);
    }

    /// <summary>
    /// True when the saved Work Authorisation checklist still requires this child permit type (derived rules).
    /// When master payload is missing, returns true (conservative).
    /// </summary>
    public static bool IsPermitTypeRequiredByMasterChecklist(
        JobPermit masterWa,
        Guid childPermitTypeId,
        IReadOnlyList<PermitType> scopedActivePermitTypes,
        IWorkAuthorizationPermitRulesService rules)
    {
        if (masterWa.PermitTemplate?.PermitType?.IsWorkAuthorisation != true) return true;
        var dto = DeserializeAndMergeMaster(masterWa.ChecklistSnapshotJson, masterWa.Id);
        if (dto == null) return true;
        foreach (var d in rules.GetDerivedPermits(dto).Where(x => x.IsRequired))
        {
            var pt = scopedActivePermitTypes.FirstOrDefault(x =>
                !x.IsWorkAuthorisation && PermitKeyMatchesTypeName(d.PermitKey, x.Name));
            if (pt != null && pt.Id == childPermitTypeId) return true;
        }

        return false;
    }
}
