namespace Ike.Api.Models;

/// <summary>
/// Many-to-many linkage between Work Authorisation masters and child permits.
/// Keeps historical visibility when permits are rolled over.
/// </summary>
public class JobPermitMasterLink
{
    public Guid MasterPermitId { get; set; }
    public JobPermit MasterPermit { get; set; } = null!;

    public Guid ChildPermitId { get; set; }
    public JobPermit ChildPermit { get; set; } = null!;

    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
}
