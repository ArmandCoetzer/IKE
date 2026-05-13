namespace Tradion.Api.Models;

public class ServiceRequest
{
    public Guid Id { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public Guid SiteId { get; set; }
    public string RequestedByUserId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? OptionalDueDate { get; set; }
    public decimal? PenaltyFee { get; set; }
    public string? PenaltyNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Site Site { get; set; } = null!;
    public ApplicationUser RequestedByUser { get; set; } = null!;
    public ICollection<ServiceRequestAttachment> Attachments { get; set; } = new List<ServiceRequestAttachment>();
}
