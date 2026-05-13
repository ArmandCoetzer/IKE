namespace Tradion.Api.Models;

public class UserBadge
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid BadgeId { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;

    public ApplicationUser User { get; set; } = null!;
    public Badge Badge { get; set; } = null!;
}
