namespace Ike.Api.Models;

public class PartSupplier
{
    public Guid PartId { get; set; }
    public Guid SupplierId { get; set; }
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;

    public Part Part { get; set; } = null!;
    public Supplier Supplier { get; set; } = null!;
}
