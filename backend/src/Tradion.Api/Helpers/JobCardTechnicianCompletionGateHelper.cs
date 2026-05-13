using Tradion.Api.Models;

namespace Tradion.Api.Helpers;

/// <summary>
/// Mirrors the mobile technician "mark completed" gate (<c>_canComplete</c>): In Progress, child permits closed/done,
/// before + after site photos, no live expired Active/Approved permit.
/// </summary>
public static class JobCardTechnicianCompletionGateHelper
{
    public static bool IsRejectedOrCancelledPermit(string? status)
    {
        return PermitStatus.IsRejectedOrCancelled(status);
    }

    /// <summary>Returns false with a short reason when prerequisites are not met.</summary>
    public static bool TryValidate(
        string jobStatus,
        bool permitsRequired,
        IReadOnlyCollection<JobPermit> visiblePermits,
        IReadOnlyCollection<JobCardDocument> documents,
        DateTime utcNow,
        out string? errorMessage)
    {
        errorMessage = null;
        if (!JobCardStatus.IsInProgressLike(jobStatus))
        {
            errorMessage = "The job must be In Progress before this step.";
            return false;
        }

        if (permitsRequired && visiblePermits.Count == 0)
        {
            errorMessage = "Permits are required for this job.";
            return false;
        }

        foreach (var p in visiblePermits)
        {
            if (IsRejectedOrCancelledPermit(p.Status))
                continue;
            var isWa = p.PermitTemplate?.PermitType?.IsWorkAuthorisation == true;
            if (isWa)
                continue;
            if (!PermitStatus.IsClosedLike(p.Status))
            {
                errorMessage = "Every work permit must be marked done (closed) before completing the job.";
                return false;
            }
        }

        var hasBefore = documents.Any(d => string.Equals(d.DocumentType, "BeforeWork", StringComparison.Ordinal));
        var hasAfter = documents.Any(d => string.Equals(d.DocumentType, "AfterWork", StringComparison.Ordinal));
        if (!hasBefore || !hasAfter)
        {
            errorMessage = "Upload both before-work and after-work site photos before completing the job.";
            return false;
        }

        foreach (var p in visiblePermits)
        {
            if (IsRejectedOrCancelledPermit(p.Status))
                continue;
            if (PermitStatus.IsActiveLike(p.Status) && p.ValidTo.HasValue && p.ValidTo.Value < utcNow)
            {
                errorMessage = "An active permit has expired. Resolve permits before completing the job.";
                return false;
            }
        }

        return true;
    }

    public static bool JobStatusIsTerminal(string? status)
    {
        return JobCardStatus.IsCompletedLike(status);
    }
}
