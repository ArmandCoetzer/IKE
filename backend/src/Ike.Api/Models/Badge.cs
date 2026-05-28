namespace Ike.Api.Models;

public class Badge
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int ValidityMonths { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    public Course Course { get; set; } = null!;
    public ICollection<UserBadge> UserBadges { get; set; } = new List<UserBadge>();
}
