using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tradion.Api.Data;
using Tradion.Api.DTOs.JobCards;
using Tradion.Api.Helpers;
using Tradion.Api.Models;
using Tradion.Api.Services;

namespace Tradion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class JobCardsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly INotificationService _notificationService;
    private readonly IAuditService _auditService;
    private readonly IRealtimeHub _realtimeHub;
    private readonly IWebHostEnvironment _env;
    private readonly IWorkAuthorizationPermitRulesService _workAuthorizationPermitRules;

    public JobCardsController(
        ApplicationDbContext db,
        ICurrentUserService currentUser,
        INotificationService notificationService,
        IAuditService auditService,
        IRealtimeHub realtimeHub,
        IWebHostEnvironment env,
        IWorkAuthorizationPermitRulesService workAuthorizationPermitRules)
    {
        _db = db;
        _currentUser = currentUser;
        _notificationService = notificationService;
        _auditService = auditService;
        _realtimeHub = realtimeHub;
        _env = env;
        _workAuthorizationPermitRules = workAuthorizationPermitRules;
    }

    [HttpGet]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(List<JobCardListDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<JobCardListDto>>> List([FromQuery] Guid? siteId, [FromQuery] string? status, [FromQuery] bool assignedToMe = false, [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        IQueryable<JobCard> query = _db.JobCards.AsNoTracking()
            .Include(j => j.Site)
            .ThenInclude(s => s!.Company)
            .Include(j => j.ServiceRequest)
            .Include(j => j.Assignments)
            .ThenInclude(a => a.User);
        if (companyId.HasValue)
        {
            if (isClient)
                query = query.Where(j => j.Site != null && j.Site.CompanyId == companyId);
            else
                query = query.Where(j => j.Site != null && (j.Site.CompanyId == companyId || (j.Site.Company != null && j.Site.Company.ParentCompanyId == companyId)));
        }
        if (assignedToMe && !string.IsNullOrEmpty(currentUserId))
            query = query.Where(j => j.Assignments.Any(a => a.UserId == currentUserId));
        if (siteId.HasValue)
            query = query.Where(j => j.SiteId == siteId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(j => j.Status == status.Trim());

        var searchTerm = search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(searchTerm))
        {
            query = query.Where(j =>
                (j.JobCardNumber != null && j.JobCardNumber.ToLower().Contains(searchTerm)) ||
                (j.ServiceRequest != null && j.ServiceRequest.RequestNumber != null && j.ServiceRequest.RequestNumber.ToLower().Contains(searchTerm)) ||
                (j.Site != null && j.Site.Name != null && j.Site.Name.ToLower().Contains(searchTerm)) ||
                j.Assignments.Any(a => (a.User != null && a.User.FullName != null && a.User.FullName.ToLower().Contains(searchTerm)) || (a.User != null && a.User.Email != null && a.User.Email.ToLower().Contains(searchTerm))));
        }

        var pageSizeClamped = Math.Clamp(pageSize, 1, 200);
        var skip = Math.Max(0, (Math.Max(1, page) - 1) * pageSizeClamped);

        List<JobCard> jobs;
        int total;
        var statusFilterActive = !string.IsNullOrWhiteSpace(status);
        if (statusFilterActive)
        {
            var ordered = query
                .OrderByDescending(j => j.Priority)
                .ThenBy(j => j.DueDate ?? DateTime.MaxValue)
                .ThenByDescending(j => j.CreatedAt);
            total = await ordered.CountAsync(ct);
            jobs = await ordered.Skip(skip).Take(pageSizeClamped).ToListAsync(ct);
        }
        else
        {
            // Keep operational queue behavior: non-completed by highest priority first.
            var openOrdered = query
                .Where(j => j.Status != JobCardStatus.Completed && j.Status != JobCardStatus.Done && j.Status != JobCardStatus.Closed)
                .OrderByDescending(j => j.Priority)
                .ThenBy(j => j.DueDate ?? DateTime.MaxValue)
                .ThenByDescending(j => j.CreatedAt);
            var openJobs = await openOrdered.ToListAsync(ct);

            // Completed jobs are always below open jobs; keep only the most recent 5.
            var completedOrdered = query
                .Where(j => j.Status == JobCardStatus.Completed || j.Status == JobCardStatus.Done || j.Status == JobCardStatus.Closed)
                .OrderByDescending(j => j.UpdatedAt ?? j.CreatedAt)
                .ThenByDescending(j => j.CreatedAt);
            var completedJobs = await completedOrdered.Take(5).ToListAsync(ct);

            var merged = openJobs.Concat(completedJobs).ToList();
            total = merged.Count;
            jobs = merged.Skip(skip).Take(pageSizeClamped).ToList();
        }
        var list = jobs.Select(j => new JobCardListDto
        {
            Id = j.Id,
            JobCardNumber = j.JobCardNumber,
            ServiceRequestId = j.ServiceRequestId,
            ServiceRequestNumber = j.ServiceRequest?.RequestNumber,
            CompanyId = j.Site?.CompanyId,
            SiteId = j.SiteId,
            SiteName = j.Site?.Name,
            Status = j.Status,
            Priority = j.Priority,
            DueDate = j.DueDate,
            CreatedAt = j.CreatedAt,
            AssignedTechnicianNames = j.Assignments.Any()
                ? string.Join(", ", j.Assignments.Select(a => a.User?.FullName ?? a.User?.Email ?? "").Where(x => !string.IsNullOrEmpty(x)))
                : null,
            BlockedReason = j.BlockedReason
        }).ToList();
        Response.Headers.Append("X-Total-Count", total.ToString());
        return Ok(list);
    }

    private static readonly string[] CompletedStatuses = { JobCardStatus.Completed, JobCardStatus.Done, JobCardStatus.Closed };

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(JobCardListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobCardListDto>> UpdateStatus(Guid id, [FromBody] UpdateJobCardStatusRequest request, CancellationToken ct = default)
    {
        if (await IsClientScopedUserAsync(ct))
            return Forbid();
        var j = await _db.JobCards
            .Include(x => x.PlannedParts)
            .Include(x => x.Site)
            .Include(x => x.Documents)
            .Include(x => x.Permits).ThenInclude(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (j == null)
            return NotFound();
        if (await PaidJobCardLockHelper.IsLockedAsync(_db, id, ct))
            return BadRequest(ApiResponseBodies.Message(PaidJobCardLockHelper.UserMessage));
        if (await WaAmendmentSignOffHelper.JobHasExpiredWorkAuthorisationStandstillAsync(_db, id, ct))
            return StatusCode(403, WaAmendmentSignOffHelper.WaExpiredStandstillMessage);
        if (!string.IsNullOrWhiteSpace(request.Status) && CompletedStatuses.Contains(request.Status.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var companyId = j.Site?.CompanyId;
            if (companyId.HasValue)
            {
                var budget = await _db.ClientBudgets.AsNoTracking().FirstOrDefaultAsync(b => b.CompanyId == companyId.Value, ct);
                if (budget != null && budget.ThresholdAmount > 0 && budget.WorkPaused)
                    return StatusCode(403, "Work is paused because budget threshold was exceeded. Client must approve continuation before completing the job.");
            }
        }

        var wasCompleted = CompletedStatuses.Contains(j.Status, StringComparer.OrdinalIgnoreCase);
        if (ValidateTerminalCompletionIfRequested(j, request.Status, wasCompleted) is ActionResult completionBlock)
            return completionBlock;

        if (!string.IsNullOrWhiteSpace(request.Status))
            j.Status = request.Status.Trim();
        var isNowCompleted = CompletedStatuses.Contains(j.Status, StringComparer.OrdinalIgnoreCase);
        if (isNowCompleted && !wasCompleted)
        {
            var (companyId, _) = await _currentUser.GetClientScopeAsync(ct);
            CloseWorkAuthorisationPermitsForCompletedJob(j);
            foreach (var jpp in j.PlannedParts)
            {
                var part = await _db.Parts.FindAsync([jpp.PartId], ct);
                if (part != null)
                    part.Quantity = Math.Max(0, part.Quantity - jpp.Quantity);
            }
            var jobNumber = j.JobCardNumber ?? id.ToString();
            await _notificationService.NotifyUsersWithPermissionAsync(
                "ViewJobCards",
                "Job completed",
                $"Job card {jobNumber} is completed and ready for invoice.",
                "JobCompleted",
                id.ToString(),
                excludeUserId: null,
                scopeCompanyId: companyId,
                ct);
        }
        j.UpdatedAt = DateTime.UtcNow;
        await _auditService.LogAsync("JobCardStatusChange", "JobCard", id.ToString(), $"Status: {j.Status}", ct);
        await _db.SaveChangesAsync(ct);

        if (j.PermitsRequired && WaAmendmentSignOffHelper.JobStatusIndicatesWorkStarted(j.Status))
        {
            var actingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            await WorkAuthorizationChildPermitSyncHelper.SyncJobChildPermitsFromWorkAuthorisationAsync(
                _db, id, _workAuthorizationPermitRules, _env, _auditService, actingUserId, ct);
        }

        await _realtimeHub.NotifyJobCardUpdatedAsync(id, ct);
        return await Get(id);
    }

    /// <summary>Block a job (e.g. client not on site, missing permits/parts). Technicians cannot open even if highest priority.</summary>
    [HttpPatch("{id:guid}/block")]
    [Authorize(Policy = "RequireAssignTechnicians")]
    [ProducesResponseType(typeof(JobCardListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobCardListDto>> Block(Guid id, [FromBody] BlockJobCardRequest request, CancellationToken ct = default)
    {
        if (await IsClientScopedUserAsync(ct))
            return Forbid();
        var j = await _db.JobCards.Include(x => x.Site).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (j == null) return NotFound();
        if (await PaidJobCardLockHelper.IsLockedAsync(_db, id, ct))
            return BadRequest(ApiResponseBodies.Message(PaidJobCardLockHelper.UserMessage));
        if (await WaAmendmentSignOffHelper.JobHasExpiredWorkAuthorisationStandstillAsync(_db, id, ct))
            return StatusCode(403, WaAmendmentSignOffHelper.WaExpiredStandstillMessage);
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (companyId.HasValue && j.Site != null)
        {
            var inScope = isClient ? j.Site.CompanyId == companyId : (j.Site.CompanyId == companyId || (j.Site.Company != null && j.Site.Company.ParentCompanyId == companyId));
            if (!inScope) return NotFound();
        }
        var reason = request?.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
            return BadRequest(new { message = "Block reason is required." });
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        j.BlockedReason = reason.Length > 500 ? reason[..500] : reason;
        j.BlockedAt = DateTime.UtcNow;
        j.BlockedByUserId = userId;
        j.UpdatedAt = DateTime.UtcNow;
        await _auditService.LogAsync("JobCardBlocked", "JobCard", id.ToString(), $"Reason: {reason}", ct);
        await _db.SaveChangesAsync(ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(id, ct);
        return await Get(id);
    }

    /// <summary>Clear block on a job (override). Requires AssignTechnicians.</summary>
    [HttpPatch("{id:guid}/unblock")]
    [Authorize(Policy = "RequireAssignTechnicians")]
    [ProducesResponseType(typeof(JobCardListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobCardListDto>> Unblock(Guid id, CancellationToken ct = default)
    {
        if (await IsClientScopedUserAsync(ct))
            return Forbid();
        var j = await _db.JobCards.Include(x => x.Site).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (j == null) return NotFound();
        if (await PaidJobCardLockHelper.IsLockedAsync(_db, id, ct))
            return BadRequest(ApiResponseBodies.Message(PaidJobCardLockHelper.UserMessage));
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (companyId.HasValue && j.Site != null)
        {
            var inScope = isClient ? j.Site.CompanyId == companyId : (j.Site.CompanyId == companyId || (j.Site.Company != null && j.Site.Company.ParentCompanyId == companyId));
            if (!inScope) return NotFound();
        }
        var previousReason = j.BlockedReason;
        j.BlockedReason = null;
        j.BlockedAt = null;
        j.BlockedByUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        j.UpdatedAt = DateTime.UtcNow;
        await _auditService.LogAsync("JobCardUnblocked", "JobCard", id.ToString(), $"Previous reason: {previousReason ?? "(none)"}", ct);
        await _db.SaveChangesAsync(ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(id, ct);
        return await Get(id);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(JobCardListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobCardListDto>> Get(Guid id, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var j = await _db.JobCards.AsNoTracking()
            .Include(j => j.Site)
            .ThenInclude(s => s!.Company)
            .Include(j => j.ServiceRequest)
            .Include(j => j.Assignments)
            .ThenInclude(a => a.User)
            .FirstOrDefaultAsync(j => j.Id == id, ct);
        if (j == null)
            return NotFound();
        if (companyId.HasValue)
        {
            if (j.Site == null)
                return NotFound();
            var inScope = isClient
                ? j.Site.CompanyId == companyId
                : (j.Site.CompanyId == companyId || (j.Site.Company != null && j.Site.Company.ParentCompanyId == companyId));
            if (!inScope)
                return NotFound();
        }
        return Ok(new JobCardListDto
        {
            Id = j.Id,
            JobCardNumber = j.JobCardNumber,
            ServiceRequestId = j.ServiceRequestId,
            ServiceRequestNumber = j.ServiceRequest?.RequestNumber,
            CompanyId = j.Site?.CompanyId,
            SiteId = j.SiteId,
            SiteName = j.Site?.Name,
            Status = j.Status,
            Priority = j.Priority,
            DueDate = j.DueDate,
            CreatedAt = j.CreatedAt,
            AssignedTechnicianNames = j.Assignments.Any()
                ? string.Join(", ", j.Assignments.Select(a => a.User?.FullName ?? a.User?.Email ?? "").Where(x => !string.IsNullOrEmpty(x)))
                : null,
            BlockedReason = j.BlockedReason
        });
    }

    [HttpPost]
    [Authorize(Policy = "RequireCreateJobCards")]
    [ProducesResponseType(typeof(JobCardListDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<JobCardListDto>> Create([FromBody] CreateJobCardRequest request, CancellationToken ct)
    {
        if (await IsClientScopedUserAsync(ct))
            return Forbid();
        if (request.ServiceRequestId.HasValue)
        {
            var sr = await _db.ServiceRequests.AsNoTracking().Include(s => s.Site).FirstOrDefaultAsync(s => s.Id == request.ServiceRequestId.Value, ct);
            if (sr == null)
                return BadRequest(ApiResponseBodies.Message("Service request not found."));
            if (sr.SiteId != request.SiteId)
                return BadRequest(ApiResponseBodies.Message("Site must match the service request's site."));
        }
        var site = await _db.Sites.AsNoTracking().Include(s => s!.Company).FirstOrDefaultAsync(s => s.Id == request.SiteId, ct);
        if (site == null)
            return BadRequest(ApiResponseBodies.Message("Site not found."));
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (companyId.HasValue)
        {
            var inScope = isClient ? site.CompanyId == companyId : site.Company?.ParentCompanyId == companyId;
            if (!inScope)
                return BadRequest(ApiResponseBodies.Message("Site not found."));
        }
        var status = string.IsNullOrWhiteSpace(request.Status) ? JobCardStatus.Open : request.Status.Trim();
        if (!string.Equals(status, JobCardStatus.Open, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status, JobCardStatus.Draft, StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponseBodies.Message("Status must be Open or Draft."));
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

        var jobCardNumber = NumberGenerator.NextJobCardNumber(_db.JobCards);
        var job = new JobCard
        {
            Id = Guid.NewGuid(),
            JobCardNumber = jobCardNumber,
            ServiceRequestId = request.ServiceRequestId,
            SiteId = request.SiteId,
            Status = status,
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow
        };
        _db.JobCards.Add(job);
        await _db.SaveChangesAsync(ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(job.Id, ct);

        var loaded = await _db.JobCards.AsNoTracking()
            .Include(j => j.Site)
            .Include(j => j.ServiceRequest)
            .FirstAsync(j => j.Id == job.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = job.Id }, new JobCardListDto
        {
            Id = loaded.Id,
            JobCardNumber = loaded.JobCardNumber,
            ServiceRequestId = loaded.ServiceRequestId,
            ServiceRequestNumber = loaded.ServiceRequest?.RequestNumber,
            CompanyId = loaded.Site?.CompanyId,
            SiteId = loaded.SiteId,
            SiteName = loaded.Site?.Name,
            Status = loaded.Status,
            Priority = loaded.Priority,
            DueDate = loaded.DueDate,
            CreatedAt = loaded.CreatedAt
        });
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(JobCardListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<JobCardListDto>> Update(Guid id, [FromBody] UpdateJobCardRequest request, CancellationToken ct = default)
    {
        if (await IsClientScopedUserAsync(ct))
            return Forbid();
        var j = await _db.JobCards
            .Include(x => x.Site).ThenInclude(s => s!.Company)
            .Include(x => x.PlannedParts)
            .Include(x => x.Documents)
            .Include(x => x.Permits).ThenInclude(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (j == null)
            return NotFound();
        if (await PaidJobCardLockHelper.IsLockedAsync(_db, id, ct))
            return BadRequest(ApiResponseBodies.Message(PaidJobCardLockHelper.UserMessage));
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (companyId.HasValue)
        {
            if (j.Site == null)
                return NotFound();
            var inScope = isClient
                ? j.Site.CompanyId == companyId
                : (j.Site.CompanyId == companyId || (j.Site.Company != null && j.Site.Company.ParentCompanyId == companyId));
            if (!inScope)
                return NotFound();
        }
        if (request.ServiceRequestId.HasValue)
        {
            var sr = await _db.ServiceRequests.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.ServiceRequestId.Value, ct);
            if (sr == null)
                return BadRequest(ApiResponseBodies.Message("Service request not found."));
            if (sr.SiteId != j.SiteId)
                return BadRequest(ApiResponseBodies.Message("Service request must be for the same site as the job card."));
            j.ServiceRequestId = request.ServiceRequestId;
        }
        var statusBeforePatch = j.Status;
        var wasCompleted = CompletedStatuses.Contains(j.Status, StringComparer.OrdinalIgnoreCase);
        if (ValidateTerminalCompletionIfRequested(j, request.Status, wasCompleted) is ActionResult completionBlock)
            return completionBlock;
        if (!string.IsNullOrWhiteSpace(request.Status))
            j.Status = request.Status.Trim();
        if (!string.Equals(statusBeforePatch, j.Status, StringComparison.Ordinal))
            await _auditService.LogAsync("JobCardStatusChange", "JobCard", id.ToString(), $"Status: {j.Status}", ct);
        var isNowCompleted = CompletedStatuses.Contains(j.Status, StringComparer.OrdinalIgnoreCase);
        if (isNowCompleted && !wasCompleted)
        {
            CloseWorkAuthorisationPermitsForCompletedJob(j);
            foreach (var jpp in j.PlannedParts)
            {
                var part = await _db.Parts.FindAsync([jpp.PartId], ct);
                if (part != null)
                    part.Quantity = Math.Max(0, part.Quantity - jpp.Quantity);
            }
        }
        if (request.Description != null)
            j.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (request.Priority.HasValue && request.Priority.Value >= 1 && request.Priority.Value <= 5)
            j.Priority = request.Priority.Value;
        if (request.DueDate.HasValue)
        {
            var dueDate = request.DueDate.Value.Date;
            if (dueDate < DateTime.UtcNow.Date)
                return BadRequest(ApiResponseBodies.Message("Due date cannot be in the past."));
            var quote = await _db.Quotes.AsNoTracking().FirstOrDefaultAsync(q => q.JobCardId == id, ct);
            if (quote?.ValidUntil.HasValue == true && quote.ValidUntil.Value.Date > dueDate)
                return BadRequest(ApiResponseBodies.Message("Quote valid-until date must be on or before the job due date."));
            j.DueDate = request.DueDate.Value;
        }
        else
            j.DueDate = null;
        if (request.PermitsRequired.HasValue)
        {
            j.PermitsRequired = request.PermitsRequired.Value;
            j.RequiredPermitTypeId = request.PermitsRequired.Value && request.RequiredPermitTypeId.HasValue ? request.RequiredPermitTypeId : null;
        }
        if (request.PartsRequired.HasValue)
            j.PartsRequired = request.PartsRequired.Value;
        if (request.PlannedParts != null)
        {
            var partsScopeCompanyId = j.Site?.CompanyId;
            var existing = await _db.JobCardPlannedParts.Where(jpp => jpp.JobCardId == id).ToListAsync(ct);
            _db.JobCardPlannedParts.RemoveRange(existing);
            foreach (var pp in request.PlannedParts)
            {
                if (pp.Quantity < 1) continue;
                if (!partsScopeCompanyId.HasValue) continue;
                var partInScope = await _db.Parts.AnyAsync(p => p.Id == pp.PartId && p.CompanyId == partsScopeCompanyId.Value, ct);
                if (!partInScope) continue;
                _db.JobCardPlannedParts.Add(new JobCardPlannedPart
                {
                    Id = Guid.NewGuid(),
                    JobCardId = id,
                    PartId = pp.PartId,
                    Quantity = pp.Quantity
                });
            }
        }
        if (request.ActiveJobPermitId.HasValue)
        {
            var permit = await _db.JobPermits
                .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
                .Include(p => p.Attachments)
                .FirstOrDefaultAsync(p => p.Id == request.ActiveJobPermitId.Value && p.JobCardId == id, ct);
            if (permit != null && await WaAmendmentSignOffHelper.JobHasPendingWaAmendmentAsync(_db, id, ct))
            {
                var isWa = permit.PermitTemplate?.PermitType?.IsWorkAuthorisation == true;
                if (!isWa && !WaAmendmentSignOffHelper.ChildPermitMayContinueDuringAmendment(permit, j, WaAmendmentSignOffHelper.HasClientSignOffForPermit(permit)))
                    return StatusCode(403, new { message = WaAmendmentSignOffHelper.WorkStandstillMessage });
            }
            j.ActiveJobPermitId = permit != null ? request.ActiveJobPermitId.Value : null;
        }
        j.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        if (j.PermitsRequired && WaAmendmentSignOffHelper.JobStatusIndicatesWorkStarted(j.Status))
        {
            var actingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            await WorkAuthorizationChildPermitSyncHelper.SyncJobChildPermitsFromWorkAuthorisationAsync(
                _db, id, _workAuthorizationPermitRules, _env, _auditService, actingUserId, ct);
        }
        await _realtimeHub.NotifyJobCardUpdatedAsync(id, ct);
        return await Get(id);
    }

    /// <summary>When moving into a terminal status from a non-terminal one, require final client sign-off and technician completion gates.</summary>
    private ActionResult? ValidateTerminalCompletionIfRequested(JobCard j, string? requestedStatus, bool wasAlreadyCompleted)
    {
        var trimmed = string.IsNullOrWhiteSpace(requestedStatus) ? null : requestedStatus.Trim();
        if (wasAlreadyCompleted || trimmed == null || !CompletedStatuses.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            return null;
        if (!JobCardFinalSignOffHelper.HasCapturedSignature(j.Documents))
            return BadRequest(new { message = "A captured client signature is required before marking the job complete." });
        var visible = PaperPermitModeHelper.VisiblePermits(j.Permits).ToList();
        if (!JobCardTechnicianCompletionGateHelper.TryValidate(
                j.Status,
                j.PermitsRequired,
                visible,
                j.Documents.ToList(),
                DateTime.UtcNow,
                out var gateError))
            return BadRequest(ApiResponseBodies.Message(gateError ?? "This job cannot be marked complete yet."));
        return null;
    }

    /// <summary>
    /// When a job reaches a terminal/completed status, normalize Work Authorisation permits to closed.
    /// This prevents completed jobs from still surfacing WA-expired standstill flags.
    /// </summary>
    private static void CloseWorkAuthorisationPermitsForCompletedJob(JobCard job)
    {
        foreach (var permit in job.Permits)
        {
            if (permit.PermitTemplate?.PermitType?.IsWorkAuthorisation != true)
                continue;
            if (PermitStatus.IsRejectedOrCancelled(permit.Status) || PermitStatus.IsClosedLike(permit.Status))
                continue;
            permit.Status = PermitStatus.Closed;
        }
    }

    private async Task<bool> IsClientScopedUserAsync(CancellationToken ct)
    {
        var (_, isClient) = await _currentUser.GetClientScopeAsync(ct);
        return isClient;
    }
}
