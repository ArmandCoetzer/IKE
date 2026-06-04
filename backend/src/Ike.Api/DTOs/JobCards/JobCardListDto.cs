namespace Ike.Api.DTOs.JobCards;

public class CreateJobCardRequest
{
    public Guid? ServiceRequestId { get; set; }
    public Guid SiteId { get; set; }
    /// <summary>Optional. Use "Draft" for start-new-job wizard; default "Open".</summary>
    public string? Status { get; set; }
}

public class UpdateJobCardRequest
{
    public Guid? ServiceRequestId { get; set; }
    public string? Status { get; set; }
    public string? Description { get; set; }
    /// <summary>1=least urgent, 5=most urgent.</summary>
    public int? Priority { get; set; }
    public DateTime? DueDate { get; set; }
    public bool? PermitsRequired { get; set; }
    public Guid? RequiredPermitTypeId { get; set; }
    public bool? PartsRequired { get; set; }
    public List<PlannedPartRequest>? PlannedParts { get; set; }
    /// <summary>Permit technicians are currently working on.</summary>
    public Guid? ActiveJobPermitId { get; set; }
}

public class PlannedPartRequest
{
    public Guid PartId { get; set; }
    public int Quantity { get; set; } = 1;
}

public class JobCardListDto
{
    public Guid Id { get; set; }
    public string JobCardNumber { get; set; } = string.Empty;
    public Guid? ServiceRequestId { get; set; }
    public string? ServiceRequestNumber { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid SiteId { get; set; }
    public string? SiteName { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Priority { get; set; } = 3;
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Comma-separated names of assigned technicians.</summary>
    public string? AssignedTechnicianNames { get; set; }
    public string? InvoiceStatus { get; set; }
    /// <summary>When set, job is blocked (highest-priority job cannot be opened until cleared or overridden).</summary>
    public string? BlockedReason { get; set; }
}

public class UpdateJobCardStatusRequest
{
    public string? Status { get; set; }
}

public class BlockJobCardRequest
{
    public string? Reason { get; set; }
}
