using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tradion.Api.Data;
using Tradion.Api.DTOs.Tracking;
using Tradion.Api.Models;
using Tradion.Api.Services;

namespace Tradion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TrackingController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<TrackingController> _logger;

    public TrackingController(ApplicationDbContext db, ICurrentUserService currentUser, ILogger<TrackingController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>Technician reports their GPS position from the mobile app.</summary>
    [HttpPost("location")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReportLocation([FromBody] ReportLocationRequest request, CancellationToken ct = default)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        if (request.JobCardId.HasValue)
        {
            var isAssigned = await _db.JobCardAssignments.AnyAsync(a => a.JobCardId == request.JobCardId && a.UserId == userId, ct);
            if (!isAssigned)
                return BadRequest(new { message = "You are not assigned to this job card." });
        }

        _db.TechnicianLocations.Add(new TechnicianLocation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            JobCardId = request.JobCardId,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            AccuracyMeters = request.AccuracyMeters,
            ReportedAt = DateTime.UtcNow
        });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to save technician location: {Inner}", ex.InnerException?.Message ?? ex.Message);
            throw;
        }
        return NoContent();
    }

    /// <summary>Admin/manager gets latest technician positions for the live tracking map.</summary>
    [HttpGet("locations")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(List<TechnicianLocationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<TechnicianLocationDto>>> GetLocations(
        [FromQuery] Guid? jobCardId,
        [FromQuery] string? userId,
        [FromQuery] int maxAgeMinutes = 120,
        CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);

        var cutoff = DateTime.UtcNow.AddMinutes(-maxAgeMinutes);

        IQueryable<TechnicianLocation> query = _db.TechnicianLocations.AsNoTracking()
            .Where(tl => tl.ReportedAt >= cutoff)
            .Include(tl => tl.User)
            .Include(tl => tl.JobCard!).ThenInclude(j => j.Site);

        if (companyId.HasValue)
        {
            if (isClient)
                query = query.Where(tl => tl.JobCard != null && tl.JobCard.Site != null && tl.JobCard.Site.CompanyId == companyId);
            else
                query = query.Where(tl => tl.JobCard == null || tl.JobCard.Site == null ||
                    tl.JobCard.Site.CompanyId == companyId ||
                    (tl.JobCard.Site!.Company != null && tl.JobCard.Site.Company!.ParentCompanyId == companyId));
        }

        if (jobCardId.HasValue)
            query = query.Where(tl => tl.JobCardId == jobCardId);
        if (!string.IsNullOrEmpty(userId))
            query = query.Where(tl => tl.UserId == userId);

        var all = await query.OrderByDescending(tl => tl.ReportedAt).ToListAsync(ct);
        var latestPerUser = all.GroupBy(tl => tl.UserId).Select(g => g.First()).ToList();

        var dtos = latestPerUser.Select(tl => new TechnicianLocationDto
            {
                UserId = tl.UserId,
                UserName = tl.User.FullName ?? tl.User.Email ?? tl.UserId,
                JobCardId = tl.JobCardId,
                JobCardNumber = tl.JobCard != null ? tl.JobCard.JobCardNumber : null,
                SiteName = tl.JobCard != null && tl.JobCard.Site != null ? tl.JobCard.Site.Name : null,
                SiteAddress = tl.JobCard != null && tl.JobCard.Site != null ? tl.JobCard.Site.Address : null,
                SiteLatitude = tl.JobCard != null && tl.JobCard.Site != null ? tl.JobCard.Site.Latitude : null,
                SiteLongitude = tl.JobCard != null && tl.JobCard.Site != null ? tl.JobCard.Site.Longitude : null,
                Latitude = tl.Latitude,
                Longitude = tl.Longitude,
                ReportedAt = tl.ReportedAt
            })
            .ToList();

        return Ok(dtos);
    }
}
