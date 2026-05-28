using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.DTOs.PurchaseOrders;
using Ike.Api.Helpers;
using Ike.Api.Models;
using Ike.Api.Services;

namespace Ike.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PurchaseOrdersController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ICurrentUserService _currentUser;
    private readonly IStatusTransitionService _statusTransitions;
    private readonly IScopeGuardService _scopeGuard;
    private const string ClientPOUploadFolder = "uploads/client-po";
    private static readonly string[] AllowedClientPOExtensions = { ".pdf", ".png", ".jpg", ".jpeg" };
    private const int MaxClientPOFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    public PurchaseOrdersController(ApplicationDbContext db, IWebHostEnvironment env, ICurrentUserService currentUser, IStatusTransitionService statusTransitions, IScopeGuardService scopeGuard)
    {
        _db = db;
        _env = env;
        _currentUser = currentUser;
        _statusTransitions = statusTransitions;
        _scopeGuard = scopeGuard;
    }

    [HttpGet]
    [Authorize(Policy = "RequireViewPurchaseOrders")]
    [ProducesResponseType(typeof(List<PurchaseOrderDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PurchaseOrderDto>>> List([FromQuery] Guid? clientId, [FromQuery] Guid? siteId, [FromQuery] string? status, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        IQueryable<PurchaseOrder> query = _db.PurchaseOrders.AsNoTracking()
            .Include(po => po.Company)
            .Include(po => po.Site);
        if (companyId.HasValue)
        {
            if (isClient)
                query = query.Where(po => po.CompanyId == companyId);
            else
                query = query.Where(po => po.Company != null && po.Company.ParentCompanyId == companyId);
        }
        if (clientId.HasValue)
            query = query.Where(po => po.CompanyId == clientId);
        if (siteId.HasValue)
            query = query.Where(po => po.SiteId == siteId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!PurchaseOrderStatus.IsValid(status))
                return BadRequest(ApiResponseBodies.Message($"Status must be one of: {string.Join(", ", PurchaseOrderStatus.All)}."));
            var normalized = PurchaseOrderStatus.Normalize(status);
            query = query.Where(po => po.Status == normalized);
        }

        var list = await query.OrderByDescending(po => po.CreatedAt)
            .Select(po => new PurchaseOrderDto
            {
                Id = po.Id,
                PONumber = po.PONumber,
                ClientPONumber = po.ClientPONumber,
                HasClientPOFile = !string.IsNullOrEmpty(po.ClientPOFilePath),
                ClientId = po.CompanyId,
                ClientName = po.Company != null ? po.Company.Name : null,
                SiteId = po.SiteId,
                SiteName = po.Site != null ? po.Site.Name : null,
                JobCardId = po.JobCardId,
                ServiceRequestId = po.ServiceRequestId,
                QuoteId = po.QuoteId,
                Amount = po.Amount,
                Currency = po.Currency,
                Status = po.Status,
                Notes = po.Notes,
                CreatedAt = po.CreatedAt
            }).ToListAsync();
        return Ok(list);
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PurchaseOrderDto>> UpdateStatus(Guid id, [FromBody] UpdatePurchaseOrderStatusRequest request, CancellationToken ct = default)
    {
        var po = await LoadPurchaseOrderInScopeForUpdateAsync(id, ct);
        if (po == null)
            return NotFound();
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!_statusTransitions.TryTransitionPurchaseOrder(po.Status, request.Status, out var nextStatus, out var transitionError))
                return BadRequest(ApiResponseBodies.Message(transitionError));
            po.Status = nextStatus;
        }
        await _db.SaveChangesAsync(ct);
        return await Get(id);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "RequireViewPurchaseOrders")]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PurchaseOrderDto>> Get(Guid id, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var po = await _db.PurchaseOrders.AsNoTracking()
            .Include(p => p.Company)
            .Include(p => p.Site)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (po == null)
            return NotFound();
        if (companyId.HasValue && po.Company != null &&
                (isClient ? po.CompanyId != companyId : po.Company.ParentCompanyId != companyId))
            return NotFound();
        return Ok(new PurchaseOrderDto
        {
            Id = po.Id,
            PONumber = po.PONumber,
            ClientPONumber = po.ClientPONumber,
            HasClientPOFile = !string.IsNullOrEmpty(po.ClientPOFilePath),
            ClientId = po.CompanyId,
            ClientName = po.Company?.Name,
            SiteId = po.SiteId,
            SiteName = po.Site?.Name,
            JobCardId = po.JobCardId,
            ServiceRequestId = po.ServiceRequestId,
            QuoteId = po.QuoteId,
            Amount = po.Amount,
            Currency = po.Currency,
            Status = po.Status,
            Notes = po.Notes,
            CreatedAt = po.CreatedAt
        });
    }

    [HttpPost]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PurchaseOrderDto>> Create([FromBody] CreatePurchaseOrderRequest request, CancellationToken ct)
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
        if (request.JobCardId.HasValue && !await _db.JobCards.AnyAsync(j => j.Id == request.JobCardId.Value, ct))
            return BadRequest(ApiResponseBodies.Message("Job card not found."));
        if (request.ServiceRequestId.HasValue && !await _db.ServiceRequests.AnyAsync(s => s.Id == request.ServiceRequestId.Value, ct))
            return BadRequest(ApiResponseBodies.Message("Service request not found."));
        if (request.QuoteId.HasValue && !await _db.Quotes.AnyAsync(q => q.Id == request.QuoteId.Value, ct))
            return BadRequest(ApiResponseBodies.Message("Quote not found."));
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

        var poNumber = NumberGenerator.NextPONumber(_db.PurchaseOrders);
        var po = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            PONumber = poNumber,
            ClientPONumber = string.IsNullOrWhiteSpace(request.ClientPONumber) ? null : request.ClientPONumber.Trim(),
            CompanyId = request.ClientId,
            SiteId = request.SiteId,
            JobCardId = request.JobCardId,
            ServiceRequestId = request.ServiceRequestId,
            QuoteId = request.QuoteId,
            Amount = request.Amount,
            Currency = request.Currency?.Trim() ?? "ZAR",
            Status = PurchaseOrderStatus.Draft,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow
        };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync(ct);

        var loaded = await _db.PurchaseOrders.AsNoTracking()
            .Include(p => p.Company)
            .Include(p => p.Site)
            .FirstAsync(p => p.Id == po.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = po.Id }, new PurchaseOrderDto
        {
            Id = loaded.Id,
            PONumber = loaded.PONumber,
            ClientPONumber = loaded.ClientPONumber,
            HasClientPOFile = !string.IsNullOrEmpty(loaded.ClientPOFilePath),
            ClientId = loaded.CompanyId,
            ClientName = loaded.Company?.Name,
            SiteId = loaded.SiteId,
            SiteName = loaded.Site?.Name,
            JobCardId = loaded.JobCardId,
            ServiceRequestId = loaded.ServiceRequestId,
            QuoteId = loaded.QuoteId,
            Amount = loaded.Amount,
            Currency = loaded.Currency,
            Status = loaded.Status,
            Notes = loaded.Notes,
            CreatedAt = loaded.CreatedAt
        });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PurchaseOrderDto>> Update(Guid id, [FromBody] UpdatePurchaseOrderRequest request, CancellationToken ct = default)
    {
        var po = await LoadPurchaseOrderInScopeForUpdateAsync(id, ct);
        if (po == null)
            return NotFound();
        po.ClientPONumber = string.IsNullOrWhiteSpace(request.ClientPONumber) ? null : request.ClientPONumber.Trim();
        po.Amount = request.Amount;
        po.Currency = request.Currency?.Trim() ?? "ZAR";
        po.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        await _db.SaveChangesAsync(ct);
        return await Get(id);
    }

    [HttpPost("{id:guid}/client-po-upload")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadClientPO(Guid id, IFormFile file, CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponseBodies.Message("No file provided."));
        if (file.Length > MaxClientPOFileSizeBytes)
            return BadRequest(ApiResponseBodies.Message("File too large (max 10 MB)."));
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !AllowedClientPOExtensions.Contains(ext))
            return BadRequest(ApiResponseBodies.Message("Allowed types: PDF, PNG, JPG."));
        var sigErr = await FileContentSignatureHelper.ValidateContentMatchesExtensionAsync(file, ext, ct);
        if (sigErr != null)
            return BadRequest(ApiResponseBodies.Message(sigErr));

        var po = await LoadPurchaseOrderInScopeForUpdateAsync(id, ct);
        if (po == null)
            return NotFound();

        var dir = Path.Combine(_env.ContentRootPath, ClientPOUploadFolder);
        Directory.CreateDirectory(dir);
        var fileName = id.ToString("N") + ext;
        var relativePath = ClientPOUploadFolder + "/" + fileName;
        var fullPath = Path.Combine(dir, fileName);
        await using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(stream, ct);
        }
        po.ClientPOFilePath = relativePath;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<PurchaseOrder?> LoadPurchaseOrderInScopeForUpdateAsync(Guid id, CancellationToken ct)
    {
        var po = await _db.PurchaseOrders
            .Include(p => p.Company)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (po == null) return null;

        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (!companyId.HasValue) return po;

        var inScope = await _scopeGuard.CanAccessCompanyAsync(po.CompanyId, po.Company, ct);
        return inScope ? po : null;
    }
}
