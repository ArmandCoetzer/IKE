namespace Ike.Api.Models;

/// <summary>GPS position reported by a technician (from the mobile app). Used for live tracking on the admin map.</summary>
public class TechnicianLocation
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid? JobCardId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
    public DateTime ReportedAt { get; set; } = DateTime.UtcNow;

    public ApplicationUser User { get; set; } = null!;
    public JobCard? JobCard { get; set; }
}
