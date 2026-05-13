namespace Tradion.Api.DTOs.ClientBudget;

public class ClientBudgetDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public decimal ThresholdAmount { get; set; }
    public decimal SpentAmount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public bool WorkPaused { get; set; }
    public DateTime? PausedAt { get; set; }
    public DateTime? ContinuationApprovedAt { get; set; }
    /// <summary>Progress 0-100. When &gt;= 100, threshold exceeded.</summary>
    public decimal ProgressPercent => ThresholdAmount > 0 ? Math.Min(100, (SpentAmount / ThresholdAmount) * 100) : 0;
}
