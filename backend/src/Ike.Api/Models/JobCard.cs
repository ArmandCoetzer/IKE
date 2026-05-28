namespace Ike.Api.Models;

public class JobCard
{
    public Guid Id { get; set; }
    public string JobCardNumber { get; set; } = string.Empty;
    public Guid? ServiceRequestId { get; set; }
    public Guid SiteId { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    /// <summary>1=least urgent, 5=most urgent.</summary>
    public int Priority { get; set; } = 3;
    public DateTime? DueDate { get; set; }
    public string CreatedById { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    /// <summary>True if this job requires a permit before work can start.</summary>
    public bool PermitsRequired { get; set; }
    /// <summary>When PermitsRequired, which permit type is needed.</summary>
    public Guid? RequiredPermitTypeId { get; set; }
    /// <summary>True if this job will use parts from stock.</summary>
    public bool PartsRequired { get; set; }
    /// <summary>Permit technicians are currently working on.</summary>
    public Guid? ActiveJobPermitId { get; set; }
    /// <summary>When set, job is blocked (client not on site, missing permits, parts, etc.). Technicians cannot open even if highest priority.</summary>
    public string? BlockedReason { get; set; }
    /// <summary>When the job was blocked.</summary>
    public DateTime? BlockedAt { get; set; }
    /// <summary>Who set the block (or cleared it).</summary>
    public string? BlockedByUserId { get; set; }

    /// <summary>When true, permits use paper workflow (no digital WA form/signatures on mobile); existing rows may be hidden via <see cref="JobPermit.HiddenFromUiForHistory"/>.</summary>
    public bool PaperPermitMode { get; set; }

    public DateTime? PaperModeActivatedAt { get; set; }

    public string? PaperModeActivatedByUserId { get; set; }

    /// <summary>When the final client signature was stored (paired with a <see cref="JobCardDocument"/> of type FinalClientSignOff).</summary>
    public DateTime? FinalClientSignOffAt { get; set; }

    /// <summary>Technician (or coordinator) who uploaded/captured the final client signature.</summary>
    public string? FinalClientSignOffByUserId { get; set; }

    /// <summary>SHA-256 hash (hex) of the final sign-off signature file for evidence integrity checks.</summary>
    public string? FinalClientSignOffFileSha256 { get; set; }

    /// <summary>Capture source label for final sign-off evidence (for audit context).</summary>
    public string? FinalClientSignOffCaptureSource { get; set; }

    /// <summary>Latest chained evidence hash for final client sign-off.</summary>
    public string? FinalClientSignOffEvidenceHash { get; set; }

    public DateTime? FinalClientSignOffEvidenceRecordedAt { get; set; }

    public ServiceRequest? ServiceRequest { get; set; }
    public Site Site { get; set; } = null!;
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public ICollection<JobCardAssignment> Assignments { get; set; } = new List<JobCardAssignment>();
    public ICollection<JobCardDocument> Documents { get; set; } = new List<JobCardDocument>();
    public ICollection<JobPart> Parts { get; set; } = new List<JobPart>();
    public ICollection<JobPermit> Permits { get; set; } = new List<JobPermit>();
    public JobPermit? ActiveJobPermit { get; set; }
    public ICollection<IncidentReport> IncidentReports { get; set; } = new List<IncidentReport>();
    public PermitType? RequiredPermitType { get; set; }
    public ICollection<JobCardPlannedPart> PlannedParts { get; set; } = new List<JobCardPlannedPart>();
    public ICollection<JobCardSignOffEvidence> SignOffEvidenceRecords { get; set; } = new List<JobCardSignOffEvidence>();
}
