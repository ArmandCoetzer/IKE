namespace Ike.Api.Models;

public class JobCardAssignment
{
    public Guid JobCardId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; }
    public string? AssignedById { get; set; }
    public bool IsPermitManager { get; set; }

    public JobCard JobCard { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}
