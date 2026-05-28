namespace Ike.Api.Models;

public class UserModuleProgress
{
    public string UserId { get; set; } = string.Empty;
    public Guid ModuleId { get; set; }
    public int VideoProgressPercent { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ApplicationUser User { get; set; } = null!;
    public TrainingModule Module { get; set; } = null!;
}
