using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.DTOs.Invoices;
using Ike.Api.Helpers;
using Ike.Api.Models;
using Ike.Api.Services;

namespace Ike.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class InvoicesController : ControllerBase
{
    private const string InvoiceUploadFolder = "uploads/invoices";
    private const long MaxUploadedInvoiceFileSizeBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllowedUploadedInvoiceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".txt", ".csv"
    };

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
    private static bool IsTextUpload(string ext) =>
        ext.Equals(".txt", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".csv", StringComparison.OrdinalIgnoreCase);

    private static string? SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;
        var safe = Path.GetFileName(fileName.Trim());
        foreach (var c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '_');
        return safe.Length > 256 ? safe[^256..] : safe;
    }

    private static async Task<string?> ValidateTextUploadAsync(IFormFile file, CancellationToken ct)
    {
        var peekLength = (int)Math.Min(2048, file.Length);
        var buffer = new byte[peekLength];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(buffer.AsMemory(0, peekLength), ct);
        if (read == 0)
            return "File is empty.";
        var printable = 0;
        for (var i = 0; i < read; i++)
        {
            var b = buffer[i];
            if (b is 9 or 10 or 13 || (b >= 32 && b <= 126))
                printable++;
        }
        return printable >= Math.Max(1, read * 0.85) ? null : "File content does not look like readable text.";
    }

    private static async Task<string?> ExtractReadableTextAsync(string fullPath, string ext, CancellationToken ct)
    {
        try
        {
            if (IsTextUpload(ext))
            {
                var text = await System.IO.File.ReadAllTextAsync(fullPath, Encoding.UTF8, ct);
                return NormalizeExtractedText(text);
            }

            if (!ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                return null;

            var raw = await System.IO.File.ReadAllTextAsync(fullPath, Encoding.Latin1, ct);
            var matches = Regex.Matches(raw, @"\((?<text>(?:\\.|[^\\)]){2,})\)");
            var parts = matches
                .Select(m => Regex.Unescape(m.Groups["text"].Value))
                .Where(t => t.Any(char.IsLetterOrDigit))
                .Take(400);
            return NormalizeExtractedText(string.Join(" ", parts));
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeExtractedText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var normalized = Regex.Replace(text.Replace('\0', ' '), @"\s+", " ").Trim();
        return normalized.Length == 0 ? null : normalized[..Math.Min(normalized.Length, 4000)];
    }

    private static decimal? ExtractAmount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var matches = Regex.Matches(text, @"(?ix)(?:total|amount\s*due|grand\s*total|invoice\s*total|balance\s*due)?\s*(?:R|ZAR)?\s*(?<amount>\d{1,3}(?:[,\s]\d{3})*(?:\.\d{2})|\d+(?:\.\d{2}))");
        var values = matches
            .Select(m => m.Groups["amount"].Value.Replace(" ", "").Replace(",", ""))
            .Select(s => decimal.TryParse(s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : (decimal?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();
        return values.Count == 0 ? null : values.Max();
    }

    private static string? ExtractInvoiceNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = Regex.Match(text, @"(?ix)\b(?:invoice|inv)\s*(?:no\.?|number|#)?\s*[:#-]?\s*(?<number>[A-Z0-9][A-Z0-9\/\-_.]{2,})");
        return match.Success ? match.Groups["number"].Value.Trim()[..Math.Min(match.Groups["number"].Value.Trim().Length, 128)] : null;
    }

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

    [HttpGet("{id:guid}/uploaded-file")]
    [Authorize(Policy = "RequireViewReports")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUploadedFile(Guid id, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var invoice = await _db.Invoices.AsNoTracking()
            .Include(i => i.Company)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice == null || !invoice.IsUploaded)
            return NotFound();
        if (companyId.HasValue && invoice.Company != null &&
            (isClient ? invoice.CompanyId != companyId : invoice.Company.ParentCompanyId != companyId))
            return NotFound();

        var safePath = FilePathHelper.ValidateAndNormalize(invoice.UploadedFilePath);
        if (safePath == null)
            return NotFound();
        var fullPath = Path.Combine(Directory.GetCurrentDirectory(), safePath.Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(fullPath))
            return NotFound();

        var provider = new FileExtensionContentTypeProvider();
        var contentType = invoice.UploadedContentType;
        if (string.IsNullOrWhiteSpace(contentType) || !contentType.Contains('/'))
            provider.TryGetContentType(fullPath, out contentType);

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, contentType ?? "application/octet-stream", invoice.UploadedFileName ?? Path.GetFileName(fullPath));
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

    [HttpPost("upload")]
    [Authorize(Policy = "RequireManageInvoices")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<InvoiceDto>> Upload([FromForm] UploadInvoiceRequest request, CancellationToken ct)
    {
        if (await IsClientScopedUserAsync(ct))
            return Forbid();
        if (request.File == null || request.File.Length == 0)
            return BadRequest(ApiResponseBodies.Message("No invoice file provided."));
        if (request.File.Length > MaxUploadedInvoiceFileSizeBytes)
            return BadRequest(ApiResponseBodies.Message("File too large (max 10 MB)."));

        var ext = Path.GetExtension(request.File.FileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(ext) || !AllowedUploadedInvoiceExtensions.Contains(ext))
            return BadRequest(ApiResponseBodies.Message("Allowed invoice file types: PDF, PNG, JPG, WEBP, TXT, CSV."));

        var sigErr = IsTextUpload(ext)
            ? await ValidateTextUploadAsync(request.File, ct)
            : await FileContentSignatureHelper.ValidateContentMatchesExtensionAsync(request.File, ext, ct);
        if (sigErr != null)
            return BadRequest(ApiResponseBodies.Message(sigErr));

        var (scopeCompanyId, isClientScope) = await _currentUser.GetClientScopeAsync(ct);
        var job = await _db.JobCards.AsNoTracking()
            .Include(j => j.Site).ThenInclude(s => s!.Company)
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

        var clientId = request.ClientId ?? job.Site?.CompanyId;
        if (!clientId.HasValue)
            return BadRequest(ApiResponseBodies.Message("Client is required."));
        var site = await _db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SiteId, ct);
        if (site == null)
            return BadRequest(ApiResponseBodies.Message("Site not found."));
        if (site.CompanyId != clientId.Value)
            return BadRequest(ApiResponseBodies.Message("Site must belong to the selected client."));

        var quoteForJob = await ResolveQuoteForJobInvoiceAsync(request.JobCardId, job.ServiceRequestId, request.QuoteId, ct);
        if (quoteForJob != null && !string.Equals(quoteForJob.Status, QuoteStatus.Accepted, StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponseBodies.Message("The client must accept the quote before you can create an invoice for this job."));
        var quoteId = request.QuoteId ?? quoteForJob?.Id;

        var invoiceId = Guid.NewGuid();
        var safeOriginalName = SanitizeFileName(request.File.FileName) ?? $"uploaded-invoice{ext}";
        var dir = Path.Combine(Directory.GetCurrentDirectory(), InvoiceUploadFolder);
        Directory.CreateDirectory(dir);
        var storedFileName = $"{invoiceId:N}{ext}";
        var fullPath = Path.Combine(dir, storedFileName);
        await using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await request.File.CopyToAsync(stream, ct);
        }

        var extractedText = await ExtractReadableTextAsync(fullPath, ext, ct);
        var extractedAmount = ExtractAmount(extractedText);
        var extractedInvoiceNumber = ExtractInvoiceNumber(extractedText);
        var lineItems = await BuildInitialInvoiceLineItemsAsync(request.JobCardId, quoteId, ct);
        AddPenaltyLineIfAny(job, lineItems);
        var computedAmount = lineItems.Count > 0 ? ComputeInvoiceTotal(lineItems) : 0m;
        var relativePath = $"{InvoiceUploadFolder}/{storedFileName}";
        var invoice = new Invoice
        {
            Id = invoiceId,
            InvoiceNumber = NumberGenerator.NextInvoiceNumber(_db.Invoices),
            JobCardId = request.JobCardId,
            QuoteId = quoteId,
            CompanyId = clientId.Value,
            SiteId = request.SiteId,
            Amount = extractedAmount ?? computedAmount,
            Currency = "ZAR",
            Status = InvoiceStatus.Draft,
            DueDate = request.DueDate?.Date ?? DateTime.UtcNow.Date.AddDays(14),
            Notes = BuildUploadedInvoiceNotes(request.Notes, extractedInvoiceNumber),
            CreatedAt = DateTime.UtcNow,
            PartsConfirmed = false,
            IsUploaded = true,
            UploadedFilePath = relativePath,
            UploadedFileName = safeOriginalName,
            UploadedContentType = request.File.ContentType,
            UploadedAt = DateTime.UtcNow,
            ExtractedInvoiceNumber = extractedInvoiceNumber,
            ExtractedText = extractedText
        };
        _db.Invoices.Add(invoice);

        var sortOrder = 0;
        foreach (var li in lineItems)
        {
            if (li.Quantity <= 0) continue;
            var partId = li.LineType?.Equals("Part", StringComparison.OrdinalIgnoreCase) == true
                && li.PartId.HasValue
                && await _db.Parts.AnyAsync(p => p.Id == li.PartId.Value, ct)
                    ? li.PartId
                    : null;
            _db.InvoiceLineItems.Add(new InvoiceLineItem
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoice.Id,
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
            .FirstAsync(i => i.Id == invoice.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = invoice.Id }, MapToDto(loaded));
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
            IsUploaded = inv.IsUploaded,
            UploadedFileName = inv.UploadedFileName,
            UploadedContentType = inv.UploadedContentType,
            UploadedAt = inv.UploadedAt,
            ExtractedInvoiceNumber = inv.ExtractedInvoiceNumber,
            ExtractedText = inv.ExtractedText,
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
            return BadRequest(ApiResponseBodies.Message("Invoice email was not sent. Ensure the client email is set, the email provider settings are valid, and any uploaded invoice file still exists."));
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
        var stockUsage = request?.LineItems != null
            ? await BuildPartStockUsageAsync(request.LineItems, ct)
            : BuildPartStockUsage(inv.LineItems);
        await DeductConfirmedPartStockAsync(stockUsage, ct);
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

    private async Task<List<InvoiceLineItemDto>> BuildInitialInvoiceLineItemsAsync(Guid jobCardId, Guid? quoteId, CancellationToken ct)
    {
        var lineItems = new List<InvoiceLineItemDto>();
        var quotePartIds = new HashSet<Guid>();
        if (quoteId.HasValue)
        {
            var quote = await _db.Quotes.AsNoTracking()
                .Include(q => q.LineItems).ThenInclude(li => li.Part)
                .FirstOrDefaultAsync(q => q.Id == quoteId.Value, ct);
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
            .Where(jpp => jpp.JobCardId == jobCardId)
            .ToListAsync(ct);
        var linesByPartId = lineItems
            .Where(li => li.PartId.HasValue)
            .ToDictionary(li => li.PartId!.Value, li => li);
        foreach (var jpp in plannedParts)
        {
            if (linesByPartId.TryGetValue(jpp.PartId, out var existing))
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

        return lineItems;
    }

    private static void AddPenaltyLineIfAny(JobCard job, List<InvoiceLineItemDto> lineItems)
    {
        if (job.ServiceRequest?.PenaltyFee is not decimal penaltyFee || penaltyFee <= 0)
            return;
        lineItems.Add(new InvoiceLineItemDto
        {
            LineType = "Labour",
            Description = string.IsNullOrWhiteSpace(job.ServiceRequest.PenaltyNote)
                ? "Priority inflation penalty"
                : job.ServiceRequest.PenaltyNote.Trim(),
            Quantity = 1,
            UnitPrice = penaltyFee,
            DiscountPercent = 0m,
            PartId = null
        });
    }

    private static string? BuildUploadedInvoiceNotes(string? userNotes, string? extractedInvoiceNumber)
    {
        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(userNotes))
            notes.Add(userNotes.Trim());
        if (!string.IsNullOrWhiteSpace(extractedInvoiceNumber))
            notes.Add($"Original invoice number: {extractedInvoiceNumber.Trim()}");
        return notes.Count == 0 ? null : string.Join(Environment.NewLine, notes);
    }

    private async Task<Dictionary<Guid, int>> BuildPartStockUsageAsync(IEnumerable<InvoiceLineItemDto> lineItems, CancellationToken ct)
    {
        var requestedPartIds = lineItems
            .Where(li => li.LineType?.Equals("Part", StringComparison.OrdinalIgnoreCase) == true && li.PartId.HasValue && li.Quantity > 0)
            .Select(li => li.PartId!.Value)
            .Distinct()
            .ToList();
        if (requestedPartIds.Count == 0)
            return new Dictionary<Guid, int>();

        var validPartIds = await _db.Parts.AsNoTracking()
            .Where(p => requestedPartIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(ct);
        var validPartIdSet = validPartIds.ToHashSet();

        return lineItems
            .Where(li => li.LineType?.Equals("Part", StringComparison.OrdinalIgnoreCase) == true
                && li.PartId.HasValue
                && validPartIdSet.Contains(li.PartId.Value)
                && li.Quantity > 0)
            .GroupBy(li => li.PartId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(li => DecimalQuantityToStockUnits(li.Quantity)));
    }

    private static Dictionary<Guid, int> BuildPartStockUsage(IEnumerable<InvoiceLineItem> lineItems)
    {
        return lineItems
            .Where(li => li.LineType.Equals("Part", StringComparison.OrdinalIgnoreCase)
                && li.PartId.HasValue
                && li.Quantity > 0)
            .GroupBy(li => li.PartId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(li => DecimalQuantityToStockUnits(li.Quantity)));
    }

    private async Task DeductConfirmedPartStockAsync(IReadOnlyDictionary<Guid, int> stockUsage, CancellationToken ct)
    {
        if (stockUsage.Count == 0)
            return;

        var partIds = stockUsage.Keys.ToList();
        var parts = await _db.Parts.Where(p => partIds.Contains(p.Id)).ToListAsync(ct);
        foreach (var part in parts)
            part.Quantity = Math.Max(0, part.Quantity - stockUsage[part.Id]);
    }

    private static int DecimalQuantityToStockUnits(decimal quantity)
    {
        if (quantity <= 0)
            return 0;
        return (int)Math.Ceiling(quantity);
    }
}
