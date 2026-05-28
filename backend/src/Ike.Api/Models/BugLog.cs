namespace Ike.Api.Models;

public class BugLog
{
    public Guid Id { get; set; }
    public string? UserId { get; set; }
    public string? Title { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ApplicationUser? User { get; set; }
    public ICollection<BugLogAttachment> Attachments { get; set; } = new List<BugLogAttachment>();
}
