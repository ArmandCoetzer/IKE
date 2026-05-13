namespace Tradion.Api.Models;

public class JobPermit
{
    public Guid Id { get; set; }
    /// <summary>Sequential number per permit type (e.g. Work Authorisation #187101).</summary>
    public int PermitNumber { get; set; }
    public Guid JobCardId { get; set; }
    public Guid PermitTemplateId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public string? RequestedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedByUserId { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public string? ChecklistSnapshotJson { get; set; }

    /// <summary>Submitted values for <see cref="PermitTemplate.FormSchemaJson"/> (JSON object id → string/bool). Not used for Work Authorisation master payload.</summary>
    public string? FormSnapshotJson { get; set; }

    /// <summary>WA was edited after client sign-off; job standstill until client signs off again.</summary>
    public bool PendingWaAmendmentSignOff { get; set; }

    /// <summary>SHA-256 hex of WA business content at last client sign-off (signatures stripped).</summary>
    public string? WaSignedBusinessContentHash { get; set; }

    /// <summary>When set, this permit is driven by the master (Work Authorisation) permit.</summary>
    public Guid? MasterPermitId { get; set; }
    public JobPermit? MasterPermit { get; set; }
    public ICollection<JobPermit> ChildPermits { get; set; } = new List<JobPermit>();
    public ICollection<JobPermitMasterLink> MasterLinks { get; set; } = new List<JobPermitMasterLink>();
    public ICollection<JobPermitMasterLink> ChildLinks { get; set; } = new List<JobPermitMasterLink>();

    /// <summary>Soft-archive: row kept for audit when switching to paper mode midstream; excluded from normal API lists.</summary>
    public bool HiddenFromUiForHistory { get; set; }

    /// <summary>Physical permit reference entered on paper workflow (required before client paper sign-off).</summary>
    public string? PaperPermitNumber { get; set; }

    public DateTime? PaperClientSignedOffAt { get; set; }

    public string? PaperClientSignedOffByUserId { get; set; }

    public JobCard JobCard { get; set; } = null!;
    public PermitTemplate PermitTemplate { get; set; } = null!;
    public ICollection<JobPermitAttachment> Attachments { get; set; } = new List<JobPermitAttachment>();
    public ApplicationUser? RequestedByUser { get; set; }
    public ApplicationUser? ApprovedByUser { get; set; }
}
