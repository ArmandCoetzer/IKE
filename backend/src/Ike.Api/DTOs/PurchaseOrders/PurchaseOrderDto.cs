namespace Ike.Api.DTOs.PurchaseOrders;

public class CreatePurchaseOrderRequest
{
    public Guid ClientId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? JobCardId { get; set; }
    public Guid? ServiceRequestId { get; set; }
    public Guid? QuoteId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public string? ClientPONumber { get; set; }
    public string? Notes { get; set; }
}

public class PurchaseOrderDto
{
    public Guid Id { get; set; }
    public string PONumber { get; set; } = string.Empty;
    public string? ClientPONumber { get; set; }
    public bool HasClientPOFile { get; set; }
    public Guid ClientId { get; set; }
    public string? ClientName { get; set; }
    public Guid SiteId { get; set; }
    public string? SiteName { get; set; }
    public Guid? JobCardId { get; set; }
    public Guid? ServiceRequestId { get; set; }
    public Guid? QuoteId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UpdatePurchaseOrderStatusRequest
{
    public string? Status { get; set; }
}

public class UpdatePurchaseOrderRequest
{
    public string? ClientPONumber { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public string? Notes { get; set; }
}
