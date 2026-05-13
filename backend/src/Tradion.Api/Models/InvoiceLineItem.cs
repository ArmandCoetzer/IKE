namespace Tradion.Api.Models;

/// <summary>Labour or part line on an invoice. Pre-filled from quote but editable for actuals.</summary>
public class InvoiceLineItem
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    /// <summary>Labour or Part</summary>
    public string LineType { get; set; } = "Labour";
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public int SortOrder { get; set; }
    public Guid? PartId { get; set; }
    /// <summary>Source quote line, if pre-filled from quote.</summary>
    public Guid? QuoteLineItemId { get; set; }

    public Invoice Invoice { get; set; } = null!;
    public Part? Part { get; set; }
}
