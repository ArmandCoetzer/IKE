namespace Tradion.Api.Models;

public class PermitType
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    /// <summary>Admin (main company) that owns this permit type. Null = unscoped/legacy.</summary>
    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }

    /// <summary>Work Authorisation / master permit. When approved, it drives other permit types.</summary>
    public bool IsWorkAuthorisation { get; set; }

    /// <summary>When this permit is approved, these permit type IDs become available/required. JSON array of Guid.</summary>
    public string? TriggersPermitTypeIdsJson { get; set; }

    public ICollection<PermitTemplate> Templates { get; set; } = new List<PermitTemplate>();
}
