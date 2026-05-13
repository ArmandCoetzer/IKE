using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tradion.Api.Data;

namespace Tradion.Api.Controllers;

[ApiController]
[Route("api/dev/audit")]
[Authorize(Roles = SeedData.RoleAdmin)]
public class DevAuditController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public DevAuditController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet("errors")]
    [ProducesResponseType(typeof(List<AuditErrorEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<AuditErrorEntryDto>>> ListErrors(
        [FromQuery] int take = 300,
        [FromQuery] int skip = 0,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 1000);
        skip = Math.Max(0, skip);
        var rows = await _db.AuditErrorEntries.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(x => new AuditErrorEntryDto
            {
                Id = x.Id,
                UserId = x.UserId,
                Method = x.Method,
                Path = x.Path,
                StatusCode = x.StatusCode,
                Message = x.Message,
                Details = x.Details,
                TraceId = x.TraceId,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(ct);
        return Ok(rows);
    }
}

public class AuditErrorEntryDto
{
    public Guid Id { get; set; }
    public string? UserId { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? TraceId { get; set; }
    public DateTime CreatedAt { get; set; }
}
