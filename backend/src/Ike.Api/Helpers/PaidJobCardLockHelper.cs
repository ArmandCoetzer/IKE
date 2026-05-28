using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.Models;

namespace Ike.Api.Helpers;

/// <summary>
/// When a job card has a paid invoice, it is view-only (no mutations on the job or its permits from the app).
/// </summary>
public static class PaidJobCardLockHelper
{
    public const string UserMessage = "This job card is locked because the invoice has been paid. It is now view-only.";

    public static Task<bool> IsLockedAsync(ApplicationDbContext db, Guid jobCardId, CancellationToken ct = default) =>
        db.Invoices.AsNoTracking().AnyAsync(i => i.JobCardId == jobCardId && i.Status == InvoiceStatus.Paid, ct);
}
