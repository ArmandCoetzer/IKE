using Microsoft.EntityFrameworkCore;
using Tradion.Api.Data;
using Tradion.Api.Helpers;
using Tradion.Api.Models;

namespace Tradion.Api.Services;

/// <summary>
/// Daily anomaly scan that records operational risk alerts and notifies report viewers.
/// </summary>
public class OperationalRiskAlertsHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    public OperationalRiskAlertsHostedService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAsync(stoppingToken);
            }
            catch
            {
                // swallow and keep service alive
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ScanAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var now = DateTime.UtcNow;

        var newAlerts = new List<RiskAlert>();
        var incidentCutoff = now.AddDays(-14);
        var staleCutoff = now.AddDays(-14);

        var repeatedExpiryJobs = await db.JobPermits.AsNoTracking()
            .Where(p => p.RequestedAt >= now.AddDays(-30) && p.Status == PermitStatus.Expired)
            .GroupBy(p => p.JobCardId)
            .Where(g => g.Count() >= 2)
            .Select(g => new { JobCardId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        foreach (var row in repeatedExpiryJobs)
        {
            newAlerts.Add(new RiskAlert
            {
                CompanyId = await GetJobCompanyIdAsync(db, row.JobCardId, ct),
                AlertType = "RepeatedPermitExpiry",
                Severity = "High",
                Title = "Repeated permit expiry on one job",
                Details = $"Job {row.JobCardId} has {row.Count} expired permits in the last 30 days.",
                EntityType = "JobCard",
                EntityId = row.JobCardId.ToString(),
                FirstDetectedAt = now,
                LastDetectedAt = now
            });
        }

        var pendingWaAmendments = await db.JobPermits.AsNoTracking()
            .Where(p => p.PendingWaAmendmentSignOff)
            .Select(p => p.JobCardId)
            .Distinct()
            .ToListAsync(ct);
        foreach (var jobId in pendingWaAmendments)
        {
            newAlerts.Add(new RiskAlert
            {
                CompanyId = await GetJobCompanyIdAsync(db, jobId, ct),
                AlertType = "PendingWaAmendment",
                Severity = "Medium",
                Title = "WA amendment pending client re-sign",
                Details = $"Job {jobId} has a Work Authorisation amendment still awaiting sign-off.",
                EntityType = "JobCard",
                EntityId = jobId.ToString(),
                FirstDetectedAt = now,
                LastDetectedAt = now
            });
        }

        var incidentBySite = await db.IncidentReports.AsNoTracking()
            .Where(i => i.CreatedAt >= incidentCutoff)
            .Join(db.JobCards.AsNoTracking(), i => i.JobCardId, j => j.Id, (i, j) => new { j.SiteId, i.Id })
            .GroupBy(x => x.SiteId)
            .Where(g => g.Count() >= 4)
            .Select(g => new { SiteId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        foreach (var site in incidentBySite)
        {
            var companyId = await db.Sites.AsNoTracking().Where(s => s.Id == site.SiteId).Select(s => s.CompanyId).FirstOrDefaultAsync(ct);
            newAlerts.Add(new RiskAlert
            {
                CompanyId = companyId,
                AlertType = "HighIncidentSite",
                Severity = "High",
                Title = "High incident volume on site",
                Details = $"Site {site.SiteId} logged {site.Count} incidents in the last 14 days.",
                EntityType = "Site",
                EntityId = site.SiteId.ToString(),
                FirstDetectedAt = now,
                LastDetectedAt = now
            });
        }

        var staleQuotes = await db.Quotes.AsNoTracking()
            .Where(q => q.CreatedAt < staleCutoff && (q.Status == QuoteStatus.Draft || q.Status == QuoteStatus.Sent))
            .Select(q => new { q.Id, q.CompanyId, q.QuoteNumber, q.Status, q.CreatedAt })
            .ToListAsync(ct);
        foreach (var q in staleQuotes)
        {
            newAlerts.Add(new RiskAlert
            {
                CompanyId = q.CompanyId,
                AlertType = "StaleQuote",
                Severity = "Medium",
                Title = "Quote stalled beyond SLA",
                Details = $"Quote {q.QuoteNumber} has remained {q.Status} since {q.CreatedAt:yyyy-MM-dd}.",
                EntityType = "Quote",
                EntityId = q.Id.ToString(),
                FirstDetectedAt = now,
                LastDetectedAt = now
            });
        }

        var stalePos = await db.PurchaseOrders.AsNoTracking()
            .Where(po => po.CreatedAt < staleCutoff && po.Status == PurchaseOrderStatus.Draft)
            .Select(po => new { po.Id, po.CompanyId, po.PONumber, po.CreatedAt })
            .ToListAsync(ct);
        foreach (var po in stalePos)
        {
            newAlerts.Add(new RiskAlert
            {
                CompanyId = po.CompanyId,
                AlertType = "StalePurchaseOrder",
                Severity = "Medium",
                Title = "Purchase order stalled beyond SLA",
                Details = $"PO {po.PONumber} remains Draft since {po.CreatedAt:yyyy-MM-dd}.",
                EntityType = "PurchaseOrder",
                EntityId = po.Id.ToString(),
                FirstDetectedAt = now,
                LastDetectedAt = now
            });
        }

        var staleInvoices = await db.Invoices.AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Paid && i.DueDate < now.AddDays(-7))
            .Select(i => new { i.Id, i.CompanyId, i.InvoiceNumber, i.DueDate })
            .ToListAsync(ct);
        foreach (var inv in staleInvoices)
        {
            newAlerts.Add(new RiskAlert
            {
                CompanyId = inv.CompanyId,
                AlertType = "StaleInvoiceCollections",
                Severity = "High",
                Title = "Invoice overdue beyond SLA",
                Details = $"Invoice {inv.InvoiceNumber} is overdue since {inv.DueDate:yyyy-MM-dd}.",
                EntityType = "Invoice",
                EntityId = inv.Id.ToString(),
                FirstDetectedAt = now,
                LastDetectedAt = now
            });
        }

        var createdAlerts = new List<RiskAlert>();
        foreach (var candidate in newAlerts)
        {
            var exists = await db.RiskAlerts.AnyAsync(a =>
                a.AlertType == candidate.AlertType
                && a.EntityType == candidate.EntityType
                && a.EntityId == candidate.EntityId
                && a.CompanyId == candidate.CompanyId
                && a.ResolvedAt == null, ct);
            if (exists)
            {
                var existing = await db.RiskAlerts.FirstOrDefaultAsync(a =>
                    a.AlertType == candidate.AlertType
                    && a.EntityType == candidate.EntityType
                    && a.EntityId == candidate.EntityId
                    && a.CompanyId == candidate.CompanyId
                    && a.ResolvedAt == null, ct);
                if (existing != null) existing.LastDetectedAt = now;
                continue;
            }

            candidate.Id = Guid.NewGuid();
            db.RiskAlerts.Add(candidate);
            createdAlerts.Add(candidate);
        }

        foreach (var alert in createdAlerts)
        {
            if (!alert.CompanyId.HasValue)
                continue;
            var mainScope = await CompanyScopeHelper.ResolveMainCompanyScopeIdAsync(db, alert.CompanyId.Value, ct);
            if (!mainScope.HasValue)
                continue;
            var recipients = await notificationService.GetStaffUserIdsWithPermissionAsync("ViewReports", mainScope, ct);
            foreach (var userId in recipients)
            {
                db.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Title = $"Operational risk: {alert.Title}",
                    Body = alert.Details.Length > 2000 ? alert.Details[..2000] : alert.Details,
                    Type = "OperationalRiskAlert",
                    RelatedEntityId = alert.EntityId,
                    CreatedAt = now
                });
            }
        }

        await db.SaveChangesAsync(ct);

        // Weekly summary on Mondays, per main company, deduped by ISO week key + company.
        if (now.DayOfWeek != DayOfWeek.Monday) return;
        var weekKey = $"{now:yyyy}-W{System.Globalization.ISOWeek.GetWeekOfYear(now):00}";

        var openAlertRows = await db.RiskAlerts.AsNoTracking()
            .Where(a => a.ResolvedAt == null && a.CompanyId != null)
            .OrderByDescending(a => a.LastDetectedAt)
            .ToListAsync(ct);
        if (openAlertRows.Count == 0) return;

        var alertsByMain = new Dictionary<Guid, List<RiskAlert>>();
        foreach (var a in openAlertRows)
        {
            if (!a.CompanyId.HasValue) continue;
            var mainId = await CompanyScopeHelper.ResolveMainCompanyScopeIdAsync(db, a.CompanyId.Value, ct);
            if (!mainId.HasValue) continue;
            if (!alertsByMain.TryGetValue(mainId.Value, out var list))
            {
                list = new List<RiskAlert>();
                alertsByMain[mainId.Value] = list;
            }
            list.Add(a);
        }

        foreach (var (mainId, alertsForMain) in alertsByMain)
        {
            var dedupeKey = $"{weekKey}:{mainId}";
            var alreadySent = await db.Notifications.AsNoTracking()
                .AnyAsync(n => n.Type == "OperationalRiskWeeklySummary" && n.RelatedEntityId == dedupeKey, ct);
            if (alreadySent) continue;

            var lines = alertsForMain
                .OrderByDescending(a => a.LastDetectedAt)
                .Take(20)
                .Select(a => $"{a.Severity}: {a.Title} ({a.EntityType}:{a.EntityId})")
                .ToList();
            if (lines.Count == 0) continue;

            var recipients = await notificationService.GetStaffUserIdsWithPermissionAsync("ViewReports", mainId, ct);
            var emails = await db.Users.AsNoTracking()
                .Where(u => recipients.Contains(u.Id) && u.Email != null && u.Email != "")
                .Select(u => u.Email!)
                .Distinct()
                .ToListAsync(ct);
            foreach (var email in emails)
                await emailService.SendOperationalRiskSummaryAsync(email, $"Operational risk weekly summary ({weekKey})", lines, ct);

            foreach (var userId in recipients)
            {
                db.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Title = "Operational risk weekly summary sent",
                    Body = $"Weekly summary {weekKey} for your organization was sent.",
                    Type = "OperationalRiskWeeklySummary",
                    RelatedEntityId = dedupeKey,
                    CreatedAt = now
                });
            }

            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task<Guid?> GetJobCompanyIdAsync(ApplicationDbContext db, Guid jobCardId, CancellationToken ct)
    {
        return await db.JobCards.AsNoTracking()
            .Where(j => j.Id == jobCardId)
            .Select(j => j.Site.CompanyId)
            .FirstOrDefaultAsync(ct);
    }
}
