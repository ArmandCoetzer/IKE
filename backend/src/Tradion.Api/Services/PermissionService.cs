using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tradion.Api.Data;
using Tradion.Api.Models;

namespace Tradion.Api.Services;

public class PermissionService : IPermissionService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public PermissionService(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<IReadOnlyList<string>> GetEffectivePermissionNamesAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Count == 0)
            return Array.Empty<string>();

        var roleIds = await _db.Roles.Where(r => roles.Contains(r.Name!)).Select(r => r.Id).ToListAsync();
        var rolePermissionIds = await _db.RolePermissions
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.PermissionId)
            .Distinct()
            .ToListAsync();

        var permissionIds = rolePermissionIds.ToHashSet();

        if (roles.Contains(SeedData.RoleManager))
        {
            var revoked = await _db.ManagerPermissions
                .Where(mp => mp.ManagerUserId == user.Id && !mp.Allowed)
                .Select(mp => mp.PermissionId)
                .ToListAsync();
            foreach (var id in revoked)
                permissionIds.Remove(id);
        }

        var names = await _db.Permissions.Where(p => permissionIds.Contains(p.Id)).Select(p => p.Name).ToListAsync();
        return names;
    }

    public async Task<bool> HasPermissionAsync(ApplicationUser user, string permissionName)
    {
        var permissions = await GetEffectivePermissionNamesAsync(user);
        return permissions.Contains(permissionName);
    }

    public async Task<IReadOnlyList<string>> GetUserIdsWithPermissionAsync(string permissionName, CancellationToken ct = default)
    {
        var permission = await _db.Permissions.AsNoTracking().FirstOrDefaultAsync(p => p.Name == permissionName, ct);
        if (permission == null)
            return Array.Empty<string>();

        var roleIds = await _db.RolePermissions
            .Where(rp => rp.PermissionId == permission.Id)
            .Select(rp => rp.RoleId)
            .Distinct()
            .ToListAsync(ct);
        if (roleIds.Count == 0)
            return Array.Empty<string>();

        var userIds = await _db.UserRoles
            .Where(ur => roleIds.Contains(ur.RoleId))
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync(ct);
        if (userIds.Count == 0)
            return Array.Empty<string>();

        var revoked = await _db.ManagerPermissions
            .Where(mp => mp.PermissionId == permission.Id && !mp.Allowed)
            .Select(mp => mp.ManagerUserId)
            .Distinct()
            .ToListAsync(ct);
        var revokedSet = revoked.ToHashSet();
        var active = await _db.Users
            .Where(u => userIds.Contains(u.Id) && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync(ct);
        return active.Where(id => !revokedSet.Contains(id)).ToList();
    }
}
