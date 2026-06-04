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
    private static readonly string[] PartImportHeaders =
    {
        "Name", "Description", "Part Number", "Quantity", "Reorder Level", "Unit", "Unit Price", "Is Labour"
    };
    private static readonly string[] PartUnitOptions =
    {
        "unit/s", "Box", "Pack", "Kg", "g", "L", "ml", "m", "cm", "mm", "Set", "Pair", "Roll", "Hours", "Days"
    };

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public PartsController(ApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet("import-template")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public ActionResult DownloadImportTemplate()
    {
        var xlsx = ImportFileHelper.CreateXlsxTemplate(PartImportHeaders, "Parts", new Dictionary<string, string[]>
        {
            ["Unit"] = PartUnitOptions,
            ["Is Labour"] = new[] { "false", "true" }
        });
        return File(xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "part-import-template.xlsx");
    }

    [HttpPost("import-preview")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(PartImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PartImportResultDto>> ImportPreview([FromForm] IFormFile? file, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (isClient)
            return BadRequest(ApiResponseBodies.Message("Only admin users can import parts."));
        if (!companyId.HasValue)
            return BadRequest(ApiResponseBodies.Message("Parts can only be imported by users with a company."));

        if (file == null || file.Length == 0)
            return BadRequest(ApiResponseBodies.Message("Select an XLSX file to import."));
        var ext = Path.GetExtension(file.FileName);
        if (!ImportFileHelper.AllowedExtensions.Contains(ext))
            return BadRequest(ApiResponseBodies.Message("Only XLSX files are supported."));

        var rows = (await ImportFileHelper.ReadRowsAsync(file, ct)).Select(ToPartImportRow).ToList();
        await ValidatePartImportRowsAsync(rows, companyId.Value, ct);
        return Ok(ToPartImportResult(rows, Array.Empty<PartImportRowDto>()));
    }

    [HttpPost("import-commit")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(typeof(PartImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PartImportResultDto>> ImportCommit([FromBody] PartImportCommitRequest request, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (isClient)
            return BadRequest(ApiResponseBodies.Message("Only admin users can import parts."));
        if (!companyId.HasValue)
            return BadRequest(ApiResponseBodies.Message("Parts can only be imported by users with a company."));

        var rows = request.Rows.Select(ClonePartImportRow).ToList();
        await ValidatePartImportRowsAsync(rows, companyId.Value, ct);
        var failedRows = rows.Where(r => r.Errors.Count > 0).ToList();
        var succeededRows = new List<PartImportRowDto>();

        foreach (var row in rows.Where(r => r.Errors.Count == 0))
        {
            var part = new Part
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId.Value,
                Name = row.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(row.Description) ? null : row.Description.Trim(),
                PartNumber = string.IsNullOrWhiteSpace(row.PartNumber) ? null : row.PartNumber.Trim(),
                Quantity = row.IsLabour ? 0 : row.Quantity,
                ReorderLevel = row.IsLabour ? 0 : row.ReorderLevel,
                UnitPrice = row.UnitPrice,
                IsLabour = row.IsLabour,
                SupplierId = null,
                Unit = string.IsNullOrWhiteSpace(row.Unit) ? (row.IsLabour ? "Hours" : null) : row.Unit.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            _db.Parts.Add(part);
            row.CreatedPartId = part.Id;
            succeededRows.Add(row);
        }

        await _db.SaveChangesAsync(ct);
        return Ok(ToPartImportResult(succeededRows, failedRows));
    }

    private static PartImportRowDto ToPartImportRow(ImportTableRow row)
    {
        string Value(params string[] names)
        {
            foreach (var name in names)
            {
                if (row.Values.TryGetValue(ImportFileHelper.NormalizeHeader(name), out var value))
                    return value.Trim();
            }
            return string.Empty;
        }

        var result = new PartImportRowDto
        {
            RowNumber = row.RowNumber,
            Name = Value("Name", "Part Name"),
            Description = EmptyToNull(Value("Description")),
            PartNumber = EmptyToNull(Value("Part Number", "Part #", "Part No")),
            Unit = EmptyToNull(Value("Unit", "Unit of Measurement"))
        };

        var quantityRaw = Value("Quantity", "Qty");
        if (!string.IsNullOrWhiteSpace(quantityRaw) && int.TryParse(quantityRaw, out var quantity))
            result.Quantity = quantity;
        else if (!string.IsNullOrWhiteSpace(quantityRaw))
            result.Errors.Add("Quantity must be a whole number.");

        var reorderRaw = Value("Reorder Level", "Reorder");
        if (!string.IsNullOrWhiteSpace(reorderRaw) && int.TryParse(reorderRaw, out var reorderLevel))
            result.ReorderLevel = reorderLevel;
        else if (!string.IsNullOrWhiteSpace(reorderRaw))
            result.Errors.Add("Reorder level must be a whole number.");

        var unitPriceRaw = Value("Unit Price", "Price");
        if (!string.IsNullOrWhiteSpace(unitPriceRaw) && decimal.TryParse(unitPriceRaw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var unitPrice))
            result.UnitPrice = unitPrice;
        else if (!string.IsNullOrWhiteSpace(unitPriceRaw))
            result.Errors.Add("Unit price must be a number.");

        result.IsLabour = ParseImportBool(Value("Is Labour", "Labour", "Non Stock"));
        return result;
    }

    private static PartImportRowDto ClonePartImportRow(PartImportRowDto row) => new()
    {
        RowNumber = row.RowNumber,
        Name = row.Name?.Trim() ?? string.Empty,
        Description = EmptyToNull(row.Description),
        PartNumber = EmptyToNull(row.PartNumber),
        Quantity = row.Quantity,
        ReorderLevel = row.ReorderLevel,
        Unit = EmptyToNull(row.Unit),
        UnitPrice = row.UnitPrice,
        IsLabour = row.IsLabour
    };

    private async Task ValidatePartImportRowsAsync(List<PartImportRowDto> rows, Guid companyId, CancellationToken ct)
    {
        var existingParts = await _db.Parts.AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .Select(p => new { p.Name, p.PartNumber })
            .ToListAsync(ct);
        var existingPartNumbers = existingParts
            .Where(p => !string.IsNullOrWhiteSpace(p.PartNumber))
            .Select(p => NormalizeImportKey(p.PartNumber))
            .ToHashSet();
        var existingNames = existingParts.Select(p => NormalizeImportKey(p.Name)).ToHashSet();
        var suppliers = await _db.Suppliers.AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            var parserErrors = row.Errors.ToList();
            row.Errors.Clear();
            row.Errors.AddRange(parserErrors);
            row.Name = row.Name?.Trim() ?? string.Empty;
            row.Description = EmptyToNull(row.Description);
            row.PartNumber = EmptyToNull(row.PartNumber);
            row.Unit = EmptyToNull(row.Unit);

            if (string.IsNullOrWhiteSpace(row.Name))
                row.Errors.Add("Name is required.");
            if (row.Quantity < 0)
                row.Errors.Add("Quantity cannot be negative.");
            if (row.ReorderLevel < 0)
                row.Errors.Add("Reorder level cannot be negative.");
            if (row.UnitPrice < 0)
                row.Errors.Add("Unit price cannot be negative.");

            if (!string.IsNullOrWhiteSpace(row.PartNumber))
            {
                if (existingPartNumbers.Contains(NormalizeImportKey(row.PartNumber)))
                    row.Errors.Add("Part number already exists.");
            }
            else if (!string.IsNullOrWhiteSpace(row.Name) && existingNames.Contains(NormalizeImportKey(row.Name)))
            {
                row.Errors.Add("Part name already exists.");
            }

        }

        foreach (var group in rows.Where(r => !string.IsNullOrWhiteSpace(r.Name)).GroupBy(PartDuplicateKey))
        {
            if (group.Key.Length == 0 || group.Count() <= 1)
                continue;
            foreach (var row in group)
                row.Errors.Add("Duplicate part in the import file.");
        }
    }

    private static PartImportResultDto ToPartImportResult(IEnumerable<PartImportRowDto> successRows, IEnumerable<PartImportRowDto> failedRows)
    {
        var successes = successRows.ToList();
        var failures = failedRows.ToList();
        var rows = successes.Concat(failures).OrderBy(r => r.RowNumber).ToList();
        return new PartImportResultDto
        {
            Rows = rows,
            FailedRows = failures.OrderBy(r => r.RowNumber).ToList(),
            TotalRows = rows.Count,
            SuccessCount = successes.Count,
            FailedCount = failures.Count
        };
    }

    private static string PartDuplicateKey(PartImportRowDto row) =>
        !string.IsNullOrWhiteSpace(row.PartNumber)
            ? $"PN:{NormalizeImportKey(row.PartNumber)}"
            : $"NM:{NormalizeImportKey(row.Name)}";

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ParseImportBool(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase)
               || value.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)
               || value.Trim().Equals("1", StringComparison.OrdinalIgnoreCase)
               || value.Trim().Equals("labour", StringComparison.OrdinalIgnoreCase)
               || value.Trim().Equals("labor", StringComparison.OrdinalIgnoreCase)
               || value.Trim().Equals("non-stock", StringComparison.OrdinalIgnoreCase)
               || value.Trim().Equals("non stock", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeImportKey(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

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
                    || p.Suppliers.Any(ps => ps.Supplier != null && ps.Supplier.Email != null && ps.Supplier.Email != ""),
                SupplierIds = p.Suppliers.Select(ps => ps.SupplierId).ToList(),
                SupplierNames = p.Suppliers.Where(ps => ps.Supplier != null).Select(ps => ps.Supplier.Name).ToList(),
                Unit = p.Unit,
                UnitPrice = p.UnitPrice,
                IsLabour = p.IsLabour,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .ToListAsync(ct);
        ApplyActiveJobReservations(list, await GetActiveJobReservationsAsync(list.Select(p => p.Id).ToList(), ct));
        if (lowStockOnly == true)
            list = list.Where(p => p.IsLowStock).ToList();
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
        var dto = MapToDto(p);
        ApplyActiveJobReservations(new List<PartDto> { dto }, await GetActiveJobReservationsAsync(new List<Guid> { dto.Id }, ct));
        return Ok(dto);
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
        var dto = MapToDto(loaded);
        ApplyActiveJobReservations(new List<PartDto> { dto }, await GetActiveJobReservationsAsync(new List<Guid> { dto.Id }, ct));
        return CreatedAtAction(nameof(Get), new { id = part.Id }, dto);
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
        var loaded = await _db.Parts.AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.Suppliers)
            .ThenInclude(ps => ps.Supplier)
            .FirstAsync(x => x.Id == p.Id, ct);
        var dto = MapToDto(loaded);
        ApplyActiveJobReservations(new List<PartDto> { dto }, await GetActiveJobReservationsAsync(new List<Guid> { dto.Id }, ct));
        return Ok(dto);
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

        var finalizedQuoteLink = await _db.QuoteLineItems.AsNoTracking()
            .AnyAsync(li => li.PartId == id
                && (li.Quote.Status == QuoteStatus.Sent || li.Quote.Status == QuoteStatus.Accepted), ct);
        if (finalizedQuoteLink)
            return BadRequest(ApiResponseBodies.Message("Cannot delete part because it is linked to a sent or accepted quote."));

        var finalizedInvoiceLink = await _db.InvoiceLineItems.AsNoTracking()
            .AnyAsync(li => li.PartId == id
                && (li.Invoice.PartsConfirmed || li.Invoice.Status != InvoiceStatus.Draft), ct);
        if (finalizedInvoiceLink)
            return BadRequest(ApiResponseBodies.Message("Cannot delete part because it is linked to a confirmed, sent, or paid invoice."));

        var completedJobLink = await _db.JobCardPlannedParts.AsNoTracking()
            .AnyAsync(jpp => jpp.PartId == id
                && (jpp.JobCard.Status == JobCardStatus.Completed
                    || jpp.JobCard.Status == JobCardStatus.Done
                    || jpp.JobCard.Status == JobCardStatus.Closed), ct);
        if (completedJobLink)
            return BadRequest(ApiResponseBodies.Message("Cannot delete part because it is linked to a completed or closed job."));

        var lockedSupplierRequestLink = await _db.SupplierQuoteRequests.AsNoTracking()
            .AnyAsync(r => r.PartId == id
                && r.Status != SupplierQuoteRequestStatus.Requested
                && r.Status != SupplierQuoteRequestStatus.Cancelled, ct);
        if (lockedSupplierRequestLink)
            return BadRequest(ApiResponseBodies.Message("Cannot delete part because it is linked to an active supplier quote request."));

        var links = await _db.PartSuppliers.Where(x => x.PartId == id).ToListAsync(ct);
        _db.PartSuppliers.RemoveRange(links);
        var safeSupplierRequests = await _db.SupplierQuoteRequests
            .Where(r => r.PartId == id
                && (r.Status == SupplierQuoteRequestStatus.Requested || r.Status == SupplierQuoteRequestStatus.Cancelled))
            .ToListAsync(ct);
        _db.SupplierQuoteRequests.RemoveRange(safeSupplierRequests);
        var quoteLines = await _db.QuoteLineItems.Where(li => li.PartId == id).ToListAsync(ct);
        foreach (var line in quoteLines)
            line.PartId = null;
        var invoiceLines = await _db.InvoiceLineItems.Where(li => li.PartId == id).ToListAsync(ct);
        foreach (var line in invoiceLines)
            line.PartId = null;
        var plannedParts = await _db.JobCardPlannedParts.Where(jpp => jpp.PartId == id).ToListAsync(ct);
        _db.JobCardPlannedParts.RemoveRange(plannedParts);
        _db.Parts.Remove(p);
        await _db.SaveChangesAsync(ct);
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
                || p.Suppliers.Any(ps => ps.Supplier != null && ps.Supplier.Email != null && ps.Supplier.Email != ""),
            SupplierIds = p.Suppliers.Select(ps => ps.SupplierId).ToList(),
            SupplierNames = p.Suppliers.Where(ps => ps.Supplier != null).Select(ps => ps.Supplier.Name).ToList(),
            Unit = p.Unit,
            UnitPrice = p.UnitPrice,
            IsLabour = p.IsLabour,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        };
    }

    private async Task<Dictionary<Guid, int>> GetActiveJobReservationsAsync(List<Guid> partIds, CancellationToken ct)
    {
        if (partIds.Count == 0)
            return new Dictionary<Guid, int>();

        return await _db.JobCardPlannedParts.AsNoTracking()
            .Where(jpp => partIds.Contains(jpp.PartId))
            .Where(jpp => jpp.JobCard.Status != JobCardStatus.Draft && jpp.JobCard.Status != JobCardStatus.Cancelled)
            .Where(jpp => !_db.Invoices.Any(i => i.JobCardId == jpp.JobCardId && i.PartsConfirmed))
            .GroupBy(jpp => jpp.PartId)
            .Select(g => new { PartId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.PartId, x => x.Quantity, ct);
    }

    private static void ApplyActiveJobReservations(IEnumerable<PartDto> parts, IReadOnlyDictionary<Guid, int> reservationsByPartId)
    {
        foreach (var part in parts)
            part.ReservedForActiveJobsQuantity = reservationsByPartId.TryGetValue(part.Id, out var reserved) ? reserved : 0;
    }
}
