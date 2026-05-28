namespace Ike.Api.Models;

public class RiskAlert
{
    public Guid Id { get; set; }
    public Guid? CompanyId { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = "Medium";
    public string Title { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public DateTime FirstDetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastDetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedByUserId { get; set; }

    public Company? Company { get; set; }
    public ApplicationUser? ResolvedByUser { get; set; }
}
