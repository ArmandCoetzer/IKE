using Microsoft.EntityFrameworkCore;
using Tradion.Api.Data;
using Tradion.Api.Helpers;
using Tradion.Api.Models;

namespace Tradion.Api.Services;

/// <summary>
/// Background service that periodically checks for expired permits and creates notifications
/// for admins and assigned technicians.
/// Child work permits linked to a Work Authorisation (MasterPermitId) expire when the master&apos;s
/// validity end time has passed, even if the child&apos;s own ValidTo is later.
/// </summary>
public class PermitExpiryNotificationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan AlreadyNotifiedWithin = TimeSpan.FromHours(24);

    public PermitExpiryNotificationHostedService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredPermitsAsync(stoppingToken);
            }
            catch (Exception)
            {
                // Log but continue; avoid crashing the host
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private static bool IsActiveOrApproved(string? status)
    {
        return PermitStatus.IsActiveLike(status);
    }

    private async Task ProcessExpiredPermitsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        var now = DateTime.UtcNow;
        var todaySa = TradionTimeHelper.TodayInSouthAfrica(now);
        var cutoff = now.Add(-AlreadyNotifiedWithin);

        // 1) Own validity ended (Active/Approved + ValidTo in the past)
        var expiredByOwnValidTo = await db.JobPermits
            .Include(p => p.PermitTemplate)
            .Where(p => IsActiveOrApproved(p.Status)
                && p.ValidTo.HasValue
                && TradionTimeHelper.TodayInSouthAfrica(p.ValidTo.Value) < todaySa)
            .ToListAsync(ct);

        foreach (var p in expiredByOwnValidTo)
            p.Status = PermitStatus.Expired;

        // 2) Linked to a Work Authorisation master: when the master validity day has passed,
        //    expire the child (legacy MasterPermitId and new link table).
        var linkedChildren = await db.JobPermits
            .Include(p => p.PermitTemplate)
            .Include(p => p.MasterPermit).ThenInclude(m => m!.PermitTemplate).ThenInclude(t => t!.PermitType)
            .Include(p => p.ChildLinks)
            .Where(p => p.MasterPermitId != null && IsActiveOrApproved(p.Status))
            .ToListAsync(ct);

        var linkedViaTable = await db.JobPermits
            .Include(p => p.PermitTemplate)
            .Include(p => p.ChildLinks)
            .ThenInclude(l => l.MasterPermit)
            .ThenInclude(m => m!.PermitTemplate)
            .ThenInclude(t => t!.PermitType)
            .Where(p => IsActiveOrApproved(p.Status) && p.ChildLinks.Any())
            .ToListAsync(ct);

        var expiredByMaster = new List<JobPermit>();
        foreach (var c in linkedChildren)
        {
            var m = c.MasterPermit;
            if (m?.PermitTemplate?.PermitType?.IsWorkAuthorisation != true)
                continue;
            if (!m.ValidTo.HasValue || TradionTimeHelper.TodayInSouthAfrica(m.ValidTo.Value) >= todaySa)
                continue;
            c.Status = PermitStatus.Expired;
            expiredByMaster.Add(c);
        }

        foreach (var c in linkedViaTable)
        {
            if (expiredByMaster.Any(x => x.Id == c.Id)) continue;
            var hasExpiredWa = c.ChildLinks.Any(l =>
                l.MasterPermit?.PermitTemplate?.PermitType?.IsWorkAuthorisation == true
                && l.MasterPermit.ValidTo.HasValue
                && TradionTimeHelper.TodayInSouthAfrica(l.MasterPermit.ValidTo.Value) < todaySa);
            if (!hasExpiredWa) continue;
            c.Status = PermitStatus.Expired;
            expiredByMaster.Add(c);
        }

        var allExpiredThisRun = expiredByOwnValidTo
            .Concat(expiredByMaster)
            .GroupBy(p => p.Id)
            .Select(g => g.First())
            .ToList();

        if (allExpiredThisRun.Count == 0)
            return;

        var recentlyNotified = await db.Notifications
            .Where(n => n.Type == "PermitExpired" && n.CreatedAt > cutoff && n.RelatedEntityId != null)
            .Select(n => n.RelatedEntityId!)
            .Distinct()
            .ToListAsync(ct);
        var notifiedSet = new HashSet<string>(recentlyNotified);

        var adminUserIds = await permissionService.GetUserIdsWithPermissionAsync("ViewPermits", ct);
        var adminSet = new HashSet<string>(adminUserIds);

        foreach (var item in allExpiredThisRun)
        {
            if (notifiedSet.Contains(item.Id.ToString()))
                continue;

            var permitName = item.PermitTemplate?.Name ?? "Permit";
            var assignedUserIds = await db.JobCardAssignments
                .Where(a => a.JobCardId == item.JobCardId)
                .Select(a => a.UserId)
                .ToListAsync(ct);

            var job = await db.JobCards.AsNoTracking()
                .Where(j => j.Id == item.JobCardId)
                .Select(j => new { j.JobCardNumber })
                .FirstOrDefaultAsync(ct);
            var jobNumber = job?.JobCardNumber ?? item.JobCardId.ToString();

            var title = "Permit expired";
            var body = $"Permit '{permitName}' for job {jobNumber} has expired.";
            var userIds = new HashSet<string>(adminSet);
            foreach (var uid in assignedUserIds)
                if (!string.IsNullOrEmpty(uid))
                    userIds.Add(uid);

            foreach (var uid in userIds)
            {
                db.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = uid,
                    Title = title.Length > 256 ? title[..256] : title,
                    Body = body.Length > 2000 ? body[..2000] : body,
                    Type = "PermitExpired",
                    RelatedEntityId = item.Id.ToString(),
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
