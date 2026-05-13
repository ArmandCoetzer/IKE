namespace Tradion.Api.Models;

public class UserQuizAttempt
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid QuizId { get; set; }
    public int Score { get; set; }
    public bool Passed { get; set; }
    public string? AnswersJson { get; set; }
    public DateTime CompletedAt { get; set; }

    public ApplicationUser User { get; set; } = null!;
    public TrainingQuiz Quiz { get; set; } = null!;
}
