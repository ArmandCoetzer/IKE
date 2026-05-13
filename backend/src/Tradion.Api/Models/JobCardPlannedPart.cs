namespace Tradion.Api.Models;

/// <summary>
/// Parts planned for use on a job card. Used for stock checks before work starts.
/// </summary>
public class JobCardPlannedPart
{
    public Guid Id { get; set; }
    public Guid JobCardId { get; set; }
    public Guid PartId { get; set; }
    public int Quantity { get; set; } = 1;

    public JobCard JobCard { get; set; } = null!;
    public Part Part { get; set; } = null!;
}
