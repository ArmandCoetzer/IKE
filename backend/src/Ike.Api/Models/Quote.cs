namespace Ike.Api.Models;

public class Quote
{
    public Guid Id { get; set; }
    public string QuoteNumber { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? JobCardId { get; set; }
    public Guid? ServiceRequestId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public string Description { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool DeferPricing { get; set; }
    public bool IsUploaded { get; set; }
    public string? UploadedFilePath { get; set; }
    public string? UploadedFileName { get; set; }
    public string? UploadedContentType { get; set; }
    public DateTime? UploadedAt { get; set; }
    public string? ExtractedQuoteNumber { get; set; }
    public string? ExtractedSupplierName { get; set; }
    public string? ExtractedText { get; set; }
    /// <summary>None | Global | PerItem</summary>
    public string DiscountMode { get; set; } = "None";
    /// <summary>Used when DiscountMode is Global, value between 0 and 100.</summary>
    public decimal GlobalDiscountPercent { get; set; }
    public DateTime? ValidUntil { get; set; }
    public DateTime? SentAt { get; set; }
    public string CreatedById { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Company Company { get; set; } = null!;
    public Site Site { get; set; } = null!;
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public ICollection<QuoteLineItem> LineItems { get; set; } = new List<QuoteLineItem>();
}
