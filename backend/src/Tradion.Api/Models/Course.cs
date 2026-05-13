namespace Tradion.Api.Models;

public class Course
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    public ICollection<TrainingModule> Modules { get; set; } = new List<TrainingModule>();
    public ICollection<Badge> Badges { get; set; } = new List<Badge>();
}
