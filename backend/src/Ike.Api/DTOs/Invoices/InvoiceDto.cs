using Microsoft.AspNetCore.Http;

namespace Ike.Api.DTOs.Invoices;

public class InvoiceLineItemDto
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

public class CreateInvoiceRequest
{
    public Guid JobCardId { get; set; }
    public Guid? QuoteId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid SiteId { get; set; }
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
    public string Currency { get; set; } = "ZAR";
    public string? Notes { get; set; }
    /// <summary>Line items. When QuoteId is provided, pre-filled from quote but editable.</summary>
    public List<InvoiceLineItemDto>? LineItems { get; set; }
}

public class UpdateInvoiceRequest
{
    public DateTime DueDate { get; set; }
    public string? Notes { get; set; }
    /// <summary>When PartsConfirmed is false, line items can be updated.</summary>
    public List<InvoiceLineItemDto>? LineItems { get; set; }
}

public class ConfirmPartsRequest
{
    /// <summary>Optional. Final line items at confirmation. If omitted, current line items are confirmed as-is.</summary>
    public List<InvoiceLineItemDto>? LineItems { get; set; }
}

public class UploadInvoiceRequest
{
    public Guid JobCardId { get; set; }
    public Guid? QuoteId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid SiteId { get; set; }
    public decimal? Amount { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Notes { get; set; }
    public string? LineItemsJson { get; set; }
    public IFormFile? File { get; set; }
}

public class InvoiceUploadPreviewRequest
{
    public Guid JobCardId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid SiteId { get; set; }
    public IFormFile? File { get; set; }
}

public class InvoiceUploadPreviewDto
{
    public string UploadedFileName { get; set; } = string.Empty;
    public string? ExtractedInvoiceNumber { get; set; }
    public string? ExtractedSourceCompanyName { get; set; }
    public string? ExtractedClientName { get; set; }
    public string SelectedClientName { get; set; } = string.Empty;
    public bool ClientNameMatchesSelected { get; set; }
    public string? ExtractedText { get; set; }
    public decimal? ExtractedAmount { get; set; }
    public DateTime? DueDate { get; set; }
    public List<InvoiceUploadPreviewLineDto> LineItems { get; set; } = new();
}

public class InvoiceUploadPreviewLineDto
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

public class SetPaymentPromiseRequest
{
    public DateTime? PromiseToPayBy { get; set; }
}

public class InvoiceLineItemResponseDto
{
    public Guid Id { get; set; }
    public string LineType { get; set; } = "Labour";
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal LineSubtotal => Quantity * UnitPrice;
    public decimal LineDiscountAmount => LineSubtotal * (DiscountPercent < 0 ? 0 : (DiscountPercent > 100 ? 100 : DiscountPercent)) / 100m;
    public decimal LineTotal => LineSubtotal - LineDiscountAmount < 0 ? 0 : LineSubtotal - LineDiscountAmount;
    public Guid? PartId { get; set; }
    public string? PartName { get; set; }
}

public class InvoiceDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid JobCardId { get; set; }
    public string? JobCardNumber { get; set; }
    public Guid? QuoteId { get; set; }
    public string? QuoteNumber { get; set; }
    public Guid? ClientId { get; set; }
    public string? ClientName { get; set; }
    public Guid SiteId { get; set; }
    public string? SiteName { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public string Status { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public int ReminderStage { get; set; }
    public DateTime? LastReminderSentAt { get; set; }
    public DateTime? PromiseToPayBy { get; set; }
    public DateTime? CollectionEscalatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool PartsConfirmed { get; set; }
    public bool IsUploaded { get; set; }
    public string? UploadedFileName { get; set; }
    public string? UploadedContentType { get; set; }
    public DateTime? UploadedAt { get; set; }
    public string? ExtractedInvoiceNumber { get; set; }
    public string? ExtractedText { get; set; }
    public string? Notes { get; set; }
    public List<InvoiceLineItemResponseDto> LineItems { get; set; } = new();
}
