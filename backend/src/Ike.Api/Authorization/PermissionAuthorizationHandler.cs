using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Ike.Api.Data;
using Ike.Api.Models;
using Ike.Api.Services;

namespace Ike.Api.Authorization;

public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement, object?>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPermissionService _permissionService;

    public PermissionAuthorizationHandler(UserManager<ApplicationUser> userManager, IPermissionService permissionService)
    {
        _userManager = userManager;
        _permissionService = permissionService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement,
        object? resource)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
            return;

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.IsActive)
            return;

        if (await _permissionService.HasPermissionAsync(user, requirement.PermissionName))
        {
            context.Succeed(requirement);
            return;
        }

        // Backstop for client self-service site visibility when role-permission rows are stale.
        if (requirement.PermissionName == "ViewSites")
        {
            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains(SeedData.RoleClient))
                context.Succeed(requirement);
        }
    }
}
