namespace Tradion.Api.Models;

public class PermitTemplate
{
    public Guid Id { get; set; }
    public Guid PermitTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? SiteId { get; set; }
    public Guid? CompanyId { get; set; }
    public string? ChecklistJson { get; set; }

    /// <summary>JSON array of structured fields (id, label, type, group, required) for child permit forms. WA uses Work Authorisation JSON instead.</summary>
    public string? FormSchemaJson { get; set; }

    public string? ValidityRulesJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    public PermitType PermitType { get; set; } = null!;
    public Site? Site { get; set; }
    public Company? Company { get; set; }
    public ICollection<JobPermit> JobPermits { get; set; } = new List<JobPermit>();
}
