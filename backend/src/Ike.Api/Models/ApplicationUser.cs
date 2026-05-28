using Microsoft.AspNetCore.Identity;

namespace Ike.Api.Models;

public class ApplicationUser : IdentityUser
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string FullName { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Occupation { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? SiteId { get; set; }
    /// <summary> "Invited" = not yet registered; "Registered" = completed invite flow. Null/empty treated as Registered for existing users. </summary>
    public string? RegistrationStatus { get; set; }
    public string? InviteToken { get; set; }
    public DateTime? InviteTokenExpiry { get; set; }
    public Guid? CompanyId { get; set; }

    public Company? Company { get; set; }
    public Site? Site { get; set; }
    public ICollection<ManagerPermission> ManagerPermissions { get; set; } = new List<ManagerPermission>();
}
