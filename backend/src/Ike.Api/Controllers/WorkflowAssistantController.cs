using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.Helpers;
using Ike.Api.Models;
using Ike.Api.Services;

namespace Ike.Api.Controllers;

[ApiController]
[Route("api/workflow-assistant")]
[Authorize]
public class WorkflowAssistantController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IScopeGuardService _scopeGuard;

    public WorkflowAssistantController(ApplicationDbContext db, IScopeGuardService scopeGuard)
    {
        _db = db;
        _scopeGuard = scopeGuard;
    }

    [HttpGet("job-cards/{id:guid}/next-actions")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(JobWorkflowNextActionsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobWorkflowNextActionsDto>> GetJobCardNextActions(Guid id, CancellationToken ct = default)
    {
        if (!await _scopeGuard.CanAccessJobCardAsync(id, ct))
            return NotFound();

        var job = await _db.JobCards.AsNoTracking()
            .Include(j => j.Permits).ThenInclude(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .Include(j => j.Documents)
            .FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job == null)
            return NotFound();

        var actions = new List<WorkflowActionDto>();
        var visiblePermits = PaperPermitModeHelper.VisiblePermits(job.Permits).ToList();

        if (await PaidJobCardLockHelper.IsLockedAsync(_db, id, ct))
        {
            actions.Add(new WorkflowActionDto
            {
                Key = "job-paid-lock",
                Priority = 1,
                Title = "Job is locked",
                Reason = "The linked invoice is paid, so operational changes are blocked.",
                RecommendedAction = "No further workflow action is required."
            });
        }

        if (await WaAmendmentSignOffHelper.JobHasExpiredWorkAuthorisationStandstillAsync(_db, id, ct))
        {
            actions.Add(new WorkflowActionDto
            {
                Key = "wa-replacement-required",
                Priority = 2,
                Title = "Work Authorisation replacement required",
                Reason = "A Work Authorisation has expired, so normal permit actions are in standstill.",
                RecommendedAction = "Request a replacement Work Authorisation to continue."
            });
        }

        if (await WaAmendmentSignOffHelper.JobHasPendingWaAmendmentAsync(_db, id, ct))
        {
            actions.Add(new WorkflowActionDto
            {
                Key = "wa-amendment-signoff-required",
                Priority = 3,
                Title = "Updated Work Authorisation needs client sign-off",
                Reason = "The Work Authorisation was changed after sign-off and is waiting for fresh client acknowledgement.",
                RecommendedAction = "Capture new client sign-off on the Work Authorisation."
            });
        }

        if (job.PermitsRequired && visiblePermits.Count == 0)
        {
            actions.Add(new WorkflowActionDto
            {
                Key = "permit-required",
                Priority = 4,
                Title = "Permit required",
                Reason = "This job requires permits but none are currently present.",
                RecommendedAction = "Request the required permit before proceeding."
            });
        }

        if (JobCardStatus.IsInProgressLike(job.Status) && !JobCardFinalSignOffHelper.HasCapturedSignature(job.Documents))
        {
            actions.Add(new WorkflowActionDto
            {
                Key = "final-client-signoff-missing",
                Priority = 5,
                Title = "Final client sign-off pending",
                Reason = "Job is in progress and final client sign-off is not yet captured.",
                RecommendedAction = "Capture final client sign-off signature in technician flow."
            });
        }

        if (job.PermitsRequired && visiblePermits.Any(p =>
                PermitStatus.IsActiveLike(p.Status) &&
                p.ValidTo.HasValue &&
                p.ValidTo.Value < DateTime.UtcNow))
        {
            actions.Add(new WorkflowActionDto
            {
                Key = "expired-active-permit",
                Priority = 6,
                Title = "Expired active permit detected",
                Reason = "At least one active permit has expired, which blocks compliant completion.",
                RecommendedAction = "Request replacement/continuation permit action before completion."
            });
        }

        if (actions.Count == 0)
        {
            actions.Add(new WorkflowActionDto
            {
                Key = "no-action",
                Priority = 99,
                Title = "No critical actions",
                Reason = "No blocking workflow condition detected.",
                RecommendedAction = "Continue normal job execution."
            });
        }

        return Ok(new JobWorkflowNextActionsDto
        {
            JobCardId = job.Id,
            JobCardStatus = job.Status,
            Actions = actions.OrderBy(a => a.Priority).ToList()
        });
    }
}

public class JobWorkflowNextActionsDto
{
    public Guid JobCardId { get; set; }
    public string JobCardStatus { get; set; } = string.Empty;
    public List<WorkflowActionDto> Actions { get; set; } = new();
}

public class WorkflowActionDto
{
    public string Key { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
}
