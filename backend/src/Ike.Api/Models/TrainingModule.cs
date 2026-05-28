namespace Ike.Api.Models;

public class TrainingModule
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ContentHtml { get; set; }
    public string? VideoUrl { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    public Course Course { get; set; } = null!;
    public TrainingQuiz? Quiz { get; set; }
    public ICollection<UserModuleProgress> UserProgress { get; set; } = new List<UserModuleProgress>();
}
