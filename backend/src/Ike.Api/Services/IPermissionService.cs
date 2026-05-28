using Ike.Api.Models;

namespace Ike.Api.Services;

public interface IPermissionService
{
    Task<IReadOnlyList<string>> GetEffectivePermissionNamesAsync(ApplicationUser user);
    Task<bool> HasPermissionAsync(ApplicationUser user, string permissionName);
    /// <summary>Returns user IDs that have the given permission (via role, excluding manager revocations), and are active.</summary>
    Task<IReadOnlyList<string>> GetUserIdsWithPermissionAsync(string permissionName, CancellationToken ct = default);
}
