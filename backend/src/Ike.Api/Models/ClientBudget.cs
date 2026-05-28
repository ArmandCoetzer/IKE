namespace Ike.Api.Models;

public class ClientBudget
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public decimal ThresholdAmount { get; set; }
    public decimal SpentAmount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public bool WorkPaused { get; set; }
    public DateTime? PausedAt { get; set; }
    public DateTime? ContinuationApprovedAt { get; set; }
    public string? ContinuationApprovedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Company Company { get; set; } = null!;
}
