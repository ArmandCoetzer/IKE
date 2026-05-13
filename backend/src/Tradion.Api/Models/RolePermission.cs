using Microsoft.AspNetCore.Identity;

namespace Tradion.Api.Models;

public class RolePermission
{
    public string RoleId { get; set; } = string.Empty;
    public Guid PermissionId { get; set; }

    public IdentityRole Role { get; set; } = null!;
    public Permission Permission { get; set; } = null!;
}
