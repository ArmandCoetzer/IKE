using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tradion.Api.Data;
using Tradion.Api.Models;
using Tradion.Api.Services;

namespace Tradion.Api.Controllers;

[ApiController]
[Route("api/risk-alerts")]
[Authorize]
public class RiskAlertsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public RiskAlertsController(ApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    [Authorize(Policy = "RequireViewReports")]
    [ProducesResponseType(typeof(List<RiskAlertDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<RiskAlertDto>>> List([FromQuery] bool includeResolved = false, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        IQueryable<RiskAlert> query = _db.RiskAlerts.AsNoTracking();
        if (!includeResolved)
            query = query.Where(a => a.ResolvedAt == null);

        if (companyId.HasValue)
        {
            if (isClient)
                query = query.Where(a => a.CompanyId == companyId);
            else
            {
                var childCompanyIds = await _db.Companies.AsNoTracking()
                    .Where(c => c.ParentCompanyId == companyId.Value)
                    .Select(c => c.Id)
                    .ToListAsync(ct);
                query = query.Where(a => a.CompanyId == companyId || (a.CompanyId.HasValue && childCompanyIds.Contains(a.CompanyId.Value)));
            }
        }

        var list = await query
            .OrderByDescending(a => a.LastDetectedAt)
            .Take(200)
            .Select(a => new RiskAlertDto
            {
                Id = a.Id,
                CompanyId = a.CompanyId,
                AlertType = a.AlertType,
                Severity = a.Severity,
                Title = a.Title,
                Details = a.Details,
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                FirstDetectedAt = a.FirstDetectedAt,
                LastDetectedAt = a.LastDetectedAt,
                ResolvedAt = a.ResolvedAt
            })
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPatch("{id:guid}/resolve")]
    [Authorize(Policy = "RequireViewReports")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Resolve(Guid id, CancellationToken ct = default)
    {
        var alert = await _db.RiskAlerts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (alert == null) return NotFound();
        if (alert.ResolvedAt.HasValue) return NoContent();

        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (companyId.HasValue)
        {
            var allowed = isClient
                ? alert.CompanyId == companyId
                : (alert.CompanyId == companyId || await _db.Companies.AsNoTracking().AnyAsync(c => c.Id == alert.CompanyId && c.ParentCompanyId == companyId, ct));
            if (!allowed) return NotFound();
        }

        alert.ResolvedAt = DateTime.UtcNow;
        alert.ResolvedByUserId = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public class RiskAlertDto
{
    public Guid Id { get; set; }
    public Guid? CompanyId { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public DateTime FirstDetectedAt { get; set; }
    public DateTime LastDetectedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
