using Tradion.Api.Models;

namespace Tradion.Api.DTOs.JobCardWork;

public class JobCardWorkDto
{
    public Guid Id { get; set; }
    public string JobCardNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? FirstPermitRequestedAt { get; set; }
    public DateTime? FirstPermitApprovedAt { get; set; }
    public DateTime? FirstSitePhotoAt { get; set; }
    public string? Description { get; set; }
    public Guid? ServiceRequestId { get; set; }
    public string? ServiceRequestNumber { get; set; }
    public string? ServiceRequestDescription { get; set; }
    public Guid? QuoteId { get; set; }
    public string? QuoteNumber { get; set; }
    public decimal? QuoteAmount { get; set; }
    public string? QuoteDescription { get; set; }
    /// <summary>Draft, Sent, Accepted, or Cancelled.</summary>
    public string? QuoteStatus { get; set; }
    public Guid SiteId { get; set; }
    public string? SiteName { get; set; }
    public string? SiteAddress { get; set; }
    public double? SiteLatitude { get; set; }
    public double? SiteLongitude { get; set; }
    public List<Guid> RequiredBadgeIds { get; set; } = new();
    public List<string> RequiredBadgeNames { get; set; } = new();
    public string Status { get; set; } = string.Empty;
    public int Priority { get; set; } = 3;
    public DateTime? DueDate { get; set; }
    public string? Notes { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public List<LinkedPoDto> LinkedPOs { get; set; } = new();
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? InvoiceStatus { get; set; }
    public List<JobCardDocumentDto> Documents { get; set; } = new();
    public List<JobPartDto> Parts { get; set; } = new();
    public List<JobPermitDto> Permits { get; set; } = new();
    public List<IncidentReportDto> IncidentReports { get; set; } = new();
    public Guid? CompanyId { get; set; }
    public bool PermitsRequired { get; set; }
    public Guid? RequiredPermitTypeId { get; set; }
    public string? RequiredPermitTypeName { get; set; }
    /// <summary>Legacy: always empty; permit types come from the job’s required permit type.</summary>
    public List<Guid> RequiredPermitTypeIdsFromEquipment { get; set; } = new();
    public bool PartsRequired { get; set; }
    public List<PlannedPartDto> PlannedParts { get; set; } = new();
    public List<JobCardAssignmentDto> Assignments { get; set; } = new();
    public Guid? ActivePermitId { get; set; }
    public string? ActivePermitName { get; set; }
    /// <summary>Client budget for the job's site company, when applicable.</summary>
    public JobCardBudgetSummaryDto? Budget { get; set; }
    /// <summary>When set, job is blocked (technicians cannot open even if highest priority).</summary>
    public string? BlockedReason { get; set; }

    /// <summary>True when the Work Authorisation was amended after client sign-off and must be signed again.</summary>
    public bool PendingWaAmendmentSignOff { get; set; }

    /// <summary>True when work is paused because no valid signed Work Authorisation is currently in force.</summary>
    public bool WaExpiredStandstill { get; set; }

    /// <summary>Job uses paper permit workflow (no digital WA on mobile).</summary>
    public bool PaperPermitMode { get; set; }

    /// <summary>True when the current user may switch this job to paper mode (before any visible WA has client sign-off).</summary>
    public bool CanActivatePaperPermitMode { get; set; }

    /// <summary>On-site acknowledgement that the client accepts completed work; required before status may move to completed.</summary>
    public DateTime? FinalClientSignOffAt { get; set; }

    /// <summary>Display name for <see cref="FinalClientSignOffAt"/> recorder (technician).</summary>
    public string? FinalClientSignOffByName { get; set; }

    /// <summary>Optional client print name captured with the signature (stored on the sign-off document).</summary>
    public string? FinalClientSignerName { get; set; }

    /// <summary>SHA-256 hash for final sign-off evidence file integrity.</summary>
    public string? FinalClientSignOffFileSha256 { get; set; }

    /// <summary>Capture source for final sign-off evidence.</summary>
    public string? FinalClientSignOffCaptureSource { get; set; }

    /// <summary>Latest chained evidence hash for final sign-off audit.</summary>
    public string? FinalClientSignOffEvidenceHash { get; set; }

    public DateTime? FinalClientSignOffEvidenceRecordedAt { get; set; }

    public string? FinalClientSignOffDeviceId { get; set; }

    public string? FinalClientSignOffAppVersion { get; set; }
}

public class JobCardBudgetSummaryDto
{
    public decimal ThresholdAmount { get; set; }
    public decimal SpentAmount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public bool WorkPaused { get; set; }
}

public class JobCardAssignmentDto
{
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public DateTime AssignedAt { get; set; }
    public bool IsPermitManager { get; set; }
    public List<Guid> BadgeIds { get; set; } = new();
}

public class PlannedPartDto
{
    public Guid Id { get; set; }
    public Guid PartId { get; set; }
    public string PartName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int StockQuantity { get; set; }
    public int ReorderLevel { get; set; }
    public bool IsLowStock => StockQuantity <= ReorderLevel;
}

public class JobCardDocumentDto
{
    public Guid Id { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; }
    public string? SignedByUserName { get; set; }
    public string? Notes { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public string? FilePath { get; set; }
}

public class JobPartDto
{
    public Guid Id { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public string? Description { get; set; }
    public string? OldPartPhotoPath { get; set; }
    public string? NewPartPhotoPath { get; set; }
}

public class JobPermitAttachmentDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
}

public class JobPermitDto
{
    public Guid Id { get; set; }
    public int PermitNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    /// <summary>Permit type id for this row (child vs master checklist).</summary>
    public Guid? PermitTypeId { get; set; }
    public string? PermitTemplateName { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? ValidFrom { get; set; }
    /// <summary>
    /// Validity end for UI/countdown: only set once the permit has client/site sign-off (digital or paper).
    /// Omitted earlier so clients do not show a valid-to end time before the clock starts.
    /// </summary>
    public DateTime? ValidTo { get; set; }
    public Guid? MasterPermitId { get; set; }
    public bool IsWorkAuthorisation { get; set; }
    /// <summary>True when site/client acknowledgement signature is present on the master permit payload.</summary>
    public bool HasClientSignOff { get; set; }
    public List<Guid>? TriggersPermitTypeIds { get; set; }
    /// <summary>Names for TriggersPermitTypeIds, for mobile to show child permit options.</summary>
    public List<string> TriggersPermitTypeNames { get; set; } = new();
    /// <summary>
    /// Work permits still requestable from this master, derived from saved Work Authorisation checklist (not admin triggers).
    /// </summary>
    public List<Guid>? RequestableWorkPermitTypeIds { get; set; }
    public List<string> RequestableWorkPermitTypeNames { get; set; } = new();
    public List<JobPermitAttachmentDto> Attachments { get; set; } = new();
    /// <summary>Work Authorisation checklist items from PermitTemplate.ChecklistJson + completed state from ChecklistSnapshotJson.</summary>
    public List<ChecklistItemDto> ChecklistItems { get; set; } = new();

    /// <summary>WA amended after sign-off; cleared when client signs off again.</summary>
    public bool PendingWaAmendmentSignOff { get; set; }

    /// <summary>
    /// For child permits under a WA: whether the saved master checklist still requires this permit type.
    /// Null for Work Authorisation rows or non-child permits.
    /// </summary>
    public bool? StillRequiredByWorkAuthorisation { get; set; }

    /// <summary>Child permit structured form schema (permit template FormSchemaJson).</summary>
    public List<PermitFormFieldSchemaDto>? FormFields { get; set; }

    /// <summary>Submitted structured form values (job permit FormSnapshotJson).</summary>
    public Dictionary<string, string>? FormValues { get; set; }

    /// <summary>Physical permit reference when <see cref="JobCardWorkDto.PaperPermitMode"/> is true.</summary>
    public string? PaperPermitNumber { get; set; }

    public DateTime? PaperClientSignedOffAt { get; set; }
}

public class ChecklistItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Checked { get; set; }
}

public class IncidentReportDto
{
    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = IncidentStatus.Open;
    public string? Resolution { get; set; }
    public List<string> PhotoPaths { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class LinkedPoDto
{
    public Guid Id { get; set; }
    public string PONumber { get; set; } = string.Empty;
    public string? ClientPONumber { get; set; }
}

public class SubmitDocumentRequest
{
    public string DocumentType { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public Guid? PurchaseOrderId { get; set; }
}

public class CreateIncidentRequest
{
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = "Medium";
    /// <summary>When true, sets the job card blocked reason like the web Block job action (coordinator must unblock).</summary>
    public bool BlockJob { get; set; }
}

public class UpdateIncidentRequest
{
    public string? Status { get; set; }
    public string? Resolution { get; set; }
}

public class ActivatePaperPermitModeRequest
{
    /// <summary>Must be true. Enables paper mode and hides existing permit rows from the UI (rows remain in the database).</summary>
    public bool Enable { get; set; }
}
