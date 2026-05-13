namespace Tradion.Api.Models;

/// <summary>Labour or part line on a quote.</summary>
public class QuoteLineItem
{
    public Guid Id { get; set; }
    public Guid QuoteId { get; set; }
    /// <summary>Labour or Part</summary>
    public string LineType { get; set; } = "Labour";
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>Used when quote DiscountMode is PerItem, value between 0 and 100.</summary>
    public decimal DiscountPercent { get; set; }
    public int SortOrder { get; set; }
    /// <summary>When LineType is Part, optional link to inventory part.</summary>
    public Guid? PartId { get; set; }

    public Quote Quote { get; set; } = null!;
    public Part? Part { get; set; }
}
