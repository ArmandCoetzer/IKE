using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.DTOs.PermitTypes;
using Ike.Api.Helpers;
using Ike.Api.Models;
using Ike.Api.Services;

namespace Ike.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PermitTypesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public PermitTypesController(ApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<PermitTypeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PermitTypeDto>>> List([FromQuery] bool? isActive, CancellationToken ct)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var scopeCompanyId = companyId;
        if (isClient && companyId.HasValue)
        {
            var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId.Value, ct);
            scopeCompanyId = company?.ParentCompanyId;
        }
        IQueryable<PermitType> query = _db.PermitTypes.AsNoTracking();
        if (scopeCompanyId.HasValue)
            query = query.Where(p => p.CompanyId == scopeCompanyId.Value);
        else
            query = query.Where(p => false); // No scope = no permit types
        if (isActive.HasValue)
            query = query.Where(p => p.IsActive == isActive.Value);

        var list = await query.Select(p => new PermitTypeDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            IsActive = p.IsActive,
            CreatedAt = p.CreatedAt,
            IsWorkAuthorisation = p.IsWorkAuthorisation,
            TriggersPermitTypeIdsJson = p.TriggersPermitTypeIdsJson
        }).ToListAsync(ct);
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PermitTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PermitTypeDto>> Get(Guid id, CancellationToken ct)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var scopeCompanyId = companyId;
        if (isClient && companyId.HasValue)
        {
            var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId.Value, ct);
            scopeCompanyId = company?.ParentCompanyId;
        }
        var query = _db.PermitTypes.AsNoTracking().Where(p => p.Id == id);
        if (scopeCompanyId.HasValue)
            query = query.Where(p => p.CompanyId == scopeCompanyId.Value);
        else
            query = query.Where(p => false);
        var permitType = await query.FirstOrDefaultAsync(ct);
        if (permitType == null)
            return NotFound();
        return Ok(new PermitTypeDto
        {
            Id = permitType.Id,
            Name = permitType.Name,
            Description = permitType.Description,
            IsActive = permitType.IsActive,
            CreatedAt = permitType.CreatedAt,
            IsWorkAuthorisation = permitType.IsWorkAuthorisation,
            TriggersPermitTypeIdsJson = permitType.TriggersPermitTypeIdsJson
        });
    }

    [HttpPost]
    [ProducesResponseType(typeof(PermitTypeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PermitTypeDto>> Create([FromBody] CreatePermitTypeRequest request, CancellationToken ct)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (isClient)
            return BadRequest(ApiResponseBodies.Message("Only admin users can create permit types."));
        if (!companyId.HasValue)
            return BadRequest(ApiResponseBodies.Message("Permit types can only be created by users with a company."));
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponseBodies.Message("Name is required."));
        var permitType = new PermitType
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IsActive = true,
            CompanyId = companyId.Value,
            IsWorkAuthorisation = request.IsWorkAuthorisation,
            TriggersPermitTypeIdsJson = string.IsNullOrWhiteSpace(request.TriggersPermitTypeIdsJson) ? null : request.TriggersPermitTypeIdsJson.Trim()
        };
        _db.PermitTypes.Add(permitType);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = permitType.Id }, new PermitTypeDto
        {
            Id = permitType.Id,
            Name = permitType.Name,
            Description = permitType.Description,
            IsActive = permitType.IsActive,
            CreatedAt = permitType.CreatedAt,
            IsWorkAuthorisation = permitType.IsWorkAuthorisation,
            TriggersPermitTypeIdsJson = permitType.TriggersPermitTypeIdsJson
        });
    }
}
