namespace Ike.Api.DTOs.Parts;

public class PartDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? PartNumber { get; set; }
    public int Quantity { get; set; }
    public int ReorderLevel { get; set; }
    public bool IsLowStock => Quantity <= ReorderLevel;
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public bool HasSupplierEmail { get; set; }
    public List<Guid> SupplierIds { get; set; } = new();
    public List<string> SupplierNames { get; set; } = new();
    public string? Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public bool IsLabour { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreatePartRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? PartNumber { get; set; }
    public int Quantity { get; set; }
    public int ReorderLevel { get; set; }
    public Guid? SupplierId { get; set; }
    public List<Guid>? SupplierIds { get; set; }
    public string? Unit { get; set; }
    public decimal? UnitPrice { get; set; }
    public bool IsLabour { get; set; } = false;
}

public class UpdatePartRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? PartNumber { get; set; }
    public int? Quantity { get; set; }
    public int? ReorderLevel { get; set; }
    public Guid? SupplierId { get; set; }
    public List<Guid>? SupplierIds { get; set; }
    public string? Unit { get; set; }
    public decimal? UnitPrice { get; set; }
    public bool? IsLabour { get; set; }
}
