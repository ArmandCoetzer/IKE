using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.DTOs.ServiceRequests;
using Ike.Api.Helpers;
using Ike.Api.Models;
using Ike.Api.Services;

namespace Ike.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ServiceRequestsController : ControllerBase
{
    private const string AttachmentFolder = "uploads/service-request-attachments";
    private const int MaxFileSizeBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = { ".pdf", ".png", ".jpg", ".jpeg" };

    private readonly ApplicationDbContext _db;
    private readonly INotificationService _notificationService;
    private readonly ICurrentUserService _currentUser;
    private readonly IStatusTransitionService _statusTransitions;
    private readonly IWebHostEnvironment _env;

    public ServiceRequestsController(ApplicationDbContext db, INotificationService notificationService, ICurrentUserService currentUser, IStatusTransitionService statusTransitions, IWebHostEnvironment env)
    {
        _db = db;
        _notificationService = notificationService;
        _currentUser = currentUser;
        _statusTransitions = statusTransitions;
        _env = env;
    }

    [HttpGet]
    [Authorize(Policy = "RequireViewRequests")]
    [ProducesResponseType(typeof(List<ServiceRequestDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ServiceRequestDto>>> List([FromQuery] Guid? siteId, [FromQuery] string? status, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        IQueryable<ServiceRequest> query = _db.ServiceRequests.AsNoTracking()
            .Include(sr => sr.Site)
            .ThenInclude(s => s!.Company)
            .Include(sr => sr.RequestedByUser);
        if (companyId.HasValue)
        {
            if (isClient)
                query = query.Where(sr => sr.Site != null && sr.Site.CompanyId == companyId);
            else
                query = query.Where(sr => sr.Site != null && sr.Site.Company != null && sr.Site.Company.ParentCompanyId == companyId);
        }
        if (siteId.HasValue)
            query = query.Where(sr => sr.SiteId == siteId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!ServiceRequestStatus.IsValid(status))
                return BadRequest(ApiResponseBodies.Message($"Status must be one of: {string.Join(", ", ServiceRequestStatus.All)}."));
            var normalized = ServiceRequestStatus.Normalize(status);
            query = query.Where(sr => sr.Status == normalized);
        }

        var list = await query.OrderByDescending(sr => sr.CreatedAt)
            .Select(sr => new ServiceRequestDto
            {
                Id = sr.Id,
                RequestNumber = sr.RequestNumber,
                SiteId = sr.SiteId,
                CompanyId = sr.Site != null ? sr.Site.CompanyId : null,
                SiteName = sr.Site != null ? sr.Site.Name : null,
                RequestedByUserName = sr.RequestedByUser != null ? (sr.RequestedByUser.FullName ?? sr.RequestedByUser.Email) : null,
                Description = sr.Description,
                Priority = sr.Priority,
                Status = sr.Status,
                OptionalDueDate = sr.OptionalDueDate,
                CreatedAt = sr.CreatedAt,
                JobCardId = null,
                JobCardNumber = null
            }).ToListAsync();
        var requestIds = list.Select(sr => sr.Id).ToList();
        var jobCards = await _db.JobCards.AsNoTracking()
            .Where(j => j.ServiceRequestId.HasValue && requestIds.Contains(j.ServiceRequestId.Value))
            .Select(j => new { j.ServiceRequestId, j.Id, j.JobCardNumber, j.Status })
            .ToListAsync();
        var jobCardIds = jobCards.Select(j => j.Id).ToList();
        var assignments = await _db.JobCardAssignments.AsNoTracking()
            .Where(a => jobCardIds.Contains(a.JobCardId))
            .Select(a => new { a.JobCardId, Name = a.User.FullName ?? a.User.Email })
            .ToListAsync();
        foreach (var dto in list)
        {
            var jc = jobCards.FirstOrDefault(j => j.ServiceRequestId == dto.Id);
            if (jc != null)
            {
                dto.JobCardId = jc.Id;
                dto.JobCardNumber = jc.JobCardNumber;
                dto.JobCardStatus = jc.Status;
                var techNames = assignments
                    .Where(a => a.JobCardId == jc.Id && !string.IsNullOrEmpty(a.Name))
                    .Select(a => a.Name!)
                    .Distinct()
                    .ToList();
                dto.AssignedTechnicianNames = techNames.Count > 0 ? string.Join(", ", techNames) : null;
            }
        }
        return Ok(list);
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = "RequireViewRequests")]
    [ProducesResponseType(typeof(ServiceRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceRequestDto>> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest request, CancellationToken ct = default)
    {
        var sr = await LoadServiceRequestInScopeForUpdateAsync(id, ct);
        if (sr == null)
            return NotFound();
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!_statusTransitions.TryTransitionServiceRequest(sr.Status, request.Status, out var nextStatus, out var transitionError))
                return BadRequest(ApiResponseBodies.Message(transitionError));
            sr.Status = nextStatus;
        }
        await _db.SaveChangesAsync(ct);
        return await Get(id);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireViewRequests")]
    [ProducesResponseType(typeof(ServiceRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceRequestDto>> Update(Guid id, [FromBody] UpdateServiceRequestRequest request, CancellationToken ct = default)
    {
        var sr = await LoadServiceRequestInScopeForUpdateAsync(id, ct);
        if (sr == null)
            return NotFound();
        var site = await _db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SiteId, ct);
        if (site == null)
            return BadRequest(ApiResponseBodies.Message("Site not found."));
        if (sr.SiteId != request.SiteId)
            return BadRequest(ApiResponseBodies.Message("Service request site cannot be changed."));
        if (request.Priority < 1 || request.Priority > 5)
            return BadRequest(ApiResponseBodies.Message("Priority must be between 1 and 5."));
        sr.SiteId = request.SiteId;
        sr.Description = request.Description.Trim();
        sr.Priority = request.Priority;
        sr.OptionalDueDate = request.OptionalDueDate;
        sr.PenaltyFee = request.PenaltyFee;
        sr.PenaltyNote = string.IsNullOrWhiteSpace(request.PenaltyNote) ? null : request.PenaltyNote.Trim();
        sr.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await Get(id);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "RequireViewRequests")]
    [ProducesResponseType(typeof(ServiceRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceRequestDto>> Get(Guid id, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var sr = await _db.ServiceRequests.AsNoTracking()
            .Include(s => s.Site)
            .ThenInclude(site => site!.Company)
            .Include(s => s.RequestedByUser)
            .Include(s => s.Attachments)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sr == null)
            return NotFound();
        if (companyId.HasValue && (sr.Site == null || sr.Site.Company == null ||
                (isClient ? sr.Site.CompanyId != companyId : sr.Site.Company.ParentCompanyId != companyId)))
            return NotFound();
        var jobCard = await _db.JobCards.AsNoTracking()
            .Where(j => j.ServiceRequestId == id)
            .Select(j => new { j.Id, j.JobCardNumber, j.Status })
            .FirstOrDefaultAsync();
        return Ok(new ServiceRequestDto
        {
            Id = sr.Id,
            RequestNumber = sr.RequestNumber,
            SiteId = sr.SiteId,
            SiteName = sr.Site?.Name,
            RequestedByUserName = sr.RequestedByUser?.FullName ?? sr.RequestedByUser?.Email,
            Description = sr.Description,
            Priority = sr.Priority,
            Status = sr.Status,
            OptionalDueDate = sr.OptionalDueDate,
            CreatedAt = sr.CreatedAt,
            JobCardId = jobCard?.Id,
            JobCardNumber = jobCard?.JobCardNumber,
            JobCardStatus = jobCard?.Status,
            AssignedTechnicianNames = jobCard != null ? await GetAssignedTechnicianNamesAsync(jobCard.Id, ct) : null,
            PenaltyFee = sr.PenaltyFee,
            PenaltyNote = sr.PenaltyNote,
            Attachments = sr.Attachments.OrderByDescending(a => a.CreatedAt).Select(a => new ServiceRequestAttachmentDto
            {
                Id = a.Id,
                FileName = a.FileName,
                CreatedAt = a.CreatedAt
            }).ToList()
        });
    }

    [HttpPost("{id:guid}/attachments")]
    [Authorize(Policy = "RequireViewRequests")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadAttachments(Guid id, [FromForm] IFormFileCollection? files, CancellationToken ct = default)
    {
        var sr = await _db.ServiceRequests.AsNoTracking().Include(s => s.Site).ThenInclude(site => site!.Company).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sr == null)
            return NotFound();
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (companyId.HasValue && (sr.Site == null || sr.Site.Company == null ||
                (isClient ? sr.Site.CompanyId != companyId : sr.Site.Company.ParentCompanyId != companyId)))
            return NotFound();
        if (files == null || files.Count == 0)
            return BadRequest(ApiResponseBodies.Message("At least one file is required."));
        var dir = Path.Combine(_env.ContentRootPath, AttachmentFolder);
        Directory.CreateDirectory(dir);
        foreach (var file in files)
        {
            if (file.Length == 0 || file.Length > MaxFileSizeBytes)
                return BadRequest(ApiResponseBodies.Message("Each file must be between 1 byte and 10 MB."));
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
                return BadRequest(ApiResponseBodies.Message("Allowed types: PDF, PNG, JPG, JPEG."));
            var sigErr = await FileContentSignatureHelper.ValidateContentMatchesExtensionAsync(file, ext, ct);
            if (sigErr != null)
                return BadRequest(ApiResponseBodies.Message(sigErr));
        }
        foreach (var file in files)
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var fileName = Guid.NewGuid().ToString("N") + ext;
            var relativePath = AttachmentFolder + "/" + fileName;
            var fullPath = Path.Combine(dir, fileName);
            await using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
                await file.CopyToAsync(stream, ct);
            _db.ServiceRequestAttachments.Add(new ServiceRequestAttachment
            {
                Id = Guid.NewGuid(),
                ServiceRequestId = id,
                FileName = file.FileName,
                FilePath = relativePath,
                ContentType = file.ContentType,
                CreatedAt = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/attachments/{attachmentId:guid}/file")]
    [Authorize(Policy = "RequireViewRequests")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttachmentFile(Guid id, Guid attachmentId, CancellationToken ct = default)
    {
        if (!await CanAccessServiceRequestInScopeAsync(id, ct))
            return NotFound();
        var att = await _db.ServiceRequestAttachments.AsNoTracking()
            .Where(a => a.Id == attachmentId && a.ServiceRequestId == id)
            .Select(a => new { a.FilePath, a.FileName })
            .FirstOrDefaultAsync(ct);
        if (att == null || string.IsNullOrEmpty(att.FilePath))
            return NotFound();
        var fullPath = Path.Combine(_env.ContentRootPath, att.FilePath);
        if (!System.IO.File.Exists(fullPath))
            return NotFound();
        var bytes = await System.IO.File.ReadAllBytesAsync(fullPath, ct);
        var ext = Path.GetExtension(att.FilePath).ToLowerInvariant();
        var contentType = ext switch { ".pdf" => "application/pdf", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", _ => "application/octet-stream" };
        return File(bytes, contentType, att.FileName);
    }

    [HttpPost]
    [Authorize(Policy = "RequireViewRequests")]
    [ProducesResponseType(typeof(ServiceRequestDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ServiceRequestDto>> Create([FromBody] CreateServiceRequestRequest request, CancellationToken ct)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (!isClient)
            return Forbid();
        var site = await _db.Sites.AsNoTracking().Include(s => s.Company).FirstOrDefaultAsync(s => s.Id == request.SiteId, ct);
        if (site == null)
            return BadRequest(ApiResponseBodies.Message("Site not found."));
        if (companyId.HasValue && (isClient ? site.CompanyId != companyId : (site.Company == null || site.Company.ParentCompanyId != companyId)))
            return BadRequest(ApiResponseBodies.Message("Site not found."));
        if (request.Priority < 1 || request.Priority > 5)
            return BadRequest(ApiResponseBodies.Message("Priority must be between 1 and 5."));
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var requestNumber = NumberGenerator.NextRequestNumber(_db.ServiceRequests);
        var sr = new ServiceRequest
        {
            Id = Guid.NewGuid(),
            RequestNumber = requestNumber,
            SiteId = request.SiteId,
            RequestedByUserId = userId,
            Description = request.Description.Trim(),
            Priority = request.Priority,
            Status = ServiceRequestStatus.New,
            OptionalDueDate = request.OptionalDueDate,
            CreatedAt = DateTime.UtcNow
        };
        _db.ServiceRequests.Add(sr);
        await _db.SaveChangesAsync(ct);

        await _notificationService.NotifyUsersWithPermissionAsync(
            "ViewRequests",
            "New service request",
            "Request " + requestNumber + " has been created.",
            "ServiceRequest",
            sr.Id.ToString(),
            excludeUserId: userId,
            scopeCompanyId: isClient ? site.Company?.ParentCompanyId : companyId,
            ct);

        return CreatedAtAction(nameof(Get), new { id = sr.Id }, await Get(sr.Id));
    }

    private async Task<string?> GetAssignedTechnicianNamesAsync(Guid jobCardId, CancellationToken ct)
    {
        var names = await _db.JobCardAssignments.AsNoTracking()
            .Where(a => a.JobCardId == jobCardId)
            .Select(a => a.User.FullName ?? a.User.Email)
            .Where(n => n != null)
            .Distinct()
            .ToListAsync(ct);
        return names.Count > 0 ? string.Join(", ", names!) : null;
    }

    private async Task<ServiceRequest?> LoadServiceRequestInScopeForUpdateAsync(Guid id, CancellationToken ct)
    {
        var sr = await _db.ServiceRequests
            .Include(s => s.Site).ThenInclude(site => site!.Company)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sr == null) return null;

        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (!companyId.HasValue) return sr;
        if (sr.Site == null || sr.Site.Company == null) return null;

        var inScope = isClient
            ? sr.Site.CompanyId == companyId
            : sr.Site.Company.ParentCompanyId == companyId;
        return inScope ? sr : null;
    }

    private async Task<bool> CanAccessServiceRequestInScopeAsync(Guid id, CancellationToken ct) =>
        await LoadServiceRequestInScopeForUpdateAsync(id, ct) != null;
}
