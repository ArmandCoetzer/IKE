using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.DTOs.Sites;
using Ike.Api.Models;
using Ike.Api.Services;

namespace Ike.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SitesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SitesController(ApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    [Authorize(Policy = "RequireViewSites")]
    [ProducesResponseType(typeof(List<SiteDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SiteDto>>> List([FromQuery] Guid? clientId, [FromQuery] bool? isActive, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        IQueryable<Site> query = _db.Sites.AsNoTracking().Include(s => s.Company);
        if (companyId.HasValue)
        {
            if (isClient)
                query = query.Where(s => s.CompanyId == companyId);
            else
                query = query.Where(s => s.CompanyId == companyId || (s.Company != null && s.Company.ParentCompanyId == companyId));
        }
        if (clientId.HasValue)
            query = query.Where(s => s.CompanyId == clientId);
        if (isActive.HasValue)
            query = query.Where(s => s.IsActive == isActive.Value);

        var list = await query.Select(s => new SiteDto
        {
            Id = s.Id,
            Name = s.Name,
            Address = s.Address,
            Latitude = s.Latitude,
            Longitude = s.Longitude,
            ClientId = s.CompanyId,
            ClientName = s.Company != null ? s.Company.Name : null,
            IsActive = s.IsActive,
            CreatedAt = s.CreatedAt
        }).ToListAsync(ct);
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "RequireViewSites")]
    [ProducesResponseType(typeof(SiteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SiteDto>> Get(Guid id, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var site = await _db.Sites.AsNoTracking().Include(s => s.Company)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (site == null)
            return NotFound();
        if (companyId.HasValue && site.Company != null &&
                (isClient ? site.CompanyId != companyId : site.Company.ParentCompanyId != companyId))
            return NotFound();
        return Ok(new SiteDto
        {
            Id = site.Id,
            Name = site.Name,
            Address = site.Address,
            Latitude = site.Latitude,
            Longitude = site.Longitude,
            ClientId = site.CompanyId,
            ClientName = site.Company?.Name,
            IsActive = site.IsActive,
            CreatedAt = site.CreatedAt
        });
    }

    [HttpPost]
    [Authorize(Policy = "RequireEditSites")]
    [ProducesResponseType(typeof(SiteDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SiteDto>> Create([FromBody] CreateSiteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Name is required." });
        if (!request.ClientId.HasValue)
            return BadRequest(new { message = "Client is required." });
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync();
        if (companyId.HasValue)
        {
            var clientCompany = await _db.Companies.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.ClientId.Value && c.Type == CompanyType.Client);
            if (clientCompany == null)
                return BadRequest(new { message = "Client not found." });
            if (isClient && clientCompany.Id != companyId.Value)
                return BadRequest(new { message = "Client not found." });
            if (!isClient && clientCompany.ParentCompanyId != companyId.Value)
                return BadRequest(new { message = "Client not found." });
        }
        var site = new Site
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            CompanyId = request.ClientId,
            IsActive = true
        };
        _db.Sites.Add(site);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = site.Id }, new SiteDto
        {
            Id = site.Id,
            Name = site.Name,
            Address = site.Address,
            Latitude = site.Latitude,
            Longitude = site.Longitude,
            ClientId = site.CompanyId,
            IsActive = site.IsActive,
            CreatedAt = site.CreatedAt
        });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireEditSites")]
    [ProducesResponseType(typeof(SiteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SiteDto>> Update(Guid id, [FromBody] UpdateSiteRequest request)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync();
        var site = await _db.Sites.Include(s => s.Company).FirstOrDefaultAsync(s => s.Id == id);
        if (site == null)
            return NotFound();
        if (companyId.HasValue && site.Company != null &&
                (isClient ? site.CompanyId != companyId : site.Company.ParentCompanyId != companyId))
            return NotFound();
        if (request.ClientId.HasValue)
        {
            var clientCompany = await _db.Companies.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.ClientId.Value && c.Type == CompanyType.Client);
            if (clientCompany == null)
                return BadRequest(new { message = "Client not found." });
            if (companyId.HasValue)
            {
                if (isClient && clientCompany.Id != companyId.Value)
                    return BadRequest(new { message = "Client not found." });
                if (!isClient && clientCompany.ParentCompanyId != companyId.Value)
                    return BadRequest(new { message = "Client not found." });
            }
        }
        else if (!request.ClientId.HasValue)
            return BadRequest(new { message = "Client is required." });
        if (request.Name != null) site.Name = request.Name.Trim();
        if (request.Address != null) site.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        if (request.Address != null && site.Address == null)
        {
            site.Latitude = null;
            site.Longitude = null;
        }
        if (request.Latitude.HasValue) site.Latitude = request.Latitude;
        if (request.Longitude.HasValue) site.Longitude = request.Longitude;
        if (request.ClientId.HasValue) site.CompanyId = request.ClientId;
        if (request.IsActive.HasValue) site.IsActive = request.IsActive.Value;
        await _db.SaveChangesAsync();
        return Ok(new SiteDto
        {
            Id = site.Id,
            Name = site.Name,
            Address = site.Address,
            Latitude = site.Latitude,
            Longitude = site.Longitude,
            ClientId = site.CompanyId,
            ClientName = site.Company?.Name,
            IsActive = site.IsActive,
            CreatedAt = site.CreatedAt
        });
    }
}
