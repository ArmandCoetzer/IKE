namespace Tradion.Api.Models;

public class Permission
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public ICollection<ManagerPermission> ManagerPermissions { get; set; } = new List<ManagerPermission>();
}
