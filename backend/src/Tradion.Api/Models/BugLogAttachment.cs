namespace Tradion.Api.Models;

public class BugLogAttachment
{
    public Guid Id { get; set; }
    public Guid BugLogId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public BugLog? BugLog { get; set; }
}
