namespace Tradion.Api.Models;

/// <summary>
/// Inventory part / stock item. Tracks quantity and reorder level; can link to supplier for "low stock → supplier quote → our PO".
/// </summary>
public class Part
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? PartNumber { get; set; }
    public int Quantity { get; set; }
    public int ReorderLevel { get; set; }
    public decimal UnitPrice { get; set; }
    public bool IsLabour { get; set; }
    public Guid? SupplierId { get; set; }
    public string? Unit { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Main company that owns this part. Null = unscoped/legacy.</summary>
    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }

    public Supplier? Supplier { get; set; }
    public ICollection<PartSupplier> Suppliers { get; set; } = new List<PartSupplier>();
}
