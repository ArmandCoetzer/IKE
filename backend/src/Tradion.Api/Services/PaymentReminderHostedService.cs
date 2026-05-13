using Microsoft.EntityFrameworkCore;
using Tradion.Api.Data;
using Tradion.Api.Helpers;
using Tradion.Api.Models;

namespace Tradion.Api.Services;

/// <summary>
/// Background service that creates payment reminder notifications for overdue invoices.
/// Runs daily and notifies users with ViewReports permission.
/// </summary>
public class PaymentReminderHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan AlreadyNotifiedWithin = TimeSpan.FromHours(23);

    public PaymentReminderHostedService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOverdueInvoicesAsync(stoppingToken);
            }
            catch (Exception)
            {
                // Log but continue
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ProcessOverdueInvoicesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var now = DateTime.UtcNow;
        var cutoff = now.Add(-AlreadyNotifiedWithin);

        var overdue = await db.Invoices
            .Where(i => !InvoiceStatus.IsPaid(i.Status) && i.DueDate < now.Date)
            .ToListAsync(ct);

        foreach (var inv in overdue)
        {
            var clientCompanyId = inv.CompanyId
                ?? await db.Sites.AsNoTracking().Where(s => s.Id == inv.SiteId).Select(s => (Guid?)s.CompanyId).FirstOrDefaultAsync(ct);
            if (!clientCompanyId.HasValue)
                continue;
            var mainScope = await CompanyScopeHelper.ResolveMainCompanyScopeIdAsync(db, clientCompanyId.Value, ct);
            if (!mainScope.HasValue)
                continue;
            var userIds = await notificationService.GetStaffUserIdsWithPermissionAsync("ViewReports", mainScope, ct);
            if (userIds.Count == 0)
                continue;
            var daysOverdue = Math.Max(0, (now.Date - inv.DueDate.Date).Days);
            var targetStage = daysOverdue switch
            {
                >= 14 => 3,
                >= 7 => 2,
                >= 1 => 1,
                _ => 0
            };
            if (targetStage == 0)
                continue;

            var lastReminderAt = inv.LastReminderSentAt;
            var shouldNotify = inv.ReminderStage < targetStage
                               || !lastReminderAt.HasValue
                               || lastReminderAt.Value < cutoff;
            if (!shouldNotify)
                continue;

            inv.ReminderStage = targetStage;
            inv.LastReminderSentAt = now;
            if (targetStage >= 3 && !inv.CollectionEscalatedAt.HasValue)
                inv.CollectionEscalatedAt = now;

            var title = targetStage switch
            {
                1 => "Payment reminder (gentle)",
                2 => "Payment reminder (urgent)",
                _ => "Payment reminder (final notice)"
            };
            var body = $"Invoice {inv.InvoiceNumber} is overdue by {daysOverdue} day(s) (due {inv.DueDate:yyyy-MM-dd}).";
            foreach (var uid in userIds)
            {
                db.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = uid,
                    Title = title,
                    Body = body.Length > 2000 ? body[..2000] : body,
                    Type = $"PaymentReminderStage{targetStage}",
                    RelatedEntityId = inv.Id.ToString(),
                    CreatedAt = DateTime.UtcNow
                });
            }

            await emailService.SendPaymentReminderAsync(inv.Id, toEmail: null, attachPdf: true, ct);
        }

        await db.SaveChangesAsync(ct);
    }
}
