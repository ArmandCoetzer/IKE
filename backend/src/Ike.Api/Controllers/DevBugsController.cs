using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.Helpers;
using Ike.Api.Models;

namespace Ike.Api.Controllers;

[ApiController]
[Route("api/dev/bugs")]
[Authorize]
public class DevBugsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private const string UploadFolder = "uploads/bug-logs";
    private const int MaxFileSizeBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedImageExtensions = { ".png", ".jpg", ".jpeg", ".webp" };

    public DevBugsController(ApplicationDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(BugLogDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BugLogDto>> Create(
        [FromForm] string? title,
        [FromForm] string description,
        [FromForm] IFormFileCollection? images,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(description))
            return BadRequest(new { message = "Description is required." });
        if (description.Length > 4000)
            return BadRequest(new { message = "Description is too long (max 4000 characters)." });
        if (!string.IsNullOrWhiteSpace(title) && title.Trim().Length > 256)
            return BadRequest(new { message = "Title is too long (max 256 characters)." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var bug = new BugLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
            Description = description.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        _db.BugLogs.Add(bug);

        var imageFiles = images?.ToList() ?? new List<IFormFile>();
        if (imageFiles.Count > 0)
        {
            var dir = Path.Combine(_env.ContentRootPath, UploadFolder);
            Directory.CreateDirectory(dir);
            foreach (var file in imageFiles)
            {
                if (file == null || file.Length == 0 || file.Length > MaxFileSizeBytes)
                    return BadRequest(new { message = "Each image must be between 1 byte and 10 MB." });

                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!AllowedImageExtensions.Contains(ext))
                    return BadRequest(new { message = "Allowed image types: PNG, JPG, JPEG, WEBP." });
                var sigErr = await FileContentSignatureHelper.ValidateContentMatchesExtensionAsync(file, ext, ct);
                if (sigErr != null)
                    return BadRequest(new { message = sigErr });

                var name = $"{Guid.NewGuid():N}{ext}";
                var relative = $"{UploadFolder}/{name}";
                var full = Path.Combine(dir, name);
                await using (var stream = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None))
                    await file.CopyToAsync(stream, ct);

                bug.Attachments.Add(new BugLogAttachment
                {
                    Id = Guid.NewGuid(),
                    BugLogId = bug.Id,
                    FileName = file.FileName,
                    FilePath = relative,
                    ContentType = file.ContentType,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = bug.Id }, await BuildBugDtoAsync(bug.Id, ct));
    }

    [HttpGet]
    [Authorize(Roles = SeedData.RoleAdmin)]
    [ProducesResponseType(typeof(List<BugLogListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<BugLogListItemDto>>> List(
        [FromQuery] int take = 300,
        [FromQuery] int skip = 0,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 1000);
        skip = Math.Max(0, skip);
        var rows = await _db.BugLogs.AsNoTracking()
            .Include(b => b.User)
            .Include(b => b.Attachments)
            .OrderByDescending(b => b.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(b => new BugLogListItemDto
            {
                Id = b.Id,
                Title = b.Title,
                Description = b.Description,
                CreatedAt = b.CreatedAt,
                UserId = b.UserId,
                UserName = b.User != null ? (b.User.FullName ?? b.User.Email) : null,
                AttachmentCount = b.Attachments.Count
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = SeedData.RoleAdmin)]
    [ProducesResponseType(typeof(BugLogDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BugLogDto>> Get(Guid id, CancellationToken ct = default)
    {
        var dto = await BuildBugDtoAsync(id, ct);
        if (dto == null) return NotFound();
        return Ok(dto);
    }

    [HttpGet("{id:guid}/attachments/{attachmentId:guid}/file")]
    [Authorize(Roles = SeedData.RoleAdmin)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttachmentFile(Guid id, Guid attachmentId, CancellationToken ct = default)
    {
        var att = await _db.BugLogAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.BugLogId == id, ct);
        if (att == null || string.IsNullOrWhiteSpace(att.FilePath))
            return NotFound();
        var full = Path.Combine(_env.ContentRootPath, att.FilePath.Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(full))
            return NotFound();
        var ext = Path.GetExtension(att.FilePath).ToLowerInvariant();
        var contentType = ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
        var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, contentType, att.FileName);
    }

    private async Task<BugLogDto?> BuildBugDtoAsync(Guid id, CancellationToken ct)
    {
        return await _db.BugLogs.AsNoTracking()
            .Include(b => b.User)
            .Include(b => b.Attachments)
            .Where(b => b.Id == id)
            .Select(b => new BugLogDto
            {
                Id = b.Id,
                Title = b.Title,
                Description = b.Description,
                CreatedAt = b.CreatedAt,
                UserId = b.UserId,
                UserName = b.User != null ? (b.User.FullName ?? b.User.Email) : null,
                Attachments = b.Attachments
                    .OrderBy(x => x.CreatedAt)
                    .Select(x => new BugLogAttachmentDto
                    {
                        Id = x.Id,
                        FileName = x.FileName,
                        ContentType = x.ContentType,
                        CreatedAt = x.CreatedAt
                    }).ToList()
            })
            .FirstOrDefaultAsync(ct);
    }
}

public class BugLogListItemDto
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public int AttachmentCount { get; set; }
}

public class BugLogDto
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public List<BugLogAttachmentDto> Attachments { get; set; } = new();
}

public class BugLogAttachmentDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public DateTime CreatedAt { get; set; }
}
