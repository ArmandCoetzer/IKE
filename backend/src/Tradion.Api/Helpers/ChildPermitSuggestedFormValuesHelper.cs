using System.Globalization;
using System.Text.Json;
using Tradion.Api.DTOs.WorkAuthorizations;
using Tradion.Api.Models;

namespace Tradion.Api.Helpers;

/// <summary>
/// Fills empty child permit structured form fields from the job, site, and linked Work Authorisation payload (API display only — not persisted until the technician saves the form).
/// </summary>
public static class ChildPermitSuggestedFormValuesHelper
{
    public static Dictionary<string, string> MergeSuggestedValues(
        JobCard job,
        JobPermit child,
        JobPermit? masterWa,
        Dictionary<string, string> existing)
    {
        var result = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
        if (child.PermitTemplate?.PermitType?.IsWorkAuthorisation == true)
            return result;

        WorkAuthorizationMasterPermitDto? wa = null;
        if (masterWa != null
            && masterWa.PermitTemplate?.PermitType?.IsWorkAuthorisation == true
            && !string.IsNullOrWhiteSpace(masterWa.ChecklistSnapshotJson))
        {
            try
            {
                wa = JsonSerializer.Deserialize<WorkAuthorizationMasterPermitDto>(masterWa.ChecklistSnapshotJson);
            }
            catch
            {
                // ignore invalid snapshot
            }
        }

        var site = job.Site;
        var siteLine = JoinNonEmpty(" — ", site?.Name, site?.Address);

        void SuggestIfNoKeyYet(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (result.ContainsKey(key)) return;
            result[key] = value.Trim();
        }

        if (wa != null)
        {
            SuggestIfNoKeyYet("work_location", FirstNonEmpty(wa.LocationTask?.LocationOfOperation, wa.Header?.SiteName, siteLine));
            SuggestIfNoKeyYet("task_summary", FirstNonEmpty(wa.LocationTask?.TaskDescription, wa.Header?.ScopeOfWorks, job.Description, job.ServiceRequest?.Description));
            SuggestIfNoKeyYet("written_authorisation_ref", string.IsNullOrWhiteSpace(wa.WorkAuthorizationNumber) ? null : wa.WorkAuthorizationNumber);
            SuggestIfNoKeyYet("site_contact", wa.Declaration?.SiteAcknowledgement?.Name);
            SuggestIfNoKeyYet("issuing_authority_name", wa.Declaration?.IssuingAuthority?.Name);
            SuggestIfNoKeyYet("issuing_authority_role", wa.Declaration?.IssuingAuthority?.Role);
            SuggestIfNoKeyYet("performing_authority_name", wa.Declaration?.PerformingAuthority?.Name);
            SuggestIfNoKeyYet("performing_authority_role", wa.Declaration?.PerformingAuthority?.Role);
            var start = wa.Header?.ValidFromDate ?? job.DueDate?.Date ?? DateTime.UtcNow.Date;
            SuggestIfNoKeyYet("intended_start_date", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        // Fallbacks when WA is missing or did not supply a value
        SuggestIfNoKeyYet("work_location", siteLine);
        SuggestIfNoKeyYet("task_summary", FirstNonEmpty(job.Description, job.ServiceRequest?.Description));
        if (job.JobCardNumber != null)
            SuggestIfNoKeyYet("written_authorisation_ref", $"Job {job.JobCardNumber.Trim()}");

        return result;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        }
        return null;
    }

    private static string? JoinNonEmpty(string sep, params string?[] parts)
    {
        var list = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToList();
        return list.Count == 0 ? null : string.Join(sep, list);
    }
}
