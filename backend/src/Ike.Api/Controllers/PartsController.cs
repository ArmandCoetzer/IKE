using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.DTOs.Parts;
using Ike.Api.Helpers;
using Ike.Api.Models;
using Ike.Api.Services;

namespace Ike.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PartsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public PartsController(ApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    [Authorize(Policy = "RequireViewPurchaseOrders")]
    [ProducesResponseType(typeof(List<PartDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PartDto>>> List([FromQuery] bool? lowStockOnly, [FromQuery] Guid? forCompanyId, CancellationToken ct)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var scopeCompanyId = companyId;
        if (companyId.HasValue)
        {
            var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId.Value, ct);
            if (isClient)
                scopeCompanyId = company?.ParentCompanyId;
        }
        // When forCompanyId is provided (e.g. job edit page), scope to that exact company - only parts belonging to it
        if (forCompanyId.HasValue && scopeCompanyId.HasValue)
        {
            var forCo = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == forCompanyId.Value, ct);
            if (forCo != null)
            {
                var hasAccess = forCompanyId == scopeCompanyId || forCo.ParentCompanyId == scopeCompanyId;
                if (hasAccess)
                    scopeCompanyId = forCompanyId; // Scope to the job's company, not the parent
            }
        }
        if (!scopeCompanyId.HasValue)
            return Ok(new List<PartDto>());

        IQueryable<Part> query = _db.Parts.AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Suppliers)
            .ThenInclude(ps => ps.Supplier)
            .Where(p => p.CompanyId != null && p.CompanyId == scopeCompanyId.Value)
            .OrderBy(p => p.Name);

        if (lowStockOnly == true)
            query = query.Where(p => p.Quantity <= p.ReorderLevel);

        var list = await query
            .Select(p => new PartDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                PartNumber = p.PartNumber,
                Quantity = p.Quantity,
                ReorderLevel = p.ReorderLevel,
                SupplierId = p.SupplierId,
                SupplierName = p.Supplier != null ? p.Supplier.Name : null,
                HasSupplierEmail = (p.Supplier != null && p.Supplier.Email != null && p.Supplier.Email != "")
                    || p.Suppliers.Any(ps => ps.Supplier.Email != null && ps.Supplier.Email != ""),
                SupplierIds = p.Suppliers.Select(ps => ps.SupplierId).ToList(),
                SupplierNames = p.Suppliers.Select(ps => ps.Supplier.Name).ToList(),
                Unit = p.Unit,
                UnitPrice = p.UnitPrice,
                IsLabour = p.IsLabour,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "RequireViewPurchaseOrders")]
    [ProducesResponseType(typeof(PartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PartDto>> Get(Guid id, CancellationToken ct)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var scopeCompanyId = companyId;
        if (isClient && companyId.HasValue)
        {
            var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId.Value, ct);
            scopeCompanyId = company?.ParentCompanyId;
        }
        var p = await _db.Parts.AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.Suppliers)
            .ThenInclude(ps => ps.Supplier)
            .Where(x => x.Id == id && scopeCompanyId.HasValue && x.CompanyId == scopeCompanyId.Value)
            .FirstOrDefaultAsync(ct);
        if (p == null) return NotFound();
        return Ok(MapToDto(p));
    }

    [HttpPost]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(typeof(PartDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PartDto>> Create([FromBody] CreatePartRequest request, [FromQuery] Guid? forCompanyId, CancellationToken ct)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (isClient)
            return BadRequest(ApiResponseBodies.Message("Only admin users can create parts."));
        if (!companyId.HasValue)
            return BadRequest(ApiResponseBodies.Message("Parts can only be created by users with a company."));
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponseBodies.Message("Name is required."));
        var partCompanyId = companyId.Value;
        if (forCompanyId.HasValue)
        {
            var forCo = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == forCompanyId.Value, ct);
            if (forCo != null && (forCompanyId == companyId || forCo.ParentCompanyId == companyId))
                partCompanyId = forCompanyId.Value;
        }
        if (!request.IsLabour && request.SupplierId.HasValue)
        {
            var sup = await _db.Suppliers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.SupplierId.Value && s.CompanyId == partCompanyId, ct);
            if (sup == null) return NotFound(ApiResponseBodies.Message("Supplier not found in your profile scope."));
        }
        if (!request.IsLabour && request.SupplierIds != null && request.SupplierIds.Count > 0)
        {
            var requestedIds = request.SupplierIds.Distinct().ToList();
            var validIds = await _db.Suppliers.AsNoTracking()
                .Where(s => requestedIds.Contains(s.Id) && s.CompanyId == partCompanyId)
                .Select(s => s.Id)
                .ToListAsync(ct);
            if (validIds.Count != requestedIds.Count)
                return NotFound(ApiResponseBodies.Message("One or more suppliers were not found in your profile scope."));
        }
        var part = new Part
        {
            Id = Guid.NewGuid(),
            CompanyId = partCompanyId,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            PartNumber = string.IsNullOrWhiteSpace(request.PartNumber) ? null : request.PartNumber.Trim(),
            Quantity = request.Quantity,
            ReorderLevel = request.ReorderLevel,
            UnitPrice = request.UnitPrice ?? 0,
            IsLabour = request.IsLabour,
            SupplierId = request.IsLabour ? null : request.SupplierId,
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? null : request.Unit.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        _db.Parts.Add(part);
        var supplierIds = request.IsLabour
            ? new List<Guid>()
            : (request.SupplierIds ?? new List<Guid>()).Distinct().ToList();
        if (!request.IsLabour)
        {
            if (part.SupplierId.HasValue && !supplierIds.Contains(part.SupplierId.Value))
                supplierIds.Insert(0, part.SupplierId.Value);
            if (!part.SupplierId.HasValue && supplierIds.Count > 0)
                part.SupplierId = supplierIds[0];
        }
        foreach (var sid in supplierIds)
        {
            _db.PartSuppliers.Add(new PartSupplier
            {
                PartId = part.Id,
                SupplierId = sid,
                LinkedAt = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync();
        var loaded = await _db.Parts.AsNoTracking().Include(x => x.Supplier).Include(x => x.Suppliers).ThenInclude(ps => ps.Supplier).FirstAsync(x => x.Id == part.Id);
        return CreatedAtAction(nameof(Get), new { id = part.Id }, MapToDto(loaded));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(typeof(PartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PartDto>> Update(Guid id, [FromBody] UpdatePartRequest request, CancellationToken ct)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var scopeCompanyId = companyId;
        if (isClient && companyId.HasValue)
        {
            var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId.Value, ct);
            scopeCompanyId = company?.ParentCompanyId;
        }
        var p = await _db.Parts.Include(x => x.Supplier)
            .FirstOrDefaultAsync(x => x.Id == id && scopeCompanyId.HasValue && x.CompanyId == scopeCompanyId.Value, ct);
        if (p == null) return NotFound();
        if (request.Name != null) p.Name = request.Name.Trim();
        if (request.Description != null) p.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (request.PartNumber != null) p.PartNumber = string.IsNullOrWhiteSpace(request.PartNumber) ? null : request.PartNumber.Trim();
        if (request.Quantity.HasValue) p.Quantity = request.Quantity.Value;
        if (request.ReorderLevel.HasValue) p.ReorderLevel = request.ReorderLevel.Value;
        var willBeLabour = request.IsLabour ?? p.IsLabour;
        if (!willBeLabour && request.SupplierId.HasValue)
        {
            var sup = await _db.Suppliers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.SupplierId.Value && s.CompanyId == p.CompanyId, ct);
            if (sup == null) return NotFound(ApiResponseBodies.Message("Supplier not found in this part's profile scope."));
            p.SupplierId = request.SupplierId;
        }
        if (!willBeLabour && request.SupplierIds != null)
        {
            var supplierIds = request.SupplierIds.Distinct().ToList();
            var validIds = await _db.Suppliers.AsNoTracking()
                .Where(s => supplierIds.Contains(s.Id) && s.CompanyId == p.CompanyId)
                .Select(s => s.Id)
                .ToListAsync(ct);
            if (validIds.Count != supplierIds.Count)
                return NotFound(ApiResponseBodies.Message("One or more suppliers were not found in this part's profile scope."));

            var existingLinks = await _db.PartSuppliers.Where(ps => ps.PartId == id).ToListAsync(ct);
            _db.PartSuppliers.RemoveRange(existingLinks);
            foreach (var sid in supplierIds)
            {
                _db.PartSuppliers.Add(new PartSupplier
                {
                    PartId = id,
                    SupplierId = sid,
                    LinkedAt = DateTime.UtcNow
                });
            }
            if (!p.SupplierId.HasValue && supplierIds.Count > 0)
                p.SupplierId = supplierIds[0];
            if (p.SupplierId.HasValue && supplierIds.Count > 0 && !supplierIds.Contains(p.SupplierId.Value))
                p.SupplierId = supplierIds[0];
            if (supplierIds.Count == 0)
                p.SupplierId = null;
        }
        else if (request.SupplierId.HasValue == false)
            p.SupplierId = null;
        if (request.Unit != null) p.Unit = string.IsNullOrWhiteSpace(request.Unit) ? null : request.Unit.Trim();
        if (request.UnitPrice.HasValue) p.UnitPrice = request.UnitPrice.Value;
        if (request.IsLabour.HasValue && request.IsLabour.Value)
        {
            p.IsLabour = true;
            p.Quantity = 0;
            p.ReorderLevel = 0;
            p.SupplierId = null;
            var existingLinks = await _db.PartSuppliers.Where(ps => ps.PartId == id).ToListAsync(ct);
            if (existingLinks.Count > 0)
                _db.PartSuppliers.RemoveRange(existingLinks);
        }
        else if (request.IsLabour.HasValue)
            p.IsLabour = false;
        p.UpdatedAt = DateTime.UtcNow;
        // CompanyId is never modified on update - part stays in its company
        await _db.SaveChangesAsync();
        return Ok(MapToDto(p));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var scopeCompanyId = companyId;
        if (isClient && companyId.HasValue)
        {
            var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId.Value, ct);
            scopeCompanyId = company?.ParentCompanyId;
        }
        var p = await _db.Parts.FirstOrDefaultAsync(x => x.Id == id && scopeCompanyId.HasValue && x.CompanyId == scopeCompanyId.Value, ct);
        if (p == null) return NotFound();
        var links = await _db.PartSuppliers.Where(x => x.PartId == id).ToListAsync(ct);
        _db.PartSuppliers.RemoveRange(links);
        _db.Parts.Remove(p);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static PartDto MapToDto(Part p)
    {
        return new PartDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            PartNumber = p.PartNumber,
            Quantity = p.Quantity,
            ReorderLevel = p.ReorderLevel,
            SupplierId = p.SupplierId,
            SupplierName = p.Supplier?.Name,
            HasSupplierEmail = (p.Supplier != null && p.Supplier.Email != null && p.Supplier.Email != "")
                || p.Suppliers.Any(ps => ps.Supplier.Email != null && ps.Supplier.Email != ""),
            SupplierIds = p.Suppliers.Select(ps => ps.SupplierId).ToList(),
            SupplierNames = p.Suppliers.Select(ps => ps.Supplier.Name).ToList(),
            Unit = p.Unit,
            UnitPrice = p.UnitPrice,
            IsLabour = p.IsLabour,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        };
    }
}
