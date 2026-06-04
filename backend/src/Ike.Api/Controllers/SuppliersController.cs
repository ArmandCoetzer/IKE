using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;
using Ike.Api.Data;
using Ike.Api.Models;
using Ike.Api.Services;

namespace Ike.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SuppliersController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SuppliersController(ApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    [Authorize(Policy = "RequireViewPurchaseOrders")]
    [ProducesResponseType(typeof(List<SupplierDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SupplierDto>>> List(CancellationToken ct = default)
    {
        var (companyId, _) = await _currentUser.GetClientScopeAsync(ct);
        if (!companyId.HasValue)
            return Ok(new List<SupplierDto>());

        var list = await _db.Suppliers
            .AsNoTracking()
            .Include(s => s.Parts)
            .ThenInclude(ps => ps.Part)
            .Where(s => s.CompanyId == companyId.Value)
            .OrderBy(s => s.Name)
            .Select(s => new SupplierDto
            {
                Id = s.Id,
                Name = s.Name,
                Email = s.Email,
                WebsiteUrl = s.WebsiteUrl,
                Phone = s.Phone,
                ContactPerson = s.ContactPerson,
                PartIds = s.Parts.Select(ps => ps.PartId).ToList(),
                PartNames = s.Parts.Where(ps => ps.Part != null).Select(ps => ps.Part.Name).ToList(),
                Performance = new SupplierPerformanceDto()
            })
            .ToListAsync(ct);

        var supplierIds = list.Select(x => x.Id).ToList();
        if (supplierIds.Count > 0)
        {
            var requestRows = await _db.SupplierQuoteRequests
                .AsNoTracking()
                .Where(r => supplierIds.Contains(r.SupplierId))
                .Select(r => new
                {
                    r.SupplierId,
                    Row = new SupplierQuoteRequestPerformanceRow
                    {
                        Status = r.Status,
                        CreatedAt = r.CreatedAt,
                        UpdatedAt = r.UpdatedAt
                    }
                })
                .ToListAsync(ct);

            var bySupplier = requestRows
                .GroupBy(r => r.SupplierId)
                .ToDictionary(g => g.Key, g => BuildPerformance(g.Select(x => x.Row)));

            foreach (var supplier in list)
            {
                if (bySupplier.TryGetValue(supplier.Id, out var perf))
                    supplier.Performance = perf;
            }
        }
        return Ok(list);
    }

    [HttpPost]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SupplierDto>> Create([FromBody] CreateSupplierRequest request, CancellationToken ct = default)
    {
        var (companyId, _) = await _currentUser.GetClientScopeAsync(ct);
        if (!companyId.HasValue)
            return BadRequest(new { message = "Suppliers can only be created by users with a company profile." });
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Supplier name is required." });
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { message = "Supplier email is required so stock requests can be emailed." });

        var normalizedName = request.Name.Trim();
        var normalizedEmail = request.Email.Trim();
        var normalizedWebsite = NormalizeWebsiteUrlOrNull(request.WebsiteUrl);
        if (!IsValidEmail(normalizedEmail))
            return BadRequest(new { message = "Supplier email is not valid." });
        if (normalizedWebsite == string.Empty)
            return BadRequest(new { message = "Supplier website URL is invalid." });

        var exists = await _db.Suppliers.AnyAsync(s =>
            s.CompanyId == companyId.Value &&
            s.Name.ToLower() == normalizedName.ToLower(), ct);
        if (exists)
            return BadRequest(new { message = "A supplier with this name already exists." });
        var emailExists = await _db.Suppliers.AnyAsync(s =>
            s.CompanyId == companyId.Value &&
            s.Email != null &&
            s.Email.ToLower() == normalizedEmail.ToLower(), ct);
        if (emailExists)
            return BadRequest(new { message = "A supplier with this email already exists." });
        var partValidationError = await ValidateSupplierPartIdsAsync(request.PartIds, companyId.Value, ct);
        if (partValidationError != null)
            return BadRequest(new { message = partValidationError });

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId.Value,
            Name = normalizedName,
            Email = normalizedEmail,
            WebsiteUrl = normalizedWebsite,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            ContactPerson = string.IsNullOrWhiteSpace(request.ContactPerson) ? null : request.ContactPerson.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        _db.Suppliers.Add(supplier);
        await SyncSupplierPartLinksAsync(supplier.Id, request.PartIds, companyId.Value, ct);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(List), new { id = supplier.Id }, new SupplierDto
        {
            Id = supplier.Id,
            Name = supplier.Name,
            Email = supplier.Email,
            WebsiteUrl = supplier.WebsiteUrl,
            Phone = supplier.Phone,
            ContactPerson = supplier.ContactPerson,
            PartIds = request.PartIds?.Distinct().ToList() ?? new List<Guid>(),
            PartNames = await GetSupplierPartNamesAsync(supplier.Id, ct),
            Performance = new SupplierPerformanceDto()
        });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupplierDto>> Update(Guid id, [FromBody] UpdateSupplierRequest request, CancellationToken ct = default)
    {
        var (companyId, _) = await _currentUser.GetClientScopeAsync(ct);
        if (!companyId.HasValue)
            return NotFound();

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId.Value, ct);
        if (supplier == null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Supplier name is required." });
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { message = "Supplier email is required so stock requests can be emailed." });

        var normalizedName = request.Name.Trim();
        var normalizedEmail = request.Email.Trim();
        var normalizedWebsite = NormalizeWebsiteUrlOrNull(request.WebsiteUrl);
        if (!IsValidEmail(normalizedEmail))
            return BadRequest(new { message = "Supplier email is not valid." });
        if (normalizedWebsite == string.Empty)
            return BadRequest(new { message = "Supplier website URL is invalid." });

        var duplicate = await _db.Suppliers.AnyAsync(s =>
            s.Id != id &&
            s.CompanyId == companyId.Value &&
            s.Name.ToLower() == normalizedName.ToLower(), ct);
        if (duplicate)
            return BadRequest(new { message = "A supplier with this name already exists." });
        var emailDuplicate = await _db.Suppliers.AnyAsync(s =>
            s.Id != id &&
            s.CompanyId == companyId.Value &&
            s.Email != null &&
            s.Email.ToLower() == normalizedEmail.ToLower(), ct);
        if (emailDuplicate)
            return BadRequest(new { message = "A supplier with this email already exists." });
        var partValidationError = await ValidateSupplierPartIdsAsync(request.PartIds, companyId.Value, ct);
        if (partValidationError != null)
            return BadRequest(new { message = partValidationError });

        supplier.Name = normalizedName;
        supplier.Email = normalizedEmail;
        supplier.WebsiteUrl = normalizedWebsite;
        supplier.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        supplier.ContactPerson = string.IsNullOrWhiteSpace(request.ContactPerson) ? null : request.ContactPerson.Trim();
        if (request.PartIds != null)
            await SyncSupplierPartLinksAsync(supplier.Id, request.PartIds, companyId.Value, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new SupplierDto
        {
            Id = supplier.Id,
            Name = supplier.Name,
            Email = supplier.Email,
            WebsiteUrl = supplier.WebsiteUrl,
            Phone = supplier.Phone,
            ContactPerson = supplier.ContactPerson,
            PartIds = request.PartIds?.Distinct().ToList() ?? await GetSupplierPartIdsAsync(supplier.Id, ct),
            PartNames = await GetSupplierPartNamesAsync(supplier.Id, ct),
            Performance = new SupplierPerformanceDto()
        });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var (companyId, _) = await _currentUser.GetClientScopeAsync(ct);
        if (!companyId.HasValue)
            return NotFound();

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId.Value, ct);
        if (supplier == null)
            return NotFound();

        var lockedRequests = await _db.SupplierQuoteRequests.AsNoTracking()
            .AnyAsync(r => r.SupplierId == id
                && r.Status != SupplierQuoteRequestStatus.Requested
                && r.Status != SupplierQuoteRequestStatus.Cancelled, ct);
        if (lockedRequests)
            return BadRequest(new { message = "Cannot delete supplier because it has quoted or ordered supplier quote requests." });

        var parts = await _db.Parts.Where(p => p.SupplierId == id).ToListAsync(ct);
        foreach (var part in parts)
            part.SupplierId = null;

        var links = await _db.PartSuppliers.Where(ps => ps.SupplierId == id).ToListAsync(ct);
        _db.PartSuppliers.RemoveRange(links);

        var safeRequests = await _db.SupplierQuoteRequests
            .Where(r => r.SupplierId == id
                && (r.Status == SupplierQuoteRequestStatus.Requested || r.Status == SupplierQuoteRequestStatus.Cancelled))
            .ToListAsync(ct);
        _db.SupplierQuoteRequests.RemoveRange(safeRequests);

        _db.Suppliers.Remove(supplier);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task SyncSupplierPartLinksAsync(Guid supplierId, List<Guid>? requestedPartIds, Guid companyId, CancellationToken ct)
    {
        var requestedIds = (requestedPartIds ?? new List<Guid>()).Distinct().ToList();

        var existingLinks = await _db.PartSuppliers
            .Where(ps => ps.SupplierId == supplierId)
            .ToListAsync(ct);
        var existingPartIds = existingLinks.Select(ps => ps.PartId).ToHashSet();
        var requestedPartIdSet = requestedIds.ToHashSet();
        var removedPartIds = existingPartIds.Except(requestedPartIdSet).ToList();

        _db.PartSuppliers.RemoveRange(existingLinks.Where(ps => !requestedPartIdSet.Contains(ps.PartId)));
        foreach (var partId in requestedPartIds?.Distinct().Where(id => !existingPartIds.Contains(id)) ?? Enumerable.Empty<Guid>())
        {
            _db.PartSuppliers.Add(new PartSupplier
            {
                PartId = partId,
                SupplierId = supplierId,
                LinkedAt = DateTime.UtcNow
            });
        }

        var affectedPartIds = requestedPartIdSet.Concat(removedPartIds).Distinct().ToList();
        if (affectedPartIds.Count == 0)
            return;

        var affectedParts = await _db.Parts
            .Where(p => affectedPartIds.Contains(p.Id))
            .ToListAsync(ct);

        foreach (var part in affectedParts)
        {
            if (requestedPartIdSet.Contains(part.Id) && !part.SupplierId.HasValue)
            {
                part.SupplierId = supplierId;
                continue;
            }

            if (removedPartIds.Contains(part.Id) && part.SupplierId == supplierId)
            {
                var replacementSupplierId = await _db.PartSuppliers
                    .Where(ps => ps.PartId == part.Id && ps.SupplierId != supplierId)
                    .Select(ps => (Guid?)ps.SupplierId)
                    .FirstOrDefaultAsync(ct);
                part.SupplierId = replacementSupplierId;
            }
        }
    }

    private async Task<string?> ValidateSupplierPartIdsAsync(List<Guid>? requestedPartIds, Guid companyId, CancellationToken ct)
    {
        var requestedIds = (requestedPartIds ?? new List<Guid>()).Distinct().ToList();
        if (requestedIds.Count == 0)
            return null;

        var validIds = await _db.Parts.AsNoTracking()
            .Where(p => requestedIds.Contains(p.Id) && p.CompanyId == companyId && !p.IsLabour)
            .Select(p => p.Id)
            .ToListAsync(ct);
        return validIds.Count == requestedIds.Count
            ? null
            : "One or more selected parts were not found in this supplier's company scope.";
    }

    private async Task<List<string>> GetSupplierPartNamesAsync(Guid supplierId, CancellationToken ct)
    {
        return await _db.PartSuppliers.AsNoTracking()
            .Where(ps => ps.SupplierId == supplierId && ps.Part != null)
            .Select(ps => ps.Part.Name)
            .OrderBy(name => name)
            .ToListAsync(ct);
    }

    private async Task<List<Guid>> GetSupplierPartIdsAsync(Guid supplierId, CancellationToken ct)
    {
        return await _db.PartSuppliers.AsNoTracking()
            .Where(ps => ps.SupplierId == supplierId)
            .Select(ps => ps.PartId)
            .ToListAsync(ct);
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new MailAddress(email);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns null for empty, normalized absolute URL for valid, and empty-string sentinel when invalid.</summary>
    private static string? NormalizeWebsiteUrlOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var raw = value.Trim();
        if (!raw.Contains("://", StringComparison.Ordinal))
            raw = "https://" + raw;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return string.Empty;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return string.Empty;
        return uri.ToString();
    }

    private sealed class SupplierQuoteRequestPerformanceRow
    {
        public string Status { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
        public DateTime? UpdatedAt { get; init; }
    }

    private static SupplierPerformanceDto BuildPerformance(IEnumerable<SupplierQuoteRequestPerformanceRow> rows)
    {
        var list = rows.ToList();
        var total = list.Count;
        var quoted = list.Count(r => string.Equals(r.Status, SupplierQuoteRequestStatus.Quoted, StringComparison.OrdinalIgnoreCase));
        var ordered = list.Count(r => string.Equals(r.Status, SupplierQuoteRequestStatus.Ordered, StringComparison.OrdinalIgnoreCase));
        var cancelled = list.Count(r => string.Equals(r.Status, SupplierQuoteRequestStatus.Cancelled, StringComparison.OrdinalIgnoreCase));
        var conversionBase = Math.Max(1, quoted + ordered);
        var quoteWinRate = total == 0 ? 0d : (double)ordered / conversionBase;

        var responded = list
            .Where(r =>
                (string.Equals(r.Status, SupplierQuoteRequestStatus.Quoted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.Status, SupplierQuoteRequestStatus.Ordered, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.Status, SupplierQuoteRequestStatus.Cancelled, StringComparison.OrdinalIgnoreCase))
                && r.UpdatedAt.HasValue)
            .ToList();

        var responseHours = responded
            .Select(r => Math.Max(0d, (r.UpdatedAt!.Value - r.CreatedAt).TotalHours))
            .ToList();
        var avgResponseHours = responseHours.Count == 0 ? 0d : responseHours.Average();
        var onTimeRate = responseHours.Count == 0 ? 0d : responseHours.Count(h => h <= 48d) / (double)responseHours.Count;

        var score = (quoteWinRate * 55d) + (onTimeRate * 35d) + ((1d - Math.Min(1d, cancelled / (double)Math.Max(1, total))) * 10d);
        score = Math.Max(0d, Math.Min(100d, score));

        return new SupplierPerformanceDto
        {
            Score = Math.Round(score, 2),
            TotalRequests = total,
            QuotedCount = quoted,
            OrderedCount = ordered,
            CancelledCount = cancelled,
            QuoteWinRate = Math.Round(quoteWinRate, 4),
            OnTimeResponseRate = Math.Round(onTimeRate, 4),
            AverageResponseHours = Math.Round(avgResponseHours, 2)
        };
    }
}

public class SupplierDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? Phone { get; set; }
    public string? ContactPerson { get; set; }
    public List<Guid> PartIds { get; set; } = new();
    public List<string> PartNames { get; set; } = new();
    public SupplierPerformanceDto Performance { get; set; } = new();
}

public class SupplierPerformanceDto
{
    public double Score { get; set; }
    public int TotalRequests { get; set; }
    public int QuotedCount { get; set; }
    public int OrderedCount { get; set; }
    public int CancelledCount { get; set; }
    public double QuoteWinRate { get; set; }
    public double OnTimeResponseRate { get; set; }
    public double AverageResponseHours { get; set; }
}

public class CreateSupplierRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? Phone { get; set; }
    public string? ContactPerson { get; set; }
    public List<Guid>? PartIds { get; set; }
}

public class UpdateSupplierRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? Phone { get; set; }
    public string? ContactPerson { get; set; }
    public List<Guid>? PartIds { get; set; }
}
