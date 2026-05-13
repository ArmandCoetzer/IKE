using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tradion.Api.Data;
using Tradion.Api.DTOs.Notifications;
using Tradion.Api.Models;

namespace Tradion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public NotificationsController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(UnreadCountDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadCountDto>> UnreadCount(CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();
        var count = await _db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);
        return Ok(new UnreadCountDto { Count = count });
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<NotificationDto>>> List(CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var list = await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderBy(n => n.ReadAt != null)
            .ThenByDescending(n => n.CreatedAt)
            .Select(n => new NotificationDto
            {
                Id = n.Id,
                Title = n.Title,
                Body = n.Body,
                Type = n.Type,
                RelatedEntityId = n.RelatedEntityId,
                CreatedAt = n.CreatedAt,
                ReadAt = n.ReadAt
            })
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPatch("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var n = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct);
        if (n == null)
            return NotFound();
        n.ReadAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("mark-all-read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var unread = await _db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null).ToListAsync(ct);
        foreach (var n in unread)
            n.ReadAt = DateTime.UtcNow;
        if (unread.Count > 0)
            await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
