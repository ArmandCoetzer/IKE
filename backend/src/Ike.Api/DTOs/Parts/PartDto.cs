namespace Ike.Api.DTOs.Parts;

public class PartDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? PartNumber { get; set; }
    public int Quantity { get; set; }
    public int ReservedForActiveJobsQuantity { get; set; }
    public int AvailableQuantity => Math.Max(0, Quantity - ReservedForActiveJobsQuantity);
    public int ReorderLevel { get; set; }
    public bool IsLowStock =>
        !IsLabour
        && (Quantity <= 0
            || ReservedForActiveJobsQuantity > Quantity
            || (ReorderLevel > 0 && AvailableQuantity <= ReorderLevel * 0.25m));
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

public class PartImportRowDto
{
    public int RowNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? PartNumber { get; set; }
    public int Quantity { get; set; }
    public int ReorderLevel { get; set; }
    public string? Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public bool IsLabour { get; set; }
    public List<string> Errors { get; set; } = new();
    public Guid? CreatedPartId { get; set; }
}

public class PartImportCommitRequest
{
    public List<PartImportRowDto> Rows { get; set; } = new();
}

public class PartImportResultDto
{
    public List<PartImportRowDto> Rows { get; set; } = new();
    public List<PartImportRowDto> FailedRows { get; set; } = new();
    public int TotalRows { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
}
