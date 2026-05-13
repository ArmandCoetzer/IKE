namespace Tradion.Api.Models;

public class JobPermitAttachment
{
    public Guid Id { get; set; }
    public Guid JobPermitId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string? UploadedByUserId { get; set; }

    public JobPermit JobPermit { get; set; } = null!;
    public ApplicationUser? UploadedByUser { get; set; }
}
