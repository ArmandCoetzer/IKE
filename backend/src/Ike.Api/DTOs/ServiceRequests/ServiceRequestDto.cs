namespace Ike.Api.DTOs.ServiceRequests;

public class CreateServiceRequestRequest
{
    public Guid SiteId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Priority { get; set; }
    public DateTime? OptionalDueDate { get; set; }
}

public class ServiceRequestDto
{
    public Guid Id { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public Guid SiteId { get; set; }
    public Guid? CompanyId { get; set; }
    public string? SiteName { get; set; }
    public string? RequestedByUserName { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? OptionalDueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? JobCardId { get; set; }
    public string? JobCardNumber { get; set; }
    public string? JobCardStatus { get; set; }
    public string? AssignedTechnicianNames { get; set; }
    public decimal? PenaltyFee { get; set; }
    public string? PenaltyNote { get; set; }
    public List<ServiceRequestAttachmentDto> Attachments { get; set; } = new();
}

public class ServiceRequestAttachmentDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class UpdateStatusRequest
{
    public string? Status { get; set; }
}

public class UpdateServiceRequestRequest
{
    public Guid SiteId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Priority { get; set; }
    public DateTime? OptionalDueDate { get; set; }
    public decimal? PenaltyFee { get; set; }
    public string? PenaltyNote { get; set; }
}
