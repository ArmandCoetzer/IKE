using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tradion.Api.Data;
using Tradion.Api.Models;
using Tradion.Api.Services;

namespace Tradion.Api.Helpers;

public static class PaperPermitModeHelper
{
    public const int PaperPermitNumberMaxLength = 50;

    public static IEnumerable<JobPermit> VisiblePermits(IEnumerable<JobPermit> permits) =>
        permits.Where(p => !p.HiddenFromUiForHistory);

    public static bool VisibleWorkAuthorisationHasClientSignOff(IEnumerable<JobPermit> allOnJob) =>
        VisiblePermits(allOnJob).Any(p =>
            p.PermitTemplate?.PermitType?.IsWorkAuthorisation == true
            && WaAmendmentSignOffHelper.HasClientSignOffForPermit(p));

    public static bool CanActivatePaperPermitMode(JobCard job) =>
        job.PermitsRequired
        && !job.PaperPermitMode;

    public static async Task<bool> UserMayActivatePaperPermitModeAsync(
        ApplicationDbContext db,
        Guid jobCardId,
        string userId,
        UserManager<ApplicationUser> userManager,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        if (await db.JobCardAssignments.AnyAsync(a => a.JobCardId == jobCardId && a.UserId == userId && a.IsPermitManager, ct))
            return true;
        var user = await userManager.FindByIdAsync(userId);
        return user != null && await permissionService.HasPermissionAsync(user, "AssignTechnicians");
    }
}
