using Tradion.Api.Data;
using Tradion.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Tradion.Api.Services;

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _db;
    private readonly IPermissionService _permissionService;
    private readonly IRealtimeHub _realtimeHub;

    public NotificationService(ApplicationDbContext db, IPermissionService permissionService, IRealtimeHub realtimeHub)
    {
        _db = db;
        _permissionService = permissionService;
        _realtimeHub = realtimeHub;
    }

    public async Task CreateForUserAsync(string userId, string title, string body, string type, string? relatedEntityId = null, CancellationToken ct = default)
    {
        var n = NewNotification(userId, title, body, type, relatedEntityId);
        _db.Notifications.Add(n);
        await _db.SaveChangesAsync(ct);
        await _realtimeHub.NotifyUserNotificationAsync(userId, ct);
    }

    public Task<IReadOnlyList<string>> GetStaffUserIdsWithPermissionAsync(string permissionName, Guid? mainCompanyScopeId, CancellationToken ct = default) =>
        ResolveStaffUserIdsWithPermissionAsync(permissionName, mainCompanyScopeId, ct);

    private async Task<IReadOnlyList<string>> ResolveStaffUserIdsWithPermissionAsync(string permissionName, Guid? mainCompanyScopeId, CancellationToken ct)
    {
        var userIds = await _permissionService.GetUserIdsWithPermissionAsync(permissionName, ct);
        userIds = await FilterUsersByScopeCompanyAsync(userIds, mainCompanyScopeId, ct);
        userIds = await FilterOutClientUsersAsync(userIds, ct);
        return userIds;
    }

    public async Task NotifyUsersWithPermissionAsync(string permissionName, string title, string body, string type, string? relatedEntityId = null, string? excludeUserId = null, Guid? scopeCompanyId = null, CancellationToken ct = default)
    {
        var userIds = await ResolveStaffUserIdsWithPermissionAsync(permissionName, scopeCompanyId, ct);
        var exclude = string.IsNullOrEmpty(excludeUserId) ? null : excludeUserId;
        var toNotify = exclude == null ? userIds : userIds.Where(id => id != exclude).ToList();
        foreach (var uid in toNotify)
        {
            _db.Notifications.Add(NewNotification(uid, title, body, type, relatedEntityId));
        }
        if (toNotify.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            foreach (var uid in toNotify)
                await _realtimeHub.NotifyUserNotificationAsync(uid, ct);
        }
    }

    private async Task<List<string>> FilterUsersByScopeCompanyAsync(IReadOnlyList<string> userIds, Guid? scopeCompanyId, CancellationToken ct)
    {
        if (!scopeCompanyId.HasValue || userIds.Count == 0)
            return userIds.ToList();

        return await _db.Users.AsNoTracking()
            .Include(u => u.Company)
            .Where(u =>
                userIds.Contains(u.Id)
                && u.IsActive
                && u.CompanyId.HasValue
                && (u.CompanyId == scopeCompanyId.Value
                    || (u.Company != null && u.Company.ParentCompanyId == scopeCompanyId.Value)))
            .Select(u => u.Id)
            .Distinct()
            .ToListAsync(ct);
    }

    private async Task<List<string>> FilterOutClientUsersAsync(IReadOnlyList<string> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
            return userIds.ToList();

        var clientRoleId = await _db.Roles.AsNoTracking()
            .Where(r => r.Name == SeedData.RoleClient)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(clientRoleId))
            return userIds.ToList();

        var clientUserIds = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.RoleId == clientRoleId && userIds.Contains(ur.UserId))
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync(ct);
        if (clientUserIds.Count == 0)
            return userIds.ToList();

        var clientUserIdSet = clientUserIds.ToHashSet(StringComparer.Ordinal);
        return userIds.Where(id => !clientUserIdSet.Contains(id)).ToList();
    }

    private static Notification NewNotification(string userId, string title, string body, string type, string? relatedEntityId)
    {
        return new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title.Length > 256 ? title[..256] : title,
            Body = body.Length > 2000 ? body[..2000] : body,
            Type = type.Length > 64 ? type[..64] : type,
            RelatedEntityId = relatedEntityId?.Length > 128 ? relatedEntityId[..128] : relatedEntityId,
            CreatedAt = DateTime.UtcNow
        };
    }
}
