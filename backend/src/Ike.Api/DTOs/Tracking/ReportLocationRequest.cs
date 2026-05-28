using System.ComponentModel.DataAnnotations;

namespace Ike.Api.DTOs.Tracking;

public class ReportLocationRequest
{
    [Required]
    [Range(-90, 90)]
    public double Latitude { get; set; }

    [Required]
    [Range(-180, 180)]
    public double Longitude { get; set; }

    public double? AccuracyMeters { get; set; }
    public Guid? JobCardId { get; set; }
}
