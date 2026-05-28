using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Ike.Api.Data;
using Ike.Api.Models;

namespace Ike.Api.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public string? UserId => _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public async Task<(Guid? CompanyId, bool IsClient)> GetClientScopeAsync(CancellationToken ct = default)
    {
        var uid = UserId;
        if (string.IsNullOrEmpty(uid))
            return (null, false);
        var user = await _userManager.FindByIdAsync(uid);
        if (user == null)
            return (null, false);
        var roles = await _userManager.GetRolesAsync(user);
        var isClient = roles.Contains(SeedData.RoleClient);
        return (user.CompanyId, isClient);
    }
}
