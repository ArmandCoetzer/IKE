namespace Tradion.Api.Services;

public interface INotificationService
{
    Task CreateForUserAsync(string userId, string title, string body, string type, string? relatedEntityId = null, CancellationToken ct = default);
    /// <summary>Creates a notification for every user that has the given permission, optionally excluding one user (e.g. the actor).</summary>
    Task NotifyUsersWithPermissionAsync(string permissionName, string title, string body, string type, string? relatedEntityId = null, string? excludeUserId = null, Guid? scopeCompanyId = null, CancellationToken ct = default);

    /// <summary>Active staff (non-client role) user IDs with the permission, optionally filtered to a main company scope.</summary>
    Task<IReadOnlyList<string>> GetStaffUserIdsWithPermissionAsync(string permissionName, Guid? mainCompanyScopeId, CancellationToken ct = default);
}
