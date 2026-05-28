using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.Models;
using Ike.Api.Services;

namespace Ike.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SupplierQuoteRequestsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly INotificationService _notification;
    private readonly IEmailService _emailService;
    private readonly IStatusTransitionService _statusTransitions;

    public SupplierQuoteRequestsController(ApplicationDbContext db, ICurrentUserService currentUser, INotificationService notification, IEmailService emailService, IStatusTransitionService statusTransitions)
    {
        _db = db;
        _currentUser = currentUser;
        _notification = notification;
        _emailService = emailService;
        _statusTransitions = statusTransitions;
    }

    [HttpGet]
    [Authorize(Policy = "RequireViewPurchaseOrders")]
    public async Task<ActionResult<List<SupplierQuoteRequestDto>>> List([FromQuery] string? status, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        IQueryable<SupplierQuoteRequest> query = _db.SupplierQuoteRequests.AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.Part)
            .Include(x => x.JobCard).ThenInclude(j => j!.Site).ThenInclude(s => s!.Company);

        if (companyId.HasValue)
        {
            query = query.Where(x =>
                x.Supplier != null &&
                x.Supplier.CompanyId.HasValue &&
                x.Part != null &&
                x.Part.CompanyId.HasValue &&
                x.Supplier.CompanyId == x.Part.CompanyId &&
                (isClient ? x.Part.CompanyId == companyId : x.Part.Company != null && x.Part.Company.ParentCompanyId == companyId));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!SupplierQuoteRequestStatus.IsValid(status))
                return BadRequest(new { message = $"Status must be one of: {string.Join(", ", SupplierQuoteRequestStatus.All)}." });
            var normalizedStatus = SupplierQuoteRequestStatus.Normalize(status);
            query = query.Where(x => x.Status == normalizedStatus);
        }

        var list = await query.OrderByDescending(x => x.CreatedAt).Select(x => new SupplierQuoteRequestDto
        {
            Id = x.Id,
            SupplierId = x.SupplierId,
            SupplierName = x.Supplier.Name,
            PartId = x.PartId,
            PartName = x.Part != null ? x.Part.Name : null,
            JobCardId = x.JobCardId,
            JobCardNumber = x.JobCard != null ? x.JobCard.JobCardNumber : null,
            RequestedQuantity = x.RequestedQuantity,
            Status = x.Status,
            Notes = x.Notes,
            CreatedAt = x.CreatedAt
        }).ToListAsync(ct);

        return Ok(list);
    }

    [HttpPost]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    public async Task<ActionResult<SupplierQuoteRequestDto>> Create([FromBody] CreateSupplierQuoteRequest request, CancellationToken ct = default)
    {
        if (request.SupplierId == Guid.Empty)
            return BadRequest(new { message = "Supplier is required." });
        if (request.PartId == Guid.Empty)
            return BadRequest(new { message = "Part is required." });

        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var supplier = await _db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SupplierId, ct);
        if (supplier == null) return BadRequest(new { message = "Supplier not found." });
        if (string.IsNullOrWhiteSpace(supplier.Email))
            return BadRequest(new { message = "This supplier has no email address. Add supplier email before requesting stock." });
        var part = await _db.Parts.AsNoTracking()
            .Include(p => p.Company)
            .Include(p => p.Suppliers)
            .FirstOrDefaultAsync(p => p.Id == request.PartId, ct);
        if (part == null) return BadRequest(new { message = "Part not found." });
        if (!PartLinkedToSupplier(part, supplier.Id))
            return BadRequest(new { message = "This supplier is not linked to the selected part." });
        if (companyId.HasValue)
        {
            if (supplier.CompanyId != companyId.Value)
                return NotFound();

            var inPartScope = isClient
                ? part.CompanyId == companyId
                : part.CompanyId.HasValue && (part.CompanyId == companyId || (part.Company != null && part.Company.ParentCompanyId == companyId));
            if (!inPartScope)
                return NotFound();
            if (!part.CompanyId.HasValue || part.CompanyId.Value != supplier.CompanyId)
                return BadRequest(new { message = "Supplier and part must belong to the same company profile." });
        }
        if (request.JobCardId.HasValue)
        {
            var job = await _db.JobCards.AsNoTracking()
                .Include(j => j.Site).ThenInclude(s => s!.Company)
                .FirstOrDefaultAsync(j => j.Id == request.JobCardId.Value, ct);
            if (job == null) return BadRequest(new { message = "Job card not found." });
            if (companyId.HasValue)
            {
                var inJobScope = isClient
                    ? job.Site != null && job.Site.CompanyId == companyId
                    : job.Site != null && (job.Site.CompanyId == companyId || (job.Site.Company != null && job.Site.Company.ParentCompanyId == companyId));
                if (!inJobScope)
                    return NotFound();
            }
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var entity = new SupplierQuoteRequest
        {
            Id = Guid.NewGuid(),
            SupplierId = request.SupplierId,
            PartId = request.PartId,
            JobCardId = request.JobCardId,
            RequestedQuantity = request.RequestedQuantity,
            Status = SupplierQuoteRequestStatus.Requested,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow
        };
        _db.SupplierQuoteRequests.Add(entity);
        await _db.SaveChangesAsync(ct);

        await _notification.NotifyUsersWithPermissionAsync(
            "ViewPurchaseOrders",
            "Supplier quote requested",
            $"Quote request created for part {part.Name} from supplier {supplier.Name}.",
            "SupplierQuoteRequest",
            entity.Id.ToString(),
            excludeUserId: userId,
            scopeCompanyId: companyId,
            ct: ct
        );
        await _emailService.SendSupplierStockRequestAsync(entity.Id, toEmail: supplier.Email, ct: ct);

        return Ok(new SupplierQuoteRequestDto
        {
            Id = entity.Id,
            SupplierId = supplier.Id,
            SupplierName = supplier.Name,
            PartId = part.Id,
            PartName = part.Name,
            JobCardId = entity.JobCardId,
            RequestedQuantity = entity.RequestedQuantity,
            Status = entity.Status,
            Notes = entity.Notes,
            CreatedAt = entity.CreatedAt
        });
    }

    [HttpPost("draft")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    public async Task<ActionResult<SupplierQuoteEmailDraftDto>> BuildDraft([FromBody] BuildSupplierQuoteDraftRequest request, CancellationToken ct = default)
    {
        if (request.SupplierId == Guid.Empty)
            return BadRequest(new { message = "Supplier is required." });
        if (request.Items == null || request.Items.Count == 0)
            return BadRequest(new { message = "At least one part is required." });

        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var supplier = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SupplierId, ct);
        if (supplier == null) return BadRequest(new { message = "Supplier not found." });
        if (string.IsNullOrWhiteSpace(supplier.Email))
            return BadRequest(new { message = "This supplier has no email address. Add supplier email before requesting stock." });

        var requestedPartIds = request.Items
            .Where(i => i.PartId != Guid.Empty)
            .Select(i => i.PartId)
            .Distinct()
            .ToList();
        if (requestedPartIds.Count == 0)
            return BadRequest(new { message = "At least one valid part is required." });

        var parts = await _db.Parts.AsNoTracking()
            .Include(p => p.Suppliers)
            .Include(p => p.Company)
            .Where(p => requestedPartIds.Contains(p.Id))
            .ToListAsync(ct);
        if (parts.Count == 0) return BadRequest(new { message = "Part not found." });

        if (companyId.HasValue)
        {
            if (supplier.CompanyId != companyId.Value)
                return NotFound();
        }
        var requestedByPartId = request.Items
            .Where(i => i.PartId != Guid.Empty)
            .GroupBy(i => i.PartId)
            .ToDictionary(g => g.Key, g => g.Last().RequestedQuantity.GetValueOrDefault());
        var draftItems = new List<SupplierQuoteEmailDraftItemDto>();
        foreach (var part in parts)
        {
            if (part.IsLabour) continue;
            if (companyId.HasValue)
            {
                var inPartScope = isClient
                    ? part.CompanyId == companyId
                    : part.CompanyId.HasValue && (part.CompanyId == companyId || (part.Company != null && part.Company.ParentCompanyId == companyId));
                if (!inPartScope || !part.CompanyId.HasValue || part.CompanyId.Value != supplier.CompanyId)
                    continue;
            }
            if (!PartLinkedToSupplier(part, supplier.Id))
                continue;

            var requestedQty = requestedByPartId.TryGetValue(part.Id, out var qty) ? qty : 0;
            if (requestedQty <= 0)
            {
                var needed = Math.Max(1, part.ReorderLevel - part.Quantity);
                requestedQty = needed;
            }
            var unit = string.IsNullOrWhiteSpace(part.Unit)
                ? "unit/s"
                : (part.Unit.Trim().Equals("Each", StringComparison.OrdinalIgnoreCase) ? "unit/s" : part.Unit.Trim());
            draftItems.Add(new SupplierQuoteEmailDraftItemDto
            {
                PartId = part.Id,
                PartName = part.Name,
                Unit = unit,
                RequiredQuantity = requestedQty,
                ReorderLevel = part.ReorderLevel,
                StockQuantity = part.Quantity,
                RequestedQuantity = requestedQty
            });
        }
        if (draftItems.Count == 0)
            return BadRequest(new { message = "No supplier-linked parts found for the selected items." });

        var greetingName = string.IsNullOrWhiteSpace(supplier.ContactPerson) ? supplier.Name : supplier.ContactPerson.Trim();
        var draft = new SupplierQuoteEmailDraftDto
        {
            SupplierId = supplier.Id,
            SupplierName = supplier.Name,
            ContactPerson = supplier.ContactPerson,
            ToEmail = supplier.Email ?? "",
            Subject = $"Stock request: {draftItems.Count} item{(draftItems.Count == 1 ? "" : "s")}",
            Body = BuildDraftEmailBody(greetingName, null, draftItems, request.Notes),
            Items = draftItems.OrderBy(i => i.PartName).ToList()
        };

        return Ok(draft);
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    public async Task<ActionResult> UpdateStatus(Guid id, [FromBody] UpdateSupplierQuoteRequestStatus request, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var item = await _db.SupplierQuoteRequests
            .Include(x => x.Part).ThenInclude(p => p!.Company)
            .Include(x => x.Supplier)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item == null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Status)) return BadRequest(new { message = "Status is required." });
        if (!_statusTransitions.TryTransitionSupplierQuoteRequest(item.Status, request.Status, out var nextStatus, out var transitionError))
            return BadRequest(new { message = transitionError });
        if (companyId.HasValue)
        {
            var part = item.Part;
            if (part == null) return NotFound();
            var supplier = item.Supplier;
            if (supplier == null) return NotFound();
            var inScope = isClient
                ? part.CompanyId == companyId
                : part.CompanyId.HasValue && (part.CompanyId == companyId || (part.Company != null && part.Company.ParentCompanyId == companyId));
            if (!inScope) return NotFound();
            if (supplier.CompanyId != part.CompanyId) return NotFound();
        }

        item.Status = nextStatus;
        item.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("part-email-drafts")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    public async Task<ActionResult<SupplierQuoteEmailDraftsResponse>> BuildPartEmailDrafts([FromBody] BuildSupplierQuotePartDraftsRequest request, CancellationToken ct = default)
    {
        if (request.Items == null || request.Items.Count == 0)
            return BadRequest(new { message = "At least one part is required." });

        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var requestedPartIds = request.Items
            .Where(i => i.PartId != Guid.Empty)
            .Select(i => i.PartId)
            .Distinct()
            .ToList();
        if (requestedPartIds.Count == 0)
            return BadRequest(new { message = "At least one valid part is required." });

        var requestedByPartId = request.Items
            .Where(i => i.PartId != Guid.Empty)
            .GroupBy(i => i.PartId)
            .ToDictionary(g => g.Key, g => g.Last().RequestedQuantity.GetValueOrDefault());

        var parts = await _db.Parts.AsNoTracking()
            .Include(p => p.Company)
            .Include(p => p.Supplier)
            .Include(p => p.Suppliers).ThenInclude(ps => ps.Supplier)
            .Where(p => requestedPartIds.Contains(p.Id))
            .ToListAsync(ct);
        if (parts.Count == 0)
            return BadRequest(new { message = "No valid parts found for this request." });

        var supplierGroups = new Dictionary<Guid, SupplierQuoteEmailDraftDto>();
        foreach (var part in parts)
        {
            if (part.IsLabour) continue;
            if (companyId.HasValue)
            {
                var inPartScope = isClient
                    ? part.CompanyId == companyId
                    : part.CompanyId.HasValue && (part.CompanyId == companyId || (part.Company != null && part.Company.ParentCompanyId == companyId));
                if (!inPartScope)
                    continue;
            }

            var linkedSupplierIds = new HashSet<Guid>();
            if (part.SupplierId.HasValue) linkedSupplierIds.Add(part.SupplierId.Value);
            foreach (var ps in part.Suppliers)
                linkedSupplierIds.Add(ps.SupplierId);

            foreach (var supplierId in linkedSupplierIds)
            {
                var supplier = part.Suppliers.FirstOrDefault(ps => ps.SupplierId == supplierId)?.Supplier;
                if (supplier == null && part.SupplierId == supplierId)
                    supplier = part.Supplier;
                if (supplier == null) continue;
                if (part.CompanyId.HasValue && supplier.CompanyId != part.CompanyId.Value) continue;

                if (!supplierGroups.TryGetValue(supplierId, out var draft))
                {
                    draft = new SupplierQuoteEmailDraftDto
                    {
                        SupplierId = supplier.Id,
                        SupplierName = supplier.Name,
                        ContactPerson = supplier.ContactPerson,
                        ToEmail = supplier.Email ?? "",
                        Subject = "Stock request",
                        Body = "",
                        Items = new List<SupplierQuoteEmailDraftItemDto>()
                    };
                    supplierGroups[supplierId] = draft;
                }

                var requestedQty = requestedByPartId.TryGetValue(part.Id, out var qty) ? qty : 0;
                if (requestedQty <= 0)
                    requestedQty = Math.Max(1, part.ReorderLevel);
                draft.Items.Add(new SupplierQuoteEmailDraftItemDto
                {
                    PartId = part.Id,
                    PartName = part.Name,
                    Unit = string.IsNullOrWhiteSpace(part.Unit)
                        ? "unit/s"
                        : (part.Unit.Trim().Equals("Each", StringComparison.OrdinalIgnoreCase) ? "unit/s" : part.Unit.Trim()),
                    RequiredQuantity = requestedQty,
                    ReorderLevel = part.ReorderLevel,
                    RequestedQuantity = requestedQty,
                    StockQuantity = part.Quantity
                });
            }
        }

        foreach (var draft in supplierGroups.Values)
        {
            draft.Items = draft.Items.OrderBy(i => i.PartName).ToList();
            draft.Subject = $"Stock request: {draft.Items.Count} item{(draft.Items.Count == 1 ? "" : "s")}";
            var greetingName = string.IsNullOrWhiteSpace(draft.ContactPerson) ? draft.SupplierName : draft.ContactPerson!;
            draft.Body = BuildDraftEmailBody(greetingName, null, draft.Items, null);
        }

        return Ok(new SupplierQuoteEmailDraftsResponse
        {
            Drafts = supplierGroups.Values.OrderBy(d => d.SupplierName).ToList()
        });
    }

    [HttpPost("job-cards/{jobCardId:guid}/email-drafts")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    public async Task<ActionResult<SupplierQuoteEmailDraftsResponse>> BuildJobCardEmailDrafts(Guid jobCardId, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var job = await _db.JobCards.AsNoTracking()
            .Include(j => j.Site).ThenInclude(s => s!.Company)
            .FirstOrDefaultAsync(j => j.Id == jobCardId, ct);
        if (job?.Site == null)
            return NotFound();

        if (companyId.HasValue)
        {
            var inScope = isClient
                ? job.Site.CompanyId == companyId
                : (job.Site.CompanyId == companyId || (job.Site.Company != null && job.Site.Company.ParentCompanyId == companyId));
            if (!inScope)
                return NotFound();
        }

        var planned = await _db.JobCardPlannedParts.AsNoTracking()
            .Include(pp => pp.Part).ThenInclude(p => p!.Suppliers).ThenInclude(ps => ps.Supplier)
            .Include(pp => pp.Part).ThenInclude(p => p!.Supplier)
            .Where(pp => pp.JobCardId == jobCardId)
            .ToListAsync(ct);
        if (planned.Count == 0)
            return Ok(new SupplierQuoteEmailDraftsResponse { JobCardId = jobCardId, Drafts = new List<SupplierQuoteEmailDraftDto>() });

        var supplierGroups = new Dictionary<Guid, SupplierQuoteEmailDraftDto>();
        foreach (var pp in planned)
        {
            var part = pp.Part;
            if (part == null || part.IsLabour) continue;

            var linkedSupplierIds = new HashSet<Guid>();
            if (part.SupplierId.HasValue) linkedSupplierIds.Add(part.SupplierId.Value);
            foreach (var ps in part.Suppliers)
                linkedSupplierIds.Add(ps.SupplierId);
            if (linkedSupplierIds.Count == 0) continue;

            foreach (var supplierId in linkedSupplierIds)
            {
                var supplier = part.Suppliers.FirstOrDefault(ps => ps.SupplierId == supplierId)?.Supplier;
                if (supplier == null && part.SupplierId == supplierId)
                    supplier = part.Supplier;
                if (supplier == null) continue;

                if (!supplierGroups.TryGetValue(supplierId, out var draft))
                {
                    draft = new SupplierQuoteEmailDraftDto
                    {
                        SupplierId = supplier.Id,
                        SupplierName = supplier.Name,
                        ContactPerson = supplier.ContactPerson,
                        ToEmail = supplier.Email ?? "",
                        Subject = $"Stock request for Job Card {job.JobCardNumber ?? job.Id.ToString()}",
                        Body = "",
                        Items = new List<SupplierQuoteEmailDraftItemDto>()
                    };
                    supplierGroups[supplierId] = draft;
                }

                var existingItem = draft.Items.FirstOrDefault(i => i.PartId == part.Id);
                var requiredQty = Math.Max(1, pp.Quantity);
                var requestQty = CalculateRequestedQuantity(requiredQty, part.ReorderLevel);
                if (existingItem == null)
                {
                    draft.Items.Add(new SupplierQuoteEmailDraftItemDto
                    {
                        PartId = part.Id,
                        PartName = part.Name,
                        Unit = string.IsNullOrWhiteSpace(part.Unit)
                            ? "unit/s"
                            : (part.Unit.Trim().Equals("Each", StringComparison.OrdinalIgnoreCase) ? "unit/s" : part.Unit.Trim()),
                        RequiredQuantity = requiredQty,
                        ReorderLevel = part.ReorderLevel,
                        RequestedQuantity = requestQty,
                        StockQuantity = part.Quantity
                    });
                }
                else
                {
                    existingItem.RequiredQuantity = Math.Max(existingItem.RequiredQuantity, requiredQty);
                    existingItem.ReorderLevel = Math.Max(existingItem.ReorderLevel, part.ReorderLevel);
                    existingItem.StockQuantity = Math.Max(existingItem.StockQuantity, part.Quantity);
                    existingItem.RequestedQuantity = Math.Max(existingItem.RequestedQuantity, requestQty);
                    if (string.IsNullOrWhiteSpace(existingItem.Unit) && !string.IsNullOrWhiteSpace(part.Unit))
                        existingItem.Unit = part.Unit.Trim();
                }
            }
        }

        foreach (var draft in supplierGroups.Values)
        {
            draft.Items = draft.Items.OrderBy(i => i.PartName).ToList();
            var greetingName = string.IsNullOrWhiteSpace(draft.ContactPerson) ? draft.SupplierName : draft.ContactPerson!;
            draft.Body = BuildDraftEmailBody(greetingName, job, draft.Items, null);
        }

        return Ok(new SupplierQuoteEmailDraftsResponse
        {
            JobCardId = jobCardId,
            Drafts = supplierGroups.Values.OrderBy(d => d.SupplierName).ToList()
        });
    }

    [HttpPost("send-draft")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    public async Task<ActionResult<SupplierQuoteDraftSendResultDto>> SendDraft([FromBody] SendSupplierQuoteDraftRequest request, CancellationToken ct = default)
    {
        if (request.SupplierId == Guid.Empty)
            return BadRequest(new { message = "SupplierId is required." });
        if (request.Items == null || request.Items.Count == 0)
            return BadRequest(new { message = "At least one item is required." });
        if (string.IsNullOrWhiteSpace(request.ToEmail))
            return BadRequest(new { message = "Supplier email is required." });
        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body))
            return BadRequest(new { message = "Subject and body are required." });

        var supplier = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SupplierId, ct);
        if (supplier == null) return BadRequest(new { message = "Supplier not found." });
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (companyId.HasValue && supplier.CompanyId != companyId.Value)
            return NotFound();
        if (request.JobCardId.HasValue)
        {
            var job = await _db.JobCards.AsNoTracking()
                .Include(j => j.Site).ThenInclude(s => s!.Company)
                .FirstOrDefaultAsync(j => j.Id == request.JobCardId.Value, ct);
            if (job?.Site == null)
                return NotFound();
            if (companyId.HasValue)
            {
                var inScope = isClient
                    ? job.Site.CompanyId == companyId
                    : (job.Site.CompanyId == companyId || (job.Site.Company != null && job.Site.Company.ParentCompanyId == companyId));
                if (!inScope)
                    return NotFound();
            }
        }

        var requestedPartIds = request.Items
            .Where(i => i.PartId != Guid.Empty && i.RequestedQuantity > 0)
            .Select(i => i.PartId)
            .Distinct()
            .ToList();
        if (requestedPartIds.Count == 0)
            return BadRequest(new { message = "No valid part items provided." });

        var parts = await _db.Parts.AsNoTracking()
            .Include(p => p.Company)
            .Include(p => p.Suppliers)
            .Where(p => requestedPartIds.Contains(p.Id))
            .ToListAsync(ct);
        if (parts.Count == 0)
            return BadRequest(new { message = "No valid parts found for this draft." });

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var created = new List<Guid>();
        foreach (var item in request.Items.Where(i => i.PartId != Guid.Empty && i.RequestedQuantity > 0))
        {
            var part = parts.FirstOrDefault(p => p.Id == item.PartId);
            if (part == null || part.IsLabour)
                continue;
            if (companyId.HasValue)
            {
                var inPartScope = isClient
                    ? part.CompanyId == companyId
                    : part.CompanyId.HasValue && (part.CompanyId == companyId || (part.Company != null && part.Company.ParentCompanyId == companyId));
                if (!inPartScope || !part.CompanyId.HasValue || part.CompanyId.Value != supplier.CompanyId)
                    continue;
            }
            if (!PartLinkedToSupplier(part, supplier.Id))
                continue;

            var entity = new SupplierQuoteRequest
            {
                Id = Guid.NewGuid(),
                SupplierId = request.SupplierId,
                PartId = item.PartId,
                JobCardId = request.JobCardId,
                RequestedQuantity = item.RequestedQuantity,
                Status = SupplierQuoteRequestStatus.Requested,
                Notes = string.IsNullOrWhiteSpace(item.Notes) ? "Requested from job card supplier draft." : item.Notes.Trim(),
                CreatedById = userId,
                CreatedAt = DateTime.UtcNow
            };
            _db.SupplierQuoteRequests.Add(entity);
            created.Add(entity.Id);
        }
        await _db.SaveChangesAsync(ct);

        await _emailService.SendCustomEmailAsync(request.ToEmail.Trim(), request.Subject.Trim(), request.Body, ct);

        return Ok(new SupplierQuoteDraftSendResultDto
        {
            SupplierId = request.SupplierId,
            SupplierName = supplier.Name,
            CreatedRequestIds = created
        });
    }

    private static bool PartLinkedToSupplier(Part part, Guid supplierId)
    {
        return part.SupplierId == supplierId || part.Suppliers.Any(ps => ps.SupplierId == supplierId);
    }

    private static int CalculateRequestedQuantity(int requiredForJob, int reorderLevel)
    {
        var required = Math.Max(1, requiredForJob);
        var reorder = Math.Max(0, reorderLevel);
        if (reorder <= 0) return required;
        var qty = reorder;
        while (qty < required)
            qty += reorder;
        return qty;
    }

    private static string BuildDraftEmailBody(string greetingName, JobCard? job, IReadOnlyList<SupplierQuoteEmailDraftItemDto> items, string? notes)
    {
        var lines = new List<string>
        {
            $"Dear {greetingName},",
            "",
            "I hope you are well.",
            "",
            "Could you please provide pricing and availability for the following stock items:"
        };
        foreach (var i in items)
        {
            var unit = string.IsNullOrWhiteSpace(i.Unit) ? "unit/s" : i.Unit!.Trim();
            lines.Add($"- {i.PartName}: required amount {i.RequestedQuantity} ({unit})");
        }
        lines.Add("");
        lines.Add("Please share lead time and unit pricing.");
        if (!string.IsNullOrWhiteSpace(notes))
        {
            lines.Add("");
            lines.Add("Notes:");
            lines.Add(notes.Trim());
        }
        lines.Add("");
        lines.Add("Kind regards,");
        lines.Add("Ian Kleyn Electrical");
        return string.Join("\n", lines);
    }
}

public class SupplierQuoteRequestDto
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public Guid? PartId { get; set; }
    public string? PartName { get; set; }
    public Guid? JobCardId { get; set; }
    public string? JobCardNumber { get; set; }
    public int? RequestedQuantity { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateSupplierQuoteRequest
{
    public Guid SupplierId { get; set; }
    public Guid PartId { get; set; }
    public Guid? JobCardId { get; set; }
    public int? RequestedQuantity { get; set; }
    public string? Notes { get; set; }
}

public class UpdateSupplierQuoteRequestStatus
{
    public string Status { get; set; } = string.Empty;
}

public class SupplierQuoteEmailDraftsResponse
{
    public Guid? JobCardId { get; set; }
    public List<SupplierQuoteEmailDraftDto> Drafts { get; set; } = new();
}

public class SupplierQuoteEmailDraftDto
{
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string ToEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public List<SupplierQuoteEmailDraftItemDto> Items { get; set; } = new();
}

public class SupplierQuoteEmailDraftItemDto
{
    public Guid PartId { get; set; }
    public string PartName { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public int RequiredQuantity { get; set; }
    public int ReorderLevel { get; set; }
    public int StockQuantity { get; set; }
    public int RequestedQuantity { get; set; }
}

public class SendSupplierQuoteDraftRequest
{
    public Guid? JobCardId { get; set; }
    public Guid SupplierId { get; set; }
    public string ToEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public List<SendSupplierQuoteDraftItemDto> Items { get; set; } = new();
}

public class BuildSupplierQuoteDraftRequest
{
    public Guid SupplierId { get; set; }
    public List<BuildSupplierQuoteDraftItem> Items { get; set; } = new();
    public string? Notes { get; set; }
}

public class BuildSupplierQuoteDraftItem
{
    public Guid PartId { get; set; }
    public int? RequestedQuantity { get; set; }
}

public class BuildSupplierQuotePartDraftsRequest
{
    public List<BuildSupplierQuoteDraftItem> Items { get; set; } = new();
}

public class SendSupplierQuoteDraftItemDto
{
    public Guid PartId { get; set; }
    public int RequestedQuantity { get; set; }
    public string? Notes { get; set; }
}

public class SupplierQuoteDraftSendResultDto
{
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public List<Guid> CreatedRequestIds { get; set; } = new();
}
