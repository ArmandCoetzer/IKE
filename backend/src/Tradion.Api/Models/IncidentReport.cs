namespace Tradion.Api.Models;

public class IncidentReport
{
    public Guid Id { get; set; }
    public Guid JobCardId { get; set; }
    public string ReportedByUserId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string? PhotosJson { get; set; }
    /// <summary>e.g. Open, Resolved.</summary>
    public string Status { get; set; } = "Open";
    /// <summary>Resolution details when Status is Resolved.</summary>
    public string? Resolution { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public JobCard JobCard { get; set; } = null!;
    public ApplicationUser ReportedByUser { get; set; } = null!;
}
