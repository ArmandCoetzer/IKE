namespace Tradion.Api.Models;

public class SupplierQuoteRequest
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public Guid? PartId { get; set; }
    public Guid? JobCardId { get; set; }
    public int? RequestedQuantity { get; set; }
    public string Status { get; set; } = SupplierQuoteRequestStatus.Requested;
    public string? Notes { get; set; }
    public string CreatedById { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Supplier Supplier { get; set; } = null!;
    public Part? Part { get; set; }
    public JobCard? JobCard { get; set; }
    public ApplicationUser CreatedByUser { get; set; } = null!;
}
