namespace Ike.Api.DTOs.Tracking;

public class TechnicianLocationDto
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public Guid? JobCardId { get; set; }
    public string? JobCardNumber { get; set; }
    public string? SiteName { get; set; }
    public string? SiteAddress { get; set; }
    public double? SiteLatitude { get; set; }
    public double? SiteLongitude { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime ReportedAt { get; set; }
}
