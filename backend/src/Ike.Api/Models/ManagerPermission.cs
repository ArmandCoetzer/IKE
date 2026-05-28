namespace Ike.Api.Models;

public class ManagerPermission
{
    public string ManagerUserId { get; set; } = string.Empty;
    public Guid PermissionId { get; set; }
    public bool Allowed { get; set; }

    public ApplicationUser ManagerUser { get; set; } = null!;
    public Permission Permission { get; set; } = null!;
}
