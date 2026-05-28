namespace Ike.Api.Models;

public class PurchaseOrder
{
    public Guid Id { get; set; }
    public string PONumber { get; set; } = string.Empty;
    public string? ClientPONumber { get; set; }
    /// <summary>Relative path to uploaded client PO file (e.g. PDF).</summary>
    public string? ClientPOFilePath { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? JobCardId { get; set; }
    public Guid? ServiceRequestId { get; set; }
    public Guid? QuoteId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string CreatedById { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Company Company { get; set; } = null!;
    public Site Site { get; set; } = null!;
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public Quote? Quote { get; set; }
    public ICollection<JobCardDocument> JobCardDocuments { get; set; } = new List<JobCardDocument>();
}
