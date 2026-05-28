namespace Ike.Api.Models;

public class JobPart
{
    public Guid Id { get; set; }
    public Guid JobCardId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public string? Description { get; set; }
    public string? OldPartPhotoPath { get; set; }
    public string? NewPartPhotoPath { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public JobCard JobCard { get; set; } = null!;
    public ApplicationUser CreatedByUser { get; set; } = null!;
}
