namespace Ike.Api.Models;

public class JobCardSignOffEvidence
{
    public Guid Id { get; set; }
    public Guid JobCardId { get; set; }
    public Guid JobCardDocumentId { get; set; }
    public string FileSha256 { get; set; } = string.Empty;
    public string EvidenceHash { get; set; } = string.Empty;
    public string? PreviousEvidenceHash { get; set; }
    public string CaptureSource { get; set; } = "UploadedImage";
    public string? DeviceId { get; set; }
    public string? AppVersion { get; set; }
    public string? SignerDisplayName { get; set; }
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
    public string RecordedByUserId { get; set; } = string.Empty;

    public JobCard JobCard { get; set; } = null!;
    public JobCardDocument JobCardDocument { get; set; } = null!;
    public ApplicationUser RecordedByUser { get; set; } = null!;
}
