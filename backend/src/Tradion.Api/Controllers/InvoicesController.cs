using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tradion.Api.Data;
using Tradion.Api.DTOs.Invoices;
using Tradion.Api.Helpers;
using Tradion.Api.Models;
using Tradion.Api.Services;

namespace Tradion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class InvoicesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly ICurrentUserService _currentUser;
    private readonly IScopeGuardService _scopeGuard;

    public InvoicesController(ApplicationDbContext db, IEmailService emailService, INotificationService notificationService, ICurrentUserService currentUser, IScopeGuardService scopeGuard)
    {
        _db = db;
        _emailService = emailService;
        _notificationService = notificationService;
        _currentUser = currentUser;
        _scopeGuard = scopeGuard;
    }

    private static decimal ClampPercent(decimal value) => value < 0 ? 0 : (value > 100 ? 100 : value);
    private static decimal ComputeLineSubtotal(InvoiceLineItemDto li) => Math.Max(0m, li.Quantity * li.UnitPrice);
    private static decimal ComputeLineDiscountAmount(InvoiceLineItemDto li) => Math.Round(ComputeLineSubtotal(li) * (ClampPercent(li.DiscountPercent) / 100m), 2, MidpointRounding.AwayFromZero);
    private static decimal ComputeLineTotal(InvoiceLineItemDto li) => Math.Round(Math.Max(0m, ComputeLineSubtotal(li) - ComputeLineDiscountAmount(li)), 2, MidpointRounding.AwayFromZero);
    private static decimal ComputeInvoiceTotal(IEnumerable<InvoiceLineItemDto> lines) => lines.Where(li => li.Quantity > 0).Sum(ComputeLineTotal);

    [HttpGet]
    [Authorize(Policy = "RequireViewReports")]
    [ProducesResponseType(typeof(List<InvoiceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<InvoiceDto>>> List([FromQuery] Guid? clientId, [FromQuery] Guid? siteId, [FromQuery] string? status, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        IQueryable<Invoice> query = _db.Invoices.AsNoTracking()
            .Include(i => i.JobCard)
            .Include(i => i.Company)
            .Include(i => i.Site);
        if (companyId.HasValue)
        {
            if (isClient)
                query = query.Where(i => i.CompanyId == companyId);
            else
                query = query.Where(i => i.Company != null && i.Company.ParentCompanyId == companyId);
        }
        if (clientId.HasValue)
            query = query.Where(i => i.CompanyId == clientId);
        if (siteId.HasValue)
            query = query.Where(i => i.SiteId == siteId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(i => i.Status == status.Trim());

        var list = await query.Include(i => i.Quote)
            .Include(i => i.LineItems)
            .ThenInclude(li => li.Part)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();
        var dtos = list.Select(i => MapToDto(i)).ToList();
        return Ok(dtos);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "RequireViewReports")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvoiceDto>> Get(Guid id, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var inv = await _db.Invoices.AsNoTracking()
            .Include(i => i.JobCard)
            .Include(i => i.Quote)
            .Include(i => i.Company)
            .Include(i => i.Site)
            .Include(i => i.LineItems)
            .ThenInclude(li => li.Part)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv == null)
            return NotFound();
        if (companyId.HasValue && inv.Company != null &&
                (isClient ? inv.CompanyId != companyId : inv.Company.ParentCompanyId != companyId))
            return NotFound();
        return Ok(MapToDto(inv));
    }

    [HttpPost]
    [Authorize(Policy = "RequireManageInvoices")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<InvoiceDto>> Create([FromBody] CreateInvoiceRequest request, CancellationToken ct)
    {
        if (await IsClientScopedUserAsync(ct))
            return Forbid();
        var (scopeCompanyId, isClientScope) = await _currentUser.GetClientScopeAsync(ct);
        var job = await _db.JobCards.AsNoTracking()
            .Include(j => j.Site)
            .ThenInclude(s => s!.Company)
            .Include(j => j.ServiceRequest)
            .FirstOrDefaultAsync(j => j.Id == request.JobCardId, ct);
        if (job == null)
            return BadRequest(ApiResponseBodies.Message("Job card not found."));
        if (job.SiteId != request.SiteId)
            return BadRequest(ApiResponseBodies.Message("Site must match the job card's site."));
        if (scopeCompanyId.HasValue && job.Site != null)
        {
            var inJobScope = isClientScope
                ? job.Site.CompanyId == scopeCompanyId
                : (job.Site.CompanyId == scopeCompanyId || (job.Site.Company != null && job.Site.Company.ParentCompanyId == scopeCompanyId));
            if (!inJobScope)
                return NotFound();
        }
        if (request.ClientId.HasValue && !await _db.Companies.AnyAsync(c => c.Id == request.ClientId.Value, ct))
            return BadRequest(ApiResponseBodies.Message("Client not found."));
        var site = await _db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SiteId, ct);
        if (site == null)
            return BadRequest(ApiResponseBodies.Message("Site not found."));
        if (request.ClientId.HasValue && site.CompanyId != request.ClientId.Value)
            return BadRequest(ApiResponseBodies.Message("Site must belong to the selected client."));
        if (scopeCompanyId.HasValue)
        {
            var inClientScope = isClientScope
                ? request.ClientId == scopeCompanyId
                : await _db.Companies.AsNoTracking().AnyAsync(c => c.Id == request.ClientId && c.ParentCompanyId == scopeCompanyId, ct);
            if (!inClientScope)
                return NotFound();
        }

        var quoteForJob = await ResolveQuoteForJobInvoiceAsync(request.JobCardId, job.ServiceRequestId, request.QuoteId, ct);
        if (quoteForJob != null && !string.Equals(quoteForJob.Status, QuoteStatus.Accepted, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "The client must accept the quote before you can create an invoice for this job." });

        var lineItems = request.LineItems ?? new List<InvoiceLineItemDto>();
        if (lineItems.Count == 0)
        {
            var quotePartIds = new HashSet<Guid>();
            if (request.QuoteId.HasValue)
            {
                var quote = await _db.Quotes.AsNoTracking()
                    .Include(q => q.LineItems).ThenInclude(li => li.Part)
                    .FirstOrDefaultAsync(q => q.Id == request.QuoteId.Value, ct);
                if (quote != null)
                {
                    lineItems = quote.LineItems.OrderBy(li => li.SortOrder).Select(li =>
                    {
                        if (li.PartId.HasValue) quotePartIds.Add(li.PartId.Value);
                        return new InvoiceLineItemDto
                        {
                            LineType = li.LineType,
                            Description = li.Description,
                            Quantity = li.Quantity,
                            UnitPrice = li.UnitPrice,
                            DiscountPercent = li.DiscountPercent,
                            PartId = li.PartId
                        };
                    }).ToList();
                }
            }
            var plannedParts = await _db.JobCardPlannedParts.AsNoTracking()
                .Include(jpp => jpp.Part)
                .Where(jpp => jpp.JobCardId == request.JobCardId)
                .ToListAsync(ct);
            var quoteLinesByPartId = lineItems
                .Where(li => li.PartId.HasValue)
                .ToDictionary(li => li.PartId!.Value, li => li);
            foreach (var jpp in plannedParts)
            {
                if (quoteLinesByPartId.TryGetValue(jpp.PartId, out var existing))
                    existing.Quantity = Math.Max(existing.Quantity, (decimal)jpp.Quantity);
                else if (!quotePartIds.Contains(jpp.PartId))
                {
                    lineItems.Add(new InvoiceLineItemDto
                    {
                        LineType = "Part",
                        Description = jpp.Part?.Name ?? "Part",
                        Quantity = jpp.Quantity,
                        UnitPrice = 0m,
                        DiscountPercent = 0m,
                        PartId = jpp.PartId
                    });
                }
            }
        }
        if (job.ServiceRequestId.HasValue && job.ServiceRequest != null && job.ServiceRequest.PenaltyFee.HasValue && job.ServiceRequest.PenaltyFee.Value > 0)
        {
            lineItems.Add(new InvoiceLineItemDto
            {
                LineType = "Labour",
                Description = string.IsNullOrWhiteSpace(job.ServiceRequest.PenaltyNote) ? "Priority inflation penalty" : job.ServiceRequest.PenaltyNote.Trim(),
                Quantity = 1,
                UnitPrice = job.ServiceRequest.PenaltyFee.Value,
                DiscountPercent = 0m,
                PartId = null
            });
        }
        var amount = request.Amount;
        if (lineItems.Count > 0)
            amount = ComputeInvoiceTotal(lineItems);

        Guid? quoteId = request.QuoteId;
        if (quoteId.HasValue && !await _db.Quotes.AnyAsync(q => q.Id == quoteId.Value, ct))
            quoteId = null;

        var invoiceNumber = NumberGenerator.NextInvoiceNumber(_db.Invoices);
        var inv = new Invoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = invoiceNumber,
            JobCardId = request.JobCardId,
            QuoteId = quoteId,
            CompanyId = request.ClientId,
            SiteId = request.SiteId,
            Amount = amount,
            Currency = request.Currency?.Trim() ?? "ZAR",
            Status = InvoiceStatus.Draft,
            DueDate = request.DueDate,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedAt = DateTime.UtcNow,
            PartsConfirmed = false
        };
        _db.Invoices.Add(inv);
        var sortOrder = 0;
        foreach (var li in lineItems)
        {
            if (li.Quantity <= 0) continue;
            var partId = li.LineType?.Equals("Part", StringComparison.OrdinalIgnoreCase) == true && li.PartId.HasValue && await _db.Parts.AnyAsync(p => p.Id == li.PartId.Value, ct) ? li.PartId : null;
            _db.InvoiceLineItems.Add(new InvoiceLineItem
            {
                Id = Guid.NewGuid(),
                InvoiceId = inv.Id,
                LineType = (li.LineType ?? "Labour").Trim().Length > 0 ? li.LineType!.Trim() : "Labour",
                Description = li.Description?.Trim() ?? "",
                Quantity = li.Quantity,
                UnitPrice = li.UnitPrice,
                DiscountPercent = ClampPercent(li.DiscountPercent),
                SortOrder = sortOrder++,
                PartId = partId
            });
        }
        await _db.SaveChangesAsync(ct);

        var loaded = await _db.Invoices.AsNoTracking()
            .Include(i => i.JobCard)
            .Include(i => i.Quote)
            .Include(i => i.Company)
            .Include(i => i.Site)
            .Include(i => i.LineItems)
            .ThenInclude(li => li.Part)
            .FirstAsync(i => i.Id == inv.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = inv.Id }, MapToDto(loaded));
    }

    private static InvoiceDto MapToDto(Invoice inv)
    {
        return new InvoiceDto
        {
            Id = inv.Id,
            InvoiceNumber = inv.InvoiceNumber,
            JobCardId = inv.JobCardId,
            JobCardNumber = inv.JobCard?.JobCardNumber,
            QuoteId = inv.QuoteId,
            QuoteNumber = inv.Quote?.QuoteNumber,
            ClientId = inv.CompanyId,
            ClientName = inv.Company?.Name,
            SiteId = inv.SiteId,
            SiteName = inv.Site?.Name,
            Amount = inv.Amount,
            Currency = inv.Currency,
            Status = inv.Status,
            DueDate = inv.DueDate,
            SentAt = inv.SentAt,
            PaidAt = inv.PaidAt,
            ReminderStage = inv.ReminderStage,
            LastReminderSentAt = inv.LastReminderSentAt,
            PromiseToPayBy = inv.PromiseToPayBy,
            CollectionEscalatedAt = inv.CollectionEscalatedAt,
            CreatedAt = inv.CreatedAt,
            Notes = inv.Notes,
            PartsConfirmed = inv.PartsConfirmed,
            LineItems = inv.LineItems?.OrderBy(li => li.SortOrder).Select(li => new InvoiceLineItemResponseDto
            {
                Id = li.Id,
                LineType = li.LineType,
                Description = li.Description,
                Quantity = li.Quantity,
                UnitPrice = li.UnitPrice,
                DiscountPercent = li.DiscountPercent,
                PartId = li.PartId,
                PartName = li.Part?.Name
            }).ToList() ?? new List<InvoiceLineItemResponseDto>()
        };
    }

    [HttpPost("{id:guid}/send")]
    [Authorize(Policy = "RequireManageInvoices")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Send(Guid id, [FromQuery] string? toEmail, [FromQuery] bool attachPdf = true, CancellationToken ct = default)
    {
        if (await IsClientScopedUserAsync(ct))
            return Forbid();
        var inv = await LoadInvoiceInScopeForUpdateAsync(id, ct);
        if (inv == null)
            return NotFound();
        if (!inv.PartsConfirmed)
            return BadRequest(ApiResponseBodies.Message("Parts must be confirmed before sending the invoice."));
        var sent = await _emailService.SendInvoiceToClientAsync(id, toEmail, attachPdf, ct);
        if (!sent)
            return BadRequest(ApiResponseBodies.Message("Invoice email was not sent. Ensure the client email is set and SMTP settings are valid."));
        inv.SentAt = DateTime.UtcNow;
        if (!InvoiceStatus.IsPaid(inv.Status))
            inv.Status = InvoiceStatus.WaitingPayment;
        await _db.SaveChangesAsync(ct);
        await _notificationService.NotifyUsersWithPermissionAsync(
            "ViewReports",
            "Invoice sent",
            $"Invoice {inv.InvoiceNumber} has been sent to the client.",
            "InvoiceSent",
            inv.Id.ToString(),
            excludeUserId: null,
            scopeCompanyId: (await _currentUser.GetClientScopeAsync(ct)).CompanyId,
            ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/send-reminder")]
    [Authorize(Policy = "RequireManageInvoices")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendReminder(Guid id, [FromQuery] string? toEmail, [FromQuery] bool attachPdf = true, CancellationToken ct = default)
    {
        if (await IsClientScopedUserAsync(ct))
            return Forbid();
        var inv = await LoadInvoiceInScopeForUpdateAsync(id, ct);
        if (inv == null)
            return NotFound();
        if (InvoiceStatus.IsPaid(inv.Status))
            return BadRequest(ApiResponseBodies.Message("Invoice is already paid."));
        await _emailService.SendPaymentReminderAsync(id, toEmail, attachPdf, ct);
        await _notificationService.NotifyUsersWithPermissionAsync(
            "ViewReports",
            "Payment reminder sent",
            "A payment reminder was sent for overdue invoice " + inv.InvoiceNumber + ".",
            "OverdueInvoice",
            inv.Id.ToString(),
            excludeUserId: null,
            scopeCompanyId: (await _currentUser.GetClientScopeAsync(ct)).CompanyId,
            ct);
        return NoContent();
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireManageInvoices")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<InvoiceDto>> Update(Guid id, [FromBody] UpdateInvoiceRequest request, CancellationToken ct = default)
    {
        if (await IsClientScopedUserAsync(ct))
            return Forbid();
        var inv = await LoadInvoiceInScopeForUpdateAsync(id, ct, includeLineItems: true);
        if (inv == null)
            return NotFound();
        if (InvoiceStatus.IsPaid(inv.Status))
            return BadRequest(ApiResponseBodies.Message("Paid invoices are locked and cannot be edited."));
        if (inv.PartsConfirmed && request.LineItems != null)
            return BadRequest(ApiResponseBodies.Message("Cannot update line items after parts are confirmed."));
        inv.DueDate = request.DueDate;
        inv.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (request.LineItems != null && !inv.PartsConfirmed)
        {
            _db.InvoiceLineItems.RemoveRange(inv.LineItems);
            var sortOrder = 0;
            foreach (var li in request.LineItems)
            {
                if (li.Quantity <= 0) continue;
                var partId = li.LineType?.Equals("Part", StringComparison.OrdinalIgnoreCase) == true && li.PartId.HasValue && await _db.Parts.AnyAsync(p => p.Id == li.PartId.Value, ct) ? li.PartId : null;
                _db.InvoiceLineItems.Add(new InvoiceLineItem
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = id,
                    LineType = (li.LineType ?? "Labour").Trim().Length > 0 ? li.LineType!.Trim() : "Labour",
                    Description = li.Description?.Trim() ?? "",
                    Quantity = li.Quantity,
                    UnitPrice = li.UnitPrice,
                    DiscountPercent = ClampPercent(li.DiscountPercent),
                    SortOrder = sortOrder++,
                    PartId = partId
                });
            }
            inv.Amount = ComputeInvoiceTotal(request.LineItems);
        }
        await _db.SaveChangesAsync(ct);
        return await Get(id);
    }

    [HttpPost("{id:guid}/confirm-parts")]
    [Authorize(Policy = "RequireManageInvoices")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<InvoiceDto>> ConfirmParts(Guid id, [FromBody] ConfirmPartsRequest? request, CancellationToken ct = default)
    {
        if (await IsClientScopedUserAsync(ct))
            return Forbid();
        var inv = await LoadInvoiceInScopeForUpdateAsync(id, ct, includeLineItems: true);
        if (inv == null)
            return NotFound();
        if (inv.PartsConfirmed)
        {
            var loaded = await _db.Invoices.AsNoTracking()
                .Include(i => i.JobCard).Include(i => i.Quote).Include(i => i.Company).Include(i => i.Site)
                .Include(i => i.LineItems).ThenInclude(li => li.Part)
                .FirstAsync(i => i.Id == id, ct);
            return Ok(MapToDto(loaded));
        }
        if (request?.LineItems != null)
        {
            _db.InvoiceLineItems.RemoveRange(inv.LineItems);
            var sortOrder = 0;
            foreach (var li in request.LineItems)
            {
                if (li.Quantity <= 0) continue;
                var partId = li.LineType?.Equals("Part", StringComparison.OrdinalIgnoreCase) == true && li.PartId.HasValue && await _db.Parts.AnyAsync(p => p.Id == li.PartId.Value, ct) ? li.PartId : null;
                _db.InvoiceLineItems.Add(new InvoiceLineItem
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = id,
                    LineType = (li.LineType ?? "Labour").Trim().Length > 0 ? li.LineType!.Trim() : "Labour",
                    Description = li.Description?.Trim() ?? "",
                    Quantity = li.Quantity,
                    UnitPrice = li.UnitPrice,
                    DiscountPercent = ClampPercent(li.DiscountPercent),
                    SortOrder = sortOrder++,
                    PartId = partId
                });
            }
            inv.Amount = ComputeInvoiceTotal(request.LineItems);
        }
        inv.PartsConfirmed = true;
        await _db.SaveChangesAsync(ct);
        var result = await _db.Invoices.AsNoTracking()
            .Include(i => i.JobCard).Include(i => i.Quote).Include(i => i.Company).Include(i => i.Site)
            .Include(i => i.LineItems).ThenInclude(li => li.Part)
            .FirstAsync(i => i.Id == id, ct);
        return Ok(MapToDto(result));
    }

    [HttpPatch("{id:guid}/mark-paid")]
    [Authorize(Policy = "RequireManageInvoices")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvoiceDto>> MarkPaid(Guid id, CancellationToken ct = default)
    {
        if (await IsClientScopedUserAsync(ct))
            return Forbid();
        var inv = await LoadInvoiceInScopeForUpdateAsync(id, ct, includeGraph: true);
        if (inv == null)
            return NotFound();
        inv.Status = InvoiceStatus.Paid;
        inv.PaidAt = DateTime.UtcNow;
        inv.PromiseToPayBy = null;
        inv.CollectionEscalatedAt = null;
        await _db.SaveChangesAsync(ct);
        return Ok(MapToDto(inv));
    }

    [HttpPatch("{id:guid}/payment-promise")]
    [Authorize(Policy = "RequireManageInvoices")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvoiceDto>> SetPaymentPromise(Guid id, [FromBody] SetPaymentPromiseRequest request, CancellationToken ct = default)
    {
        if (await IsClientScopedUserAsync(ct))
            return Forbid();
        var inv = await LoadInvoiceInScopeForUpdateAsync(id, ct, includeGraph: true);
        if (inv == null)
            return NotFound();
        if (InvoiceStatus.IsPaid(inv.Status))
            return BadRequest(ApiResponseBodies.Message("Invoice is already paid."));

        inv.PromiseToPayBy = request.PromiseToPayBy?.Date;
        await _db.SaveChangesAsync(ct);
        return Ok(MapToDto(inv));
    }

    private async Task<Invoice?> LoadInvoiceInScopeForUpdateAsync(Guid id, CancellationToken ct, bool includeLineItems = false, bool includeGraph = false)
    {
        IQueryable<Invoice> query = _db.Invoices;
        if (includeLineItems || includeGraph)
            query = query.Include(i => i.LineItems);
        if (includeGraph)
            query = query
                .Include(i => i.JobCard)
                .Include(i => i.Quote)
                .Include(i => i.Company)
                .Include(i => i.Site)
                .Include(i => i.LineItems).ThenInclude(li => li.Part);

        var invoice = await query
            .Include(i => i.JobCard)
            .Include(i => i.Company)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice == null) return null;

        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (!companyId.HasValue) return invoice;
        var inScope = await _scopeGuard.CanAccessCompanyAsync(invoice.CompanyId, invoice.Company, ct);
        return inScope ? invoice : null;
    }

    private async Task<bool> IsClientScopedUserAsync(CancellationToken ct)
    {
        var (_, isClient) = await _currentUser.GetClientScopeAsync(ct);
        return isClient;
    }

    /// <summary>Quote linked to this invoice/job: explicit request id, else quote on job card, else quote on service request.</summary>
    private async Task<Quote?> ResolveQuoteForJobInvoiceAsync(Guid jobCardId, Guid? serviceRequestId, Guid? requestQuoteId, CancellationToken ct)
    {
        if (requestQuoteId.HasValue)
            return await _db.Quotes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == requestQuoteId.Value, ct);
        var byJob = await _db.Quotes.AsNoTracking().FirstOrDefaultAsync(q => q.JobCardId == jobCardId, ct);
        if (byJob != null) return byJob;
        if (serviceRequestId.HasValue)
            return await _db.Quotes.AsNoTracking().FirstOrDefaultAsync(q => q.ServiceRequestId == serviceRequestId, ct);
        return null;
    }
}
