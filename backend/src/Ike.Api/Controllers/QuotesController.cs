using System.Text;
using System.Text.RegularExpressions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.DTOs.Quotes;
using Ike.Api.Helpers;
using Ike.Api.Models;
using Ike.Api.Services;

namespace Ike.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class QuotesController : ControllerBase
{
    private const string DiscountModeNone = "None";
    private const string DiscountModeGlobal = "Global";
    private const string DiscountModePerItem = "PerItem";
    private const string QuoteUploadFolder = "uploads/quotes";
    private const long MaxUploadedQuoteFileSizeBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllowedUploadedQuoteExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".txt", ".csv"
    };

    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IEmailService _emailService;
    private readonly ICurrentUserService _currentUser;
    private readonly IStatusTransitionService _statusTransitions;
    private readonly IScopeGuardService _scopeGuard;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPermissionService _permissionService;

    public QuotesController(
        ApplicationDbContext db,
        IWebHostEnvironment env,
        IEmailService emailService,
        ICurrentUserService currentUser,
        IStatusTransitionService statusTransitions,
        IScopeGuardService scopeGuard,
        UserManager<ApplicationUser> userManager,
        IPermissionService permissionService)
    {
        _db = db;
        _env = env;
        _emailService = emailService;
        _currentUser = currentUser;
        _statusTransitions = statusTransitions;
        _scopeGuard = scopeGuard;
        _userManager = userManager;
        _permissionService = permissionService;
    }

    private static decimal ClampDiscountPercent(decimal value)
    {
        if (value < 0m) return 0m;
        if (value > 100m) return 100m;
        return value;
    }

    private static string NormalizeDiscountMode(string? mode)
    {
        var m = (mode ?? string.Empty).Trim();
        if (m.Equals(DiscountModeGlobal, StringComparison.OrdinalIgnoreCase)) return DiscountModeGlobal;
        if (m.Equals(DiscountModePerItem, StringComparison.OrdinalIgnoreCase)) return DiscountModePerItem;
        return DiscountModeNone;
    }

    private static decimal ComputeSubtotal(IEnumerable<QuoteLineItemDto> lineItems) =>
        lineItems.Where(li => li.Quantity > 0 && li.UnitPrice >= 0).Sum(li => li.Quantity * li.UnitPrice);

    private static decimal ComputePerItemDiscount(IEnumerable<QuoteLineItemDto> lineItems, string discountMode)
    {
        if (!discountMode.Equals(DiscountModePerItem, StringComparison.OrdinalIgnoreCase))
            return 0m;
        return lineItems
            .Where(li => li.Quantity > 0 && li.UnitPrice >= 0)
            .Sum(li =>
            {
                var sub = li.Quantity * li.UnitPrice;
                var pct = ClampDiscountPercent(li.DiscountPercent);
                return Math.Round(sub * (pct / 100m), 2, MidpointRounding.AwayFromZero);
            });
    }

    private static decimal ComputeQuoteAmountFromLines(IEnumerable<QuoteLineItemDto> lineItems, string discountMode, decimal globalDiscountPercent)
    {
        var subtotal = ComputeSubtotal(lineItems);
        var perItemDiscount = ComputePerItemDiscount(lineItems, discountMode);
        var afterPerItem = Math.Max(0m, subtotal - perItemDiscount);
        if (!discountMode.Equals(DiscountModeGlobal, StringComparison.OrdinalIgnoreCase))
            return Math.Round(afterPerItem, 2, MidpointRounding.AwayFromZero);
        var pct = ClampDiscountPercent(globalDiscountPercent);
        var globalDiscount = Math.Round(afterPerItem * (pct / 100m), 2, MidpointRounding.AwayFromZero);
        return Math.Round(Math.Max(0m, afterPerItem - globalDiscount), 2, MidpointRounding.AwayFromZero);
    }

    private static (decimal subtotal, decimal discount) ComputeQuoteSummary(IEnumerable<QuoteLineItemResponseDto> lineItems, string discountMode, decimal globalDiscountPercent, decimal fallbackAmount)
    {
        var list = lineItems.ToList();
        if (list.Count == 0)
            return (fallbackAmount, 0m);

        var subtotal = list.Sum(li => li.LineSubtotal);
        var perItemDiscount = discountMode == DiscountModePerItem ? list.Sum(li => li.LineDiscountAmount) : 0m;
        var afterPerItem = Math.Max(0m, subtotal - perItemDiscount);
        var globalDiscount = discountMode == DiscountModeGlobal
            ? Math.Round(afterPerItem * (ClampDiscountPercent(globalDiscountPercent) / 100m), 2, MidpointRounding.AwayFromZero)
            : 0m;
        return (Math.Round(subtotal, 2, MidpointRounding.AwayFromZero), Math.Round(perItemDiscount + globalDiscount, 2, MidpointRounding.AwayFromZero));
    }

    private static bool IsPartLine(QuoteLineItemDto li) =>
        string.Equals(li.LineType, "Part", StringComparison.OrdinalIgnoreCase);

    private static string? SanitizeFileName(string? fileName)
    {
        var safe = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(safe))
            return null;
        foreach (var c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '_');
        return safe.Length > 256 ? safe[^256..] : safe;
    }

    private static bool IsTextUpload(string ext) =>
        ext.Equals(".txt", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".csv", StringComparison.OrdinalIgnoreCase);

    private static async Task<string?> ValidateTextUploadAsync(IFormFile file, CancellationToken ct)
    {
        var peekLength = (int)Math.Min(2048, file.Length);
        var buffer = new byte[peekLength];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(buffer.AsMemory(0, peekLength), ct);
        if (read == 0)
            return "Empty file.";
        var printable = 0;
        for (var i = 0; i < read; i++)
        {
            var b = buffer[i];
            if (b == 9 || b == 10 || b == 13 || (b >= 32 && b <= 126))
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

            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath, ct);
            var raw = Encoding.Latin1.GetString(bytes);
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
        var matches = Regex.Matches(text, @"(?ix)(?:total|amount\s*due|grand\s*total|quote\s*total)?\s*(?:R|ZAR)?\s*(?<amount>\d{1,3}(?:[,\s]\d{3})*(?:\.\d{2})|\d+(?:\.\d{2}))");
        var values = matches
            .Select(m => decimal.TryParse(m.Groups["amount"].Value.Replace(",", "").Replace(" ", ""), out var value) ? value : (decimal?)null)
            .Where(v => v.HasValue && v.Value >= 0)
            .Select(v => v!.Value)
            .ToList();
        return values.Count == 0 ? null : values.Max();
    }

    private static string? ExtractQuoteNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = Regex.Match(text, @"(?ix)\b(?:quote|quotation)\s*(?:no\.?|number|#)?\s*[:#-]?\s*(?<number>[A-Z0-9][A-Z0-9\/\-_.]{2,})");
        return match.Success ? match.Groups["number"].Value.Trim()[..Math.Min(match.Groups["number"].Value.Trim().Length, 128)] : null;
    }

    private static string? ExtractSupplierName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = Regex.Match(text, @"(?im)^\s*(?<name>[A-Z][A-Za-z0-9&.,'()\- ]{2,80})\s*$");
        return match.Success ? match.Groups["name"].Value.Trim()[..Math.Min(match.Groups["name"].Value.Trim().Length, 256)] : null;
    }

    private async Task<HashSet<Guid>> GetAllowedPartIdsAsync(Guid quoteCompanyId, Guid? quoteCompanyParentId, IEnumerable<Guid> requestedPartIds, CancellationToken ct)
    {
        var partIds = requestedPartIds.Distinct().ToList();
        if (partIds.Count == 0) return new HashSet<Guid>();

        var allowedCompanyIds = new List<Guid> { quoteCompanyId };
        if (quoteCompanyParentId.HasValue)
            allowedCompanyIds.Add(quoteCompanyParentId.Value);

        var allowedIds = await _db.Parts.AsNoTracking()
            .Where(p => partIds.Contains(p.Id)
                        && p.CompanyId.HasValue
                        && allowedCompanyIds.Contains(p.CompanyId.Value))
            .Select(p => p.Id)
            .ToListAsync(ct);

        return allowedIds.ToHashSet();
    }

    [HttpGet]
    [Authorize(Policy = "RequireViewPurchaseOrders")]
    [ProducesResponseType(typeof(List<QuoteDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<QuoteDto>>> List([FromQuery] Guid? clientId, [FromQuery] Guid? siteId, [FromQuery] string? status, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        IQueryable<Quote> query = _db.Quotes.AsNoTracking()
            .Include(q => q.Company)
            .Include(q => q.Site);
        if (companyId.HasValue)
        {
            if (isClient)
                query = query.Where(q => q.CompanyId == companyId);
            else
                query = query.Where(q => q.Company != null && q.Company.ParentCompanyId == companyId);
        }
        if (clientId.HasValue)
            query = query.Where(q => q.CompanyId == clientId);
        if (siteId.HasValue)
            query = query.Where(q => q.SiteId == siteId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!QuoteStatus.IsValid(status))
                return BadRequest(ApiResponseBodies.Message($"Status must be one of: {string.Join(", ", QuoteStatus.All)}."));
            var normalized = QuoteStatus.Normalize(status);
            query = query.Where(q => q.Status == normalized);
        }

        var list = await query.OrderByDescending(q => q.CreatedAt)
            .Select(q => new QuoteDto
            {
                Id = q.Id,
                QuoteNumber = q.QuoteNumber,
                ClientId = q.CompanyId,
                ClientName = q.Company != null ? q.Company.Name : null,
                SiteId = q.SiteId,
                SiteName = q.Site != null ? q.Site.Name : null,
                JobCardId = q.JobCardId,
                ServiceRequestId = q.ServiceRequestId,
                Amount = q.Amount,
                Currency = q.Currency,
                Description = q.Description,
                DeferPricing = q.DeferPricing,
                IsUploaded = q.IsUploaded,
                UploadedFileName = q.UploadedFileName,
                UploadedContentType = q.UploadedContentType,
                UploadedAt = q.UploadedAt,
                ExtractedQuoteNumber = q.ExtractedQuoteNumber,
                ExtractedSupplierName = q.ExtractedSupplierName,
                ExtractedText = q.ExtractedText,
                DiscountMode = q.DiscountMode,
                GlobalDiscountPercent = q.GlobalDiscountPercent,
                SubtotalAmount = q.Amount,
                DiscountAmount = 0,
                Notes = q.Notes,
                Status = q.Status,
                ValidUntil = q.ValidUntil,
                SentAt = q.SentAt,
                CreatedAt = q.CreatedAt,
                LinkedPurchaseOrderId = null,
                LinkedPurchaseOrderNumber = null
            }).ToListAsync();
        var quoteIds = list.Select(q => q.Id).ToList();
        var lineStats = await _db.QuoteLineItems.AsNoTracking()
            .Where(li => quoteIds.Contains(li.QuoteId))
            .GroupBy(li => li.QuoteId)
            .Select(g => new
            {
                QuoteId = g.Key,
                Subtotal = g.Sum(li => li.Quantity * li.UnitPrice),
                PerItemDiscount = g.Sum(li => (li.Quantity * li.UnitPrice) * (li.DiscountPercent / 100m))
            })
            .ToListAsync(ct);
        var lineStatsMap = lineStats.ToDictionary(x => x.QuoteId);
        var linkedPos = await _db.PurchaseOrders.AsNoTracking()
            .Where(po => po.QuoteId != null && quoteIds.Contains(po.QuoteId.Value))
            .Select(po => new { po.QuoteId, po.Id, po.PONumber })
            .ToListAsync();
        foreach (var dto in list)
        {
            var mode = NormalizeDiscountMode(dto.DiscountMode);
            if (lineStatsMap.TryGetValue(dto.Id, out var stat))
            {
                var subtotal = Math.Round(stat.Subtotal, 2, MidpointRounding.AwayFromZero);
                var perItemDiscount = mode == DiscountModePerItem
                    ? Math.Round(stat.PerItemDiscount, 2, MidpointRounding.AwayFromZero)
                    : 0m;
                var afterPerItem = Math.Max(0m, subtotal - perItemDiscount);
                var globalDiscount = mode == DiscountModeGlobal
                    ? Math.Round(afterPerItem * (ClampDiscountPercent(dto.GlobalDiscountPercent) / 100m), 2, MidpointRounding.AwayFromZero)
                    : 0m;
                dto.SubtotalAmount = subtotal;
                dto.DiscountAmount = Math.Round(perItemDiscount + globalDiscount, 2, MidpointRounding.AwayFromZero);
            }
            else
            {
                dto.SubtotalAmount = dto.Amount;
                dto.DiscountAmount = 0m;
            }

            var po = linkedPos.FirstOrDefault(p => p.QuoteId == dto.Id);
            if (po != null)
            {
                dto.LinkedPurchaseOrderId = po.Id;
                dto.LinkedPurchaseOrderNumber = po.PONumber;
            }
        }
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "RequireViewPurchaseOrders")]
    [ProducesResponseType(typeof(QuoteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuoteDto>> Get(Guid id, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var q = await _db.Quotes.AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Site)
            .Include(x => x.LineItems)
            .ThenInclude(li => li.Part)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (q == null)
            return NotFound();
        if (companyId.HasValue && q.Company != null &&
                (isClient ? q.CompanyId != companyId : q.Company.ParentCompanyId != companyId))
            return NotFound();
        var linkedPo = await _db.PurchaseOrders.AsNoTracking()
            .Where(po => po.QuoteId == id)
            .Select(po => new { po.Id, po.PONumber })
            .FirstOrDefaultAsync();
        return Ok(MapToDto(q, linkedPo?.Id, linkedPo?.PONumber));
    }

    [HttpGet("{id:guid}/uploaded-file")]
    [Authorize(Policy = "RequireViewPurchaseOrders")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUploadedFile(Guid id, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var quote = await _db.Quotes.AsNoTracking()
            .Include(q => q.Company)
            .FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote == null || !quote.IsUploaded)
            return NotFound();
        if (companyId.HasValue && quote.Company != null &&
            (isClient ? quote.CompanyId != companyId : quote.Company.ParentCompanyId != companyId))
            return NotFound();

        var safePath = FilePathHelper.ValidateAndNormalize(quote.UploadedFilePath);
        if (safePath == null)
            return NotFound();
        var fullPath = Path.Combine(_env.ContentRootPath, safePath.Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(fullPath))
            return NotFound();

        var provider = new FileExtensionContentTypeProvider();
        var contentType = quote.UploadedContentType;
        if (string.IsNullOrWhiteSpace(contentType) || !contentType.Contains('/'))
            provider.TryGetContentType(fullPath, out contentType);

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, contentType ?? "application/octet-stream", quote.UploadedFileName ?? Path.GetFileName(fullPath));
    }

    private static QuoteDto MapToDto(Quote q, Guid? linkedPoId, string? linkedPoNumber)
    {
        var lineItems = q.LineItems?.OrderBy(li => li.SortOrder).Select(li => new QuoteLineItemResponseDto
        {
            Id = li.Id,
            LineType = li.LineType,
            Description = li.Description,
            Quantity = li.Quantity,
            UnitPrice = li.UnitPrice,
            DiscountPercent = li.DiscountPercent,
            PartId = li.PartId,
            PartName = li.Part?.Name
        }).ToList() ?? new List<QuoteLineItemResponseDto>();
        var mode = NormalizeDiscountMode(q.DiscountMode);
        var (subtotal, discount) = ComputeQuoteSummary(lineItems, mode, q.GlobalDiscountPercent, q.Amount);
        return new QuoteDto
        {
            Id = q.Id,
            QuoteNumber = q.QuoteNumber,
            ClientId = q.CompanyId,
            ClientName = q.Company?.Name,
            SiteId = q.SiteId,
            SiteName = q.Site?.Name,
            JobCardId = q.JobCardId,
            ServiceRequestId = q.ServiceRequestId,
            Amount = q.Amount,
            Currency = q.Currency,
            Description = q.Description,
            DeferPricing = q.DeferPricing,
            IsUploaded = q.IsUploaded,
            UploadedFileName = q.UploadedFileName,
            UploadedContentType = q.UploadedContentType,
            UploadedAt = q.UploadedAt,
            ExtractedQuoteNumber = q.ExtractedQuoteNumber,
            ExtractedSupplierName = q.ExtractedSupplierName,
            ExtractedText = q.ExtractedText,
            DiscountMode = mode,
            GlobalDiscountPercent = ClampDiscountPercent(q.GlobalDiscountPercent),
            SubtotalAmount = subtotal,
            DiscountAmount = discount,
            Notes = q.Notes,
            Status = q.Status,
            ValidUntil = q.ValidUntil,
            SentAt = q.SentAt,
            CreatedAt = q.CreatedAt,
            LinkedPurchaseOrderId = linkedPoId,
            LinkedPurchaseOrderNumber = linkedPoNumber,
            LineItems = lineItems
        };
    }

    [HttpPost]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(typeof(QuoteDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<QuoteDto>> Create([FromBody] CreateQuoteRequest request, CancellationToken ct)
    {
        var (scopeCompanyId, isClientScope) = await _currentUser.GetClientScopeAsync(ct);
        var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ClientId, ct);
        var site = await _db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SiteId, ct);
        if (company == null || site == null)
            return BadRequest(ApiResponseBodies.Message("Client or site not found."));
        if (site.CompanyId != request.ClientId)
            return BadRequest(ApiResponseBodies.Message("Site must belong to the selected client."));
        if (scopeCompanyId.HasValue)
        {
            var inClientScope = isClientScope
                ? request.ClientId == scopeCompanyId
                : company.ParentCompanyId == scopeCompanyId;
            if (!inClientScope)
                return NotFound();
        }
        if (request.ServiceRequestId.HasValue)
        {
            var sr = await _db.ServiceRequests.AsNoTracking()
                .Include(s => s.Site)
                .FirstOrDefaultAsync(s => s.Id == request.ServiceRequestId.Value, ct);
            if (sr == null)
                return BadRequest(ApiResponseBodies.Message("Service request not found."));
            if (sr.SiteId != request.SiteId || sr.Site.CompanyId != request.ClientId)
                return BadRequest(ApiResponseBodies.Message("Service request must belong to the selected client and site."));
        }
        if (request.JobCardId.HasValue)
        {
            var job = await _db.JobCards.AsNoTracking().FirstOrDefaultAsync(j => j.Id == request.JobCardId.Value, ct);
            if (job == null)
                return BadRequest(ApiResponseBodies.Message("Job card not found."));
            if (job.SiteId != request.SiteId)
                return BadRequest(ApiResponseBodies.Message("Quote site must match job card site."));
        }
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

        var amount = request.Amount;
        var lineItems = request.LineItems ?? new List<QuoteLineItemDto>();
        var deferPricing = request.DeferPricing;
        var discountMode = NormalizeDiscountMode(request.DiscountMode);
        var globalDiscountPercent = ClampDiscountPercent(request.GlobalDiscountPercent);
        if (lineItems.Count > 0 && !deferPricing)
            amount = ComputeQuoteAmountFromLines(lineItems, discountMode, globalDiscountPercent);
        if (deferPricing && amount < 0) amount = 0;

        var requestedPartIds = lineItems
            .Where(li => IsPartLine(li) && li.PartId.HasValue && li.Quantity > 0)
            .Select(li => li.PartId!.Value)
            .ToList();
        var allowedPartIds = await GetAllowedPartIdsAsync(request.ClientId, company.ParentCompanyId, requestedPartIds, ct);
        var invalidPart = requestedPartIds.FirstOrDefault(id => !allowedPartIds.Contains(id));
        if (invalidPart != Guid.Empty)
            return BadRequest(ApiResponseBodies.Message("One or more selected parts are outside your tenant scope or invalid for this quote."));

        var quoteNumber = NumberGenerator.NextQuoteNumber(_db.Quotes);
        var q = new Quote
        {
            Id = Guid.NewGuid(),
            QuoteNumber = quoteNumber,
            CompanyId = request.ClientId,
            SiteId = request.SiteId,
            JobCardId = request.JobCardId,
            ServiceRequestId = request.ServiceRequestId,
            Amount = amount,
            Currency = request.Currency?.Trim() ?? "ZAR",
            Description = request.Description.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            DeferPricing = deferPricing,
            DiscountMode = discountMode,
            GlobalDiscountPercent = globalDiscountPercent,
            Status = QuoteStatus.Draft,
            ValidUntil = request.ValidUntil,
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow
        };
        _db.Quotes.Add(q);
        var sortOrder = 0;
        foreach (var li in lineItems)
        {
            if (li.Quantity <= 0) continue;
            var partId = IsPartLine(li) && li.PartId.HasValue && allowedPartIds.Contains(li.PartId.Value) ? li.PartId : null;
            _db.QuoteLineItems.Add(new QuoteLineItem
            {
                Id = Guid.NewGuid(),
                QuoteId = q.Id,
                LineType = (li.LineType ?? "Labour").Trim().Length > 0 ? li.LineType!.Trim() : "Labour",
                Description = li.Description?.Trim() ?? "",
                Quantity = li.Quantity,
                UnitPrice = li.UnitPrice,
                DiscountPercent = ClampDiscountPercent(li.DiscountPercent),
                SortOrder = sortOrder++,
                PartId = partId
            });
            if (partId.HasValue)
            {
                var part = await _db.Parts.FindAsync([partId.Value], ct);
                if (part != null)
                {
                    part.UnitPrice = li.UnitPrice;
                    part.UpdatedAt = DateTime.UtcNow;
                }
            }
        }
        await _db.SaveChangesAsync(ct);

        if (q.JobCardId.HasValue)
        {
            await SyncPlannedPartsFromQuoteLineItemsAsync(q.JobCardId.Value, lineItems, ct);
            await _db.SaveChangesAsync(ct);
        }
        else if (q.ServiceRequestId.HasValue)
        {
            var relatedJobIds = await _db.JobCards.AsNoTracking()
                .Where(j => j.ServiceRequestId == q.ServiceRequestId.Value)
                .Select(j => j.Id)
                .ToListAsync(ct);
            foreach (var jobId in relatedJobIds)
                await SyncPlannedPartsFromQuoteLineItemsAsync(jobId, lineItems, ct);
            if (relatedJobIds.Count > 0)
                await _db.SaveChangesAsync(ct);
        }

        var loaded = await _db.Quotes.AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Site)
            .Include(x => x.LineItems)
            .ThenInclude(li => li.Part)
            .FirstAsync(x => x.Id == q.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = q.Id }, MapToDto(loaded, null, null));
    }

    [HttpPost("upload")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(QuoteDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<QuoteDto>> Upload([FromForm] UploadQuoteRequest request, CancellationToken ct)
    {
        if (request.File == null || request.File.Length == 0)
            return BadRequest(ApiResponseBodies.Message("No quote file provided."));
        if (request.File.Length > MaxUploadedQuoteFileSizeBytes)
            return BadRequest(ApiResponseBodies.Message("File too large (max 10 MB)."));

        var ext = Path.GetExtension(request.File.FileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(ext) || !AllowedUploadedQuoteExtensions.Contains(ext))
            return BadRequest(ApiResponseBodies.Message("Allowed quote file types: PDF, PNG, JPG, WEBP, TXT, CSV."));

        var sigErr = IsTextUpload(ext)
            ? await ValidateTextUploadAsync(request.File, ct)
            : await FileContentSignatureHelper.ValidateContentMatchesExtensionAsync(request.File, ext, ct);
        if (sigErr != null)
            return BadRequest(ApiResponseBodies.Message(sigErr));

        var (scopeCompanyId, isClientScope) = await _currentUser.GetClientScopeAsync(ct);
        var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ClientId, ct);
        var site = await _db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SiteId, ct);
        if (company == null || site == null)
            return BadRequest(ApiResponseBodies.Message("Client or site not found."));
        if (site.CompanyId != request.ClientId)
            return BadRequest(ApiResponseBodies.Message("Site must belong to the selected client."));
        if (scopeCompanyId.HasValue)
        {
            var inClientScope = isClientScope
                ? request.ClientId == scopeCompanyId
                : company.ParentCompanyId == scopeCompanyId;
            if (!inClientScope)
                return NotFound();
        }
        if (request.ServiceRequestId.HasValue)
        {
            var sr = await _db.ServiceRequests.AsNoTracking()
                .Include(s => s.Site)
                .FirstOrDefaultAsync(s => s.Id == request.ServiceRequestId.Value, ct);
            if (sr == null)
                return BadRequest(ApiResponseBodies.Message("Service request not found."));
            if (sr.SiteId != request.SiteId || sr.Site.CompanyId != request.ClientId)
                return BadRequest(ApiResponseBodies.Message("Service request must belong to the selected client and site."));
        }
        if (request.JobCardId.HasValue)
        {
            var job = await _db.JobCards.AsNoTracking().FirstOrDefaultAsync(j => j.Id == request.JobCardId.Value, ct);
            if (job == null)
                return BadRequest(ApiResponseBodies.Message("Job card not found."));
            if (job.SiteId != request.SiteId)
                return BadRequest(ApiResponseBodies.Message("Quote site must match job card site."));
        }

        var quoteId = Guid.NewGuid();
        var safeOriginalName = SanitizeFileName(request.File.FileName) ?? $"uploaded-quote{ext}";
        var dir = Path.Combine(_env.ContentRootPath, QuoteUploadFolder);
        Directory.CreateDirectory(dir);
        var storedFileName = $"{quoteId:N}{ext}";
        var fullPath = Path.Combine(dir, storedFileName);
        await using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await request.File.CopyToAsync(stream, ct);
        }

        var extractedText = await ExtractReadableTextAsync(fullPath, ext, ct);
        var extractedAmount = ExtractAmount(extractedText);
        var extractedQuoteNumber = ExtractQuoteNumber(extractedText);
        var extractedSupplierName = ExtractSupplierName(extractedText);
        var amount = extractedAmount ?? 0m;
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var relativePath = $"{QuoteUploadFolder}/{storedFileName}";
        var quote = new Quote
        {
            Id = quoteId,
            QuoteNumber = NumberGenerator.NextQuoteNumber(_db.Quotes),
            CompanyId = request.ClientId,
            SiteId = request.SiteId,
            JobCardId = request.JobCardId,
            ServiceRequestId = request.ServiceRequestId,
            Amount = amount,
            Currency = "ZAR",
            Description = extractedSupplierName != null
                ? $"Uploaded quote from {extractedSupplierName}"
                : $"Uploaded quote: {safeOriginalName}",
            Notes = extractedQuoteNumber != null ? $"Original quote number: {extractedQuoteNumber}" : null,
            Status = QuoteStatus.Draft,
            DeferPricing = !extractedAmount.HasValue,
            DiscountMode = DiscountModeNone,
            GlobalDiscountPercent = 0,
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow,
            IsUploaded = true,
            UploadedFilePath = relativePath,
            UploadedFileName = safeOriginalName,
            UploadedContentType = request.File.ContentType,
            UploadedAt = DateTime.UtcNow,
            ExtractedQuoteNumber = extractedQuoteNumber,
            ExtractedSupplierName = extractedSupplierName,
            ExtractedText = extractedText
        };
        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync(ct);

        var loaded = await _db.Quotes.AsNoTracking()
            .Include(q => q.Company)
            .Include(q => q.Site)
            .Include(q => q.LineItems)
            .FirstAsync(q => q.Id == quote.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = quote.Id }, MapToDto(loaded, null, null));
    }

    [HttpPost("{id:guid}/send")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Send(Guid id, [FromQuery] string? toEmail, [FromQuery] bool attachPdf = true, CancellationToken ct = default)
    {
        var quote = await LoadQuoteInScopeForUpdateAsync(id, ct);
        if (quote == null)
            return NotFound();
        if (string.Equals(quote.Status, QuoteStatus.Cancelled, StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponseBodies.Message("Cancelled quotes cannot be sent to the client."));
        var sent = await _emailService.SendQuoteToClientAsync(id, toEmail, attachPdf, ct);
        if (!sent)
            return BadRequest(ApiResponseBodies.Message(
                "The quote email was not sent. Common causes: missing client contact email (or toEmail query), missing uploaded quote file, wrong email provider settings, or the email provider rejected the message — check API logs."));
        quote.SentAt = DateTime.UtcNow;
        if (!string.Equals(quote.Status, QuoteStatus.Accepted, StringComparison.OrdinalIgnoreCase))
        {
            if (!_statusTransitions.TryTransitionQuote(quote.Status, QuoteStatus.Sent, out var nextStatus, out var transitionError))
                return BadRequest(ApiResponseBodies.Message(transitionError));
            quote.Status = nextStatus;
        }
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Client accepts a sent quote, or staff records acceptance (ManagePurchaseOrders).</summary>
    [HttpPost("{id:guid}/accept")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Accept(Guid id, CancellationToken ct = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return Unauthorized();

        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var canManage = await _permissionService.HasPermissionAsync(user, "ManagePurchaseOrders");

        var quote = await LoadQuoteInScopeForUpdateAsync(id, ct);
        if (quote == null)
            return NotFound();

        if (isClient)
        {
            if (!companyId.HasValue || quote.CompanyId != companyId.Value)
                return Forbid();
        }
        else if (!canManage)
        {
            return Forbid();
        }

        if (!_statusTransitions.TryTransitionQuote(quote.Status, QuoteStatus.Accepted, out var nextStatus, out var transitionError))
            return BadRequest(ApiResponseBodies.Message(transitionError));
        quote.Status = nextStatus;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(typeof(QuoteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuoteDto>> UpdateStatus(Guid id, [FromBody] UpdateQuoteStatusRequest request, CancellationToken ct = default)
    {
        var q = await LoadQuoteInScopeForUpdateAsync(id, ct);
        if (q == null)
            return NotFound();
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!_statusTransitions.TryTransitionQuote(q.Status, request.Status, out var nextStatus, out var transitionError))
                return BadRequest(ApiResponseBodies.Message(transitionError));
            q.Status = nextStatus;
        }
        await _db.SaveChangesAsync(ct);
        return await Get(id);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(typeof(QuoteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuoteDto>> Update(Guid id, [FromBody] UpdateQuoteRequest request, CancellationToken ct = default)
    {
        var q = await LoadQuoteInScopeForUpdateAsync(id, ct, includeLineItems: true);
        if (q == null)
            return NotFound();
        if (q.IsUploaded)
            return BadRequest(ApiResponseBodies.Message("Uploaded quotes are read-only and cannot be edited."));
        var amount = request.Amount;
        var lineItems = request.LineItems ?? new List<QuoteLineItemDto>();
        var discountMode = NormalizeDiscountMode(request.DiscountMode);
        var globalDiscountPercent = ClampDiscountPercent(request.GlobalDiscountPercent);
        if (lineItems.Count > 0 && !q.DeferPricing)
            amount = ComputeQuoteAmountFromLines(lineItems, discountMode, globalDiscountPercent);

        if (request.LineItems != null)
        {
            var requestedPartIds = lineItems
                .Where(li => IsPartLine(li) && li.PartId.HasValue && li.Quantity > 0)
                .Select(li => li.PartId!.Value)
                .ToList();
            var allowedPartIds = await GetAllowedPartIdsAsync(q.CompanyId, q.Company?.ParentCompanyId, requestedPartIds, ct);
            var invalidPart = requestedPartIds.FirstOrDefault(id => !allowedPartIds.Contains(id));
            if (invalidPart != Guid.Empty)
                return BadRequest(ApiResponseBodies.Message("One or more selected parts are outside your tenant scope or invalid for this quote."));
        }
        q.Amount = amount;
        q.Currency = request.Currency?.Trim() ?? "ZAR";
        q.Description = request.Description.Trim();
        q.DiscountMode = discountMode;
        q.GlobalDiscountPercent = globalDiscountPercent;
        q.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        q.ValidUntil = request.ValidUntil;
        if (request.LineItems != null)
        {
            var allowedPartIds = await GetAllowedPartIdsAsync(q.CompanyId, q.Company?.ParentCompanyId,
                lineItems.Where(x => IsPartLine(x) && x.PartId.HasValue && x.Quantity > 0).Select(x => x.PartId!.Value), ct);
            _db.QuoteLineItems.RemoveRange(q.LineItems);
            var sortOrder = 0;
            foreach (var li in lineItems)
            {
                if (li.Quantity <= 0) continue;
                var partId = IsPartLine(li) && li.PartId.HasValue && allowedPartIds.Contains(li.PartId.Value) ? li.PartId : null;
                _db.QuoteLineItems.Add(new QuoteLineItem
                {
                    Id = Guid.NewGuid(),
                    QuoteId = id,
                    LineType = (li.LineType ?? "Labour").Trim().Length > 0 ? li.LineType!.Trim() : "Labour",
                    Description = li.Description?.Trim() ?? "",
                    Quantity = li.Quantity,
                    UnitPrice = li.UnitPrice,
                    DiscountPercent = ClampDiscountPercent(li.DiscountPercent),
                    SortOrder = sortOrder++,
                    PartId = partId
                });
            }
            if (q.JobCardId.HasValue)
                await SyncPlannedPartsFromQuoteLineItemsAsync(q.JobCardId.Value, lineItems, ct);
            else if (q.ServiceRequestId.HasValue)
            {
                var relatedJobIds = await _db.JobCards.AsNoTracking()
                    .Where(j => j.ServiceRequestId == q.ServiceRequestId.Value)
                    .Select(j => j.Id)
                    .ToListAsync(ct);
                foreach (var jobId in relatedJobIds)
                    await SyncPlannedPartsFromQuoteLineItemsAsync(jobId, lineItems, ct);
            }
        }
        await _db.SaveChangesAsync(ct);
        return await Get(id);
    }

    [HttpPost("{id:guid}/link-job-card")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(typeof(QuoteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<QuoteDto>> LinkToJobCard(Guid id, [FromBody] LinkQuoteToJobCardRequest request, CancellationToken ct = default)
    {
        var q = await LoadQuoteInScopeForUpdateAsync(id, ct, includeLineItems: true);
        if (q == null)
            return NotFound();
        var job = await _db.JobCards.AsNoTracking().Include(j => j.Site).FirstOrDefaultAsync(j => j.Id == request.JobCardId, ct);
        if (job == null)
            return BadRequest(ApiResponseBodies.Message("Job card not found."));
        if (job.SiteId != q.SiteId)
            return BadRequest(ApiResponseBodies.Message("Quote site must match job card site."));
        q.JobCardId = request.JobCardId;
        await SyncPlannedPartsFromQuoteLineItemsAsync(request.JobCardId, q.LineItems.Select(li => new QuoteLineItemDto
        {
            LineType = li.LineType,
            Description = li.Description,
            Quantity = li.Quantity,
            UnitPrice = li.UnitPrice,
            DiscountPercent = li.DiscountPercent,
            PartId = li.PartId
        }), ct);
        await _db.SaveChangesAsync(ct);
        return await Get(id);
    }

    internal async Task SyncPlannedPartsFromQuoteLineItemsAsync(Guid jobCardId, IEnumerable<QuoteLineItemDto> lineItems, CancellationToken ct)
    {
        var lineList = lineItems?.ToList() ?? new List<QuoteLineItemDto>();
        if (lineList.Count == 0) return;

        var job = await _db.JobCards.AsNoTracking()
            .Include(j => j.Site).ThenInclude(s => s!.Company)
            .FirstOrDefaultAsync(j => j.Id == jobCardId, ct);
        if (job?.Site == null) return;

        if (!job.Site.CompanyId.HasValue) return;
        var allowedCompanyIds = new HashSet<Guid> { job.Site.CompanyId.Value };
        if (job.Site.Company?.ParentCompanyId.HasValue == true)
            allowedCompanyIds.Add(job.Site.Company.ParentCompanyId.Value);

        var candidatePartIds = lineList
            .Where(li =>
                string.Equals(li.LineType, "Part", StringComparison.OrdinalIgnoreCase)
                && li.PartId.HasValue
                && li.Quantity > 0)
            .Select(li => li.PartId!.Value)
            .Distinct()
            .ToList();
        if (candidatePartIds.Count == 0) return;

        var attachablePartIds = await _db.Parts.AsNoTracking()
            .Where(p =>
                candidatePartIds.Contains(p.Id)
                && p.CompanyId.HasValue
                && allowedCompanyIds.Contains(p.CompanyId.Value)
                && !p.IsLabour)
            .Select(p => p.Id)
            .ToListAsync(ct);
        if (attachablePartIds.Count == 0) return;
        var attachableSet = attachablePartIds.ToHashSet();

        var existingPlanned = await _db.JobCardPlannedParts
            .Where(jpp => jpp.JobCardId == jobCardId)
            .ToListAsync(ct);
        var plannedByPartId = existingPlanned.ToDictionary(jpp => jpp.PartId);

        foreach (var li in lineList.Where(li =>
                     string.Equals(li.LineType, "Part", StringComparison.OrdinalIgnoreCase)
                     && li.PartId.HasValue
                     && li.Quantity > 0
                     && attachableSet.Contains(li.PartId.Value)))
        {
            var partId = li.PartId!.Value;
            var qty = (int)Math.Max(1, Math.Ceiling(li.Quantity));
            if (plannedByPartId.TryGetValue(partId, out var existing))
                existing.Quantity = Math.Max(existing.Quantity, qty);
            else
            {
                var planned = new JobCardPlannedPart
                {
                    Id = Guid.NewGuid(),
                    JobCardId = jobCardId,
                    PartId = partId,
                    Quantity = qty
                };
                _db.JobCardPlannedParts.Add(planned);
                plannedByPartId[partId] = planned;
            }
        }
    }

    private async Task<Quote?> LoadQuoteInScopeForUpdateAsync(Guid id, CancellationToken ct, bool includeLineItems = false)
    {
        IQueryable<Quote> query = _db.Quotes;
        if (includeLineItems)
            query = query.Include(q => q.LineItems);
        var quote = await query
            .Include(q => q.Company)
            .FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote == null) return null;

        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (!companyId.HasValue) return quote;

        var inScope = await _scopeGuard.CanAccessCompanyAsync(quote.CompanyId, quote.Company, ct);
        return inScope ? quote : null;
    }
}
