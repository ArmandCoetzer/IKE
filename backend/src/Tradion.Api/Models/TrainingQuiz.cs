namespace Tradion.Api.Models;

public class TrainingQuiz
{
    public Guid Id { get; set; }
    public Guid ModuleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PassScore { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public TrainingModule Module { get; set; } = null!;
    public ICollection<QuizQuestion> Questions { get; set; } = new List<QuizQuestion>();
    public ICollection<UserQuizAttempt> UserAttempts { get; set; } = new List<UserQuizAttempt>();
}
