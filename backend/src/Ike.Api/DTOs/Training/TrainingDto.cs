namespace Ike.Api.DTOs.Training;

public class CourseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public int ModuleCount { get; set; }
}

public class CourseDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public List<ModuleSummaryDto> Modules { get; set; } = new();
}

public class ModuleSummaryDto
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool HasQuiz { get; set; }
    public Guid? QuizId { get; set; }
    public bool? IsCompleted { get; set; }
}

public class ModuleDetailDto
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public string? CourseName { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ContentHtml { get; set; }
    public string? VideoUrl { get; set; }
    public int SortOrder { get; set; }
    public Guid? QuizId { get; set; }
    public string? QuizName { get; set; }
    public bool? IsCompleted { get; set; }
}

public class QuizDto
{
    public Guid Id { get; set; }
    public Guid ModuleId { get; set; }
    public string? ModuleTitle { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PassScore { get; set; }
    public List<QuizQuestionDto> Questions { get; set; } = new();
}

public class QuizQuestionDto
{
    public Guid Id { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();
    public int CorrectIndex { get; set; }
    public int SortOrder { get; set; }
}

public class SubmitQuizRequest
{
    public List<QuestionAnswerDto> Answers { get; set; } = new();
}

public class QuestionAnswerDto
{
    public Guid QuestionId { get; set; }
    public int SelectedIndex { get; set; }
}

public class QuizResultDto
{
    public int Score { get; set; }
    public int Total { get; set; }
    public bool Passed { get; set; }
    public int PassScore { get; set; }
}

public class CreateCourseRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
}

public class CreateModuleRequest
{
    public string Title { get; set; } = string.Empty;
    public string? ContentHtml { get; set; }
    public string? VideoUrl { get; set; }
    public int SortOrder { get; set; }
}

public class UpdateCourseRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UpdateModuleRequest
{
    public string Title { get; set; } = string.Empty;
    public string? ContentHtml { get; set; }
    public string? VideoUrl { get; set; }
    public int SortOrder { get; set; }
}

public class CreateQuizRequest
{
    public string Name { get; set; } = string.Empty;
    public int PassScore { get; set; } = 70;
}

public class UpdateQuizRequest
{
    public string Name { get; set; } = string.Empty;
    public int PassScore { get; set; }
}

public class CreateQuizQuestionRequest
{
    public string QuestionText { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();
    public int CorrectIndex { get; set; }
    public int SortOrder { get; set; }
}

// Badges
public class BadgeDto
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int ValidityMonths { get; set; }
}

public class UserBadgeDto
{
    public Guid Id { get; set; }
    public Guid BadgeId { get; set; }
    public string BadgeName { get; set; } = string.Empty;
    public string? BadgeDescription { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class CreateBadgeRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int ValidityMonths { get; set; } = 12;
}

public class UpdateBadgeRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int ValidityMonths { get; set; }
}

public class ExpiringBadgeDto
{
    public Guid UserBadgeId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string BadgeName { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsExpired { get; set; }
}

public class TrainingMediaUploadResponse
{
    public string Url { get; set; } = string.Empty;
}

public class UpdateQuizQuestionRequest
{
    public string QuestionText { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();
    public int CorrectIndex { get; set; }
    public int SortOrder { get; set; }
}
