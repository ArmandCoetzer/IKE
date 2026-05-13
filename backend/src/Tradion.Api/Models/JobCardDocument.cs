namespace Tradion.Api.Models;

public class JobCardDocument
{
    public Guid Id { get; set; }
    public Guid JobCardId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; }
    public string SignedByUserId { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public string? Notes { get; set; }
    public Guid? PurchaseOrderId { get; set; }

    public JobCard JobCard { get; set; } = null!;
    public ApplicationUser SignedByUser { get; set; } = null!;
    public PurchaseOrder? PurchaseOrder { get; set; }
}
