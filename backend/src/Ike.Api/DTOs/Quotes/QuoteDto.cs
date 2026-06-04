using Microsoft.AspNetCore.Http;

namespace Ike.Api.DTOs.Quotes;

public class QuoteLineItemDto
{
    public string LineType { get; set; } = "Labour";
    public string? Code { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public Guid? PartId { get; set; }
    public bool AddMissingItemToSystem { get; set; }
}

public class CreateQuoteRequest
{
    public Guid ClientId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? JobCardId { get; set; }
    public Guid? ServiceRequestId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public string Description { get; set; } = string.Empty;
    public bool DeferPricing { get; set; }
    public string DiscountMode { get; set; } = "None";
    public decimal GlobalDiscountPercent { get; set; }
    public string? Notes { get; set; }
    public DateTime? ValidUntil { get; set; }
    public List<QuoteLineItemDto>? LineItems { get; set; }
}

public class QuoteLineItemResponseDto
{
    public Guid Id { get; set; }
    public string LineType { get; set; } = "Labour";
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal LineSubtotal => Quantity * UnitPrice;
    public decimal LineDiscountAmount => Math.Round(LineSubtotal * (Math.Clamp(DiscountPercent, 0m, 100m) / 100m), 2, MidpointRounding.AwayFromZero);
    public decimal LineTotal => Math.Round(LineSubtotal - LineDiscountAmount, 2, MidpointRounding.AwayFromZero);
    public Guid? PartId { get; set; }
    public string? PartName { get; set; }
}

public class QuoteDto
{
    public Guid Id { get; set; }
    public string QuoteNumber { get; set; } = string.Empty;
    public Guid ClientId { get; set; }
    public string? ClientName { get; set; }
    public Guid SiteId { get; set; }
    public string? SiteName { get; set; }
    public Guid? JobCardId { get; set; }
    public Guid? ServiceRequestId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public string Description { get; set; } = string.Empty;
    public bool DeferPricing { get; set; }
    public bool IsUploaded { get; set; }
    public string? UploadedFileName { get; set; }
    public string? UploadedContentType { get; set; }
    public DateTime? UploadedAt { get; set; }
    public string? ExtractedQuoteNumber { get; set; }
    public string? ExtractedSupplierName { get; set; }
    public string? ExtractedText { get; set; }
    public string DiscountMode { get; set; } = "None";
    public decimal GlobalDiscountPercent { get; set; }
    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? ValidUntil { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? LinkedPurchaseOrderId { get; set; }
    public string? LinkedPurchaseOrderNumber { get; set; }
    public List<QuoteLineItemResponseDto> LineItems { get; set; } = new();
}

public class UpdateQuoteStatusRequest
{
    public string? Status { get; set; }
}

public class UpdateQuoteRequest
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public string Description { get; set; } = string.Empty;
    public string DiscountMode { get; set; } = "None";
    public decimal GlobalDiscountPercent { get; set; }
    public string? Notes { get; set; }
    public DateTime? ValidUntil { get; set; }
    public List<QuoteLineItemDto>? LineItems { get; set; }
}

public class UploadQuoteRequest
{
    public Guid ClientId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? JobCardId { get; set; }
    public Guid? ServiceRequestId { get; set; }
    public decimal? Amount { get; set; }
    public decimal? GlobalDiscountPercent { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string? LineItemsJson { get; set; }
    public IFormFile? File { get; set; }
}

public class QuoteUploadPreviewRequest
{
    public Guid ClientId { get; set; }
    public Guid SiteId { get; set; }
    public IFormFile? File { get; set; }
}

public class QuoteUploadPreviewDto
{
    public string UploadedFileName { get; set; } = string.Empty;
    public string? ExtractedQuoteNumber { get; set; }
    public string? ExtractedSupplierName { get; set; }
    public string? ExtractedSourceCompanyName { get; set; }
    public string? ExtractedClientName { get; set; }
    public string SelectedClientName { get; set; } = string.Empty;
    public bool ClientNameMatchesSelected { get; set; }
    public string? ExtractedText { get; set; }
    public decimal? ExtractedAmount { get; set; }
    public decimal? OverallDiscountPercent { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime? ValidUntil { get; set; }
    public List<QuoteUploadPreviewLineDto> LineItems { get; set; } = new();
}

public class QuoteUploadPreviewLineDto
{
    public string LineType { get; set; } = "Part";
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal VatPercent { get; set; }
    public decimal ExclTotal { get; set; }
    public decimal InclTotal { get; set; }
    public Guid? SuggestedPartId { get; set; }
    public string? SuggestedPartName { get; set; }
    public string MatchStatus { get; set; } = "Unmatched";
}

public class LinkQuoteToJobCardRequest
{
    public Guid JobCardId { get; set; }
}
