using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.DTOs.WorkAuthorizations;
using Ike.Api.Helpers;
using Ike.Api.Models;
using Ike.Api.Permits;
using PermitStatus = Ike.Api.Models.PermitStatus;
using Ike.Api.Services;

namespace Ike.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class JobPermitsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly IWebHostEnvironment _env;
    private readonly IAuditService _auditService;
    private readonly IRealtimeHub _realtimeHub;
    private readonly IWorkAuthorizationPermitRulesService _workAuthRules;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPermissionService _permissionService;
    private readonly IChildPermitDocumentationPdfRenderer _childPermitPdf;
    private const string PermitUploadFolder = "uploads/permits";
    private static readonly string[] AllowedExtensions = { ".pdf", ".png", ".jpg", ".jpeg" };
    private const int MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    /// <summary>
    /// Another Work Authorisation cannot be requested while an existing one is still in its validity window
    /// (including when marked Done/Closed). After ValidTo passes, the next calendar day a new master may be requested.
    /// </summary>
    private static bool IsWorkAuthorisationBlockingNewRequest(JobPermit p, DateTime utcNow)
    {
        if (p.PermitTemplate?.PermitType?.IsWorkAuthorisation != true) return false;
        var s = (p.Status ?? "").Trim();
        if (IsRejectedOrCancelledStatus(s)) return false;
        if (s.Equals(PermitStatus.Draft, StringComparison.OrdinalIgnoreCase)) return true;
        if (p.ValidTo.HasValue)
            return utcNow.Date <= p.ValidTo.Value.Date;
        return true;
    }

    /// <summary>
    /// Master can drive linked work permit requests only while it is draft/active-like and within validity.
    /// Closed/Done, Rejected/Cancelled, Expired-like, or past-validity masters cannot drive child requests.
    /// </summary>
    private static bool MasterPermitAllowsChildPermitRequests(JobPermit master, DateTime utcNow)
    {
        var s = (master.Status ?? "").Trim();
        if (PermitStatus.IsRejectedOrCancelled(s)) return false;
        if (PermitStatus.IsClosedLike(s)) return false;
        if (PermitStatus.IsExpiredLike(s)) return false;
        if (master.ValidTo.HasValue && master.ValidTo.Value < utcNow) return false;
        return true;
    }

    private static bool IsRejectedOrCancelledStatus(string status)
    {
        return PermitStatus.IsRejectedOrCancelled(status);
    }

    private static List<Guid>? ParsePermitTypeIdsJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var arr = System.Text.Json.JsonSerializer.Deserialize<string[]>(json);
            if (arr == null || arr.Length == 0) return null;
            var list = new List<Guid>();
            foreach (var s in arr)
                if (Guid.TryParse(s, out var g)) list.Add(g);
            return list.Count > 0 ? list : null;
        }
        catch
        {
            return null;
        }
    }

    public JobPermitsController(ApplicationDbContext db, ICurrentUserService currentUser, IEmailService emailService, IWebHostEnvironment env, IAuditService auditService, IRealtimeHub realtimeHub, IWorkAuthorizationPermitRulesService workAuthRules, UserManager<ApplicationUser> userManager, IPermissionService permissionService, IChildPermitDocumentationPdfRenderer childPermitPdf)
    {
        _db = db;
        _currentUser = currentUser;
        _emailService = emailService;
        _env = env;
        _auditService = auditService;
        _realtimeHub = realtimeHub;
        _workAuthRules = workAuthRules;
        _userManager = userManager;
        _permissionService = permissionService;
        _childPermitPdf = childPermitPdf;
    }

    /// <summary>
    /// True when this request will create a Work Authorisation (explicit type, job default, or implicit default-to-WA resolution).
    /// Used to allow replacement Work Authorisation requests during expired-WA standstill.
    /// </summary>
    private async Task<bool> RequestPermitBodyTargetsWorkAuthorisationAsync(RequestJobPermitRequest request, JobCard job, CancellationToken ct)
    {
        var companyId = job.Site?.CompanyId;
        var permitTypeId = request.PermitTypeId ?? job.RequiredPermitTypeId;
        if (permitTypeId.HasValue)
            return await _db.PermitTypes.AsNoTracking().AnyAsync(pt => pt.Id == permitTypeId.Value && pt.IsWorkAuthorisation, ct);

        var workAuthTypeId = await _db.PermitTypes
            .Where(pt => pt.IsActive && pt.IsWorkAuthorisation && (pt.CompanyId == null || pt.CompanyId == companyId))
            .OrderBy(pt => pt.CompanyId == null ? 1 : 0)
            .ThenBy(pt => pt.Name)
            .Select(pt => (Guid?)pt.Id)
            .FirstOrDefaultAsync(ct);
        return workAuthTypeId.HasValue;
    }

    /// <summary>Assigned to the job, or staff with AssignTechnicians (same as job-card document uploads).</summary>
    private async Task<bool> CanPerformTechnicianWorkOnJobAsync(Guid jobCardId, string userId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        if (await _db.JobCardAssignments.AnyAsync(a => a.JobCardId == jobCardId && a.UserId == userId, ct))
            return true;
        var user = await _userManager.FindByIdAsync(userId);
        return user != null && await _permissionService.HasPermissionAsync(user, "AssignTechnicians");
    }

    /// <summary>
    /// Request a permit for a job card. Creates a JobPermit with Pending status.
    /// The job must have PermitsRequired. PermitTypeId may come from the request, the job's RequiredPermitTypeId,
    /// or a fallback permit type for the company when digital permits are configured.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RequestPermit([FromBody] RequestJobPermitRequest request, CancellationToken ct)
    {
        if (request == null || request.JobCardId == Guid.Empty)
            return BadRequest(new { message = "JobCardId is required." });

        var job = await _db.JobCards
            .Include(j => j.Site).ThenInclude(s => s!.Company)
            .FirstOrDefaultAsync(j => j.Id == request.JobCardId, ct);
        if (job == null)
            return NotFound(new { message = "Job card not found." });
        if (!await CanAccessJobInScopeAsync(job.Id, ct))
            return NotFound(new { message = "Job card not found." });
        if (await PaidJobCardLockHelper.IsLockedAsync(_db, job.Id, ct))
            return BadRequest(new { message = PaidJobCardLockHelper.UserMessage });
        if (await WaAmendmentSignOffHelper.JobHasPendingWaAmendmentAsync(_db, job.Id, ct))
            return StatusCode(403, new { message = WaAmendmentSignOffHelper.RequestPermitBlockedMessage });
        // Expired-WA standstill blocks most actions, but requesting a replacement Work Authorisation is the intended recovery path.
        if (await WaAmendmentSignOffHelper.JobHasExpiredWorkAuthorisationStandstillAsync(_db, job.Id, ct)
            && !await RequestPermitBodyTargetsWorkAuthorisationAsync(request, job, ct))
            return StatusCode(403, new { message = WaAmendmentSignOffHelper.WaExpiredStandstillMessage });

        if (!job.PermitsRequired)
            return BadRequest(new { message = "This job does not require a permit." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var isPermitManager = await _db.JobCardAssignments
            .AnyAsync(a => a.JobCardId == job.Id && a.UserId == userId && a.IsPermitManager, ct);
        if (!isPermitManager)
            return Forbid();

        var permitTypeId = request.PermitTypeId ?? job.RequiredPermitTypeId;
        var companyId = job.Site?.CompanyId;

        // If no explicit type was requested, prioritize Work Authorisation (master) as default.
        if (!permitTypeId.HasValue)
        {
            var workAuthTypeId = await _db.PermitTypes
                .Where(pt => pt.IsActive && pt.IsWorkAuthorisation && (pt.CompanyId == null || pt.CompanyId == companyId))
                .OrderBy(pt => pt.CompanyId == null ? 1 : 0)
                .ThenBy(pt => pt.Name)
                .Select(pt => (Guid?)pt.Id)
                .FirstOrDefaultAsync(ct);
            if (workAuthTypeId.HasValue)
                permitTypeId = workAuthTypeId.Value;
        }

        // Bootstrap default master permit type/template if none exist yet.
        if (!permitTypeId.HasValue)
        {
            var childPermitTypeIds = await _db.PermitTypes
                .Where(pt => pt.IsActive && !pt.IsWorkAuthorisation && (pt.CompanyId == null || pt.CompanyId == companyId))
                .OrderBy(pt => pt.CompanyId == null ? 1 : 0)
                .ThenBy(pt => pt.Name)
                .Select(pt => pt.Id)
                .ToListAsync(ct);
            var defaultWorkAuthType = new PermitType
            {
                Id = Guid.NewGuid(),
                Name = "Work Authorisation",
                Description = "Master permit",
                IsWorkAuthorisation = true,
                IsActive = true,
                CompanyId = companyId,
                TriggersPermitTypeIdsJson = childPermitTypeIds.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(childPermitTypeIds.Select(g => g.ToString()).ToList())
                    : null
            };
            _db.PermitTypes.Add(defaultWorkAuthType);
            await _db.SaveChangesAsync(ct);
            permitTypeId = defaultWorkAuthType.Id;
        }

        if (!permitTypeId.HasValue && job.Site?.CompanyId != null)
        {
            var fallback = await _db.PermitTypes
                .Where(pt => pt.IsActive && (pt.CompanyId == null || pt.CompanyId == job.Site!.CompanyId))
                .OrderBy(pt => pt.CompanyId == null ? 1 : 0)
                .ThenBy(pt => pt.Name)
                .Select(pt => pt.Id)
                .FirstOrDefaultAsync(ct);
            if (fallback != Guid.Empty)
                permitTypeId = fallback;
        }
        if (!permitTypeId.HasValue)
            return BadRequest(new { message = "No permit type configured. Permit types will be defined when digital permits are added." });

        var utcNow = DateTime.UtcNow;

        if (request.MasterPermitId.HasValue)
        {
            var master = await _db.JobPermits
                .Include(p => p.PermitTemplate).ThenInclude(t => t.PermitType)
                .FirstOrDefaultAsync(p => p.Id == request.MasterPermitId.Value, ct);
            if (master == null)
                return NotFound(new { message = "Master permit not found." });
            if (master.HiddenFromUiForHistory)
                return BadRequest(new { message = "That permit is no longer active on this job." });
            if (master.JobCardId != job.Id)
                return BadRequest(new { message = "Master permit must belong to the same job." });
            if (!MasterPermitAllowsChildPermitRequests(master, utcNow))
                return BadRequest(new { message = "Work Authorisation must be valid (not rejected or cancelled, and not past its valid-to date) before requesting work permits." });
            var reqType = await _db.PermitTypes.AsNoTracking().FirstOrDefaultAsync(pt => pt.Id == permitTypeId.Value, ct);
            if (reqType != null && !reqType.IsWorkAuthorisation)
            {
                var masterType = await _db.PermitTemplates.AsNoTracking()
                    .Where(t => t.Id == master.PermitTemplateId)
                    .Select(t => t.PermitType.IsWorkAuthorisation)
                    .FirstOrDefaultAsync(ct);
                if (!masterType)
                    return BadRequest(new { message = "MasterPermitId must reference a Work Authorisation permit." });

                if (!job.PaperPermitMode)
                {
                    var allJobPermits = await _db.JobPermits
                        .Include(x => x.PermitTemplate).ThenInclude(t => t.PermitType)
                        .Where(x => x.JobCardId == job.Id && !x.HiddenFromUiForHistory)
                        .ToListAsync(ct);
                    var masterFull = allJobPermits.FirstOrDefault(x => x.Id == request.MasterPermitId!.Value);
                    if (masterFull == null)
                        return NotFound(new { message = "Master permit not found on this job." });
                    var scopedPermitTypes = await WorkAuthorizationRequestablePermitsHelper
                        .ActiveChildPermitTypesInSiteScope(_db.PermitTypes.AsNoTracking(), companyId, job.Site?.Company)
                        .ToListAsync(ct);
                    var fromChecklist = WorkAuthorizationRequestablePermitsHelper.IsPermitTypeRequestableForMaster(
                        permitTypeId.Value, masterFull, allJobPermits, scopedPermitTypes, utcNow, _workAuthRules);
                    if (!fromChecklist)
                    {
                        var legacyTriggers = ParsePermitTypeIdsJson(masterFull.PermitTemplate?.PermitType?.TriggersPermitTypeIdsJson);
                        if (legacyTriggers == null || !legacyTriggers.Contains(permitTypeId.Value))
                            return BadRequest(new { message = "That work permit is not indicated by the saved Work Authorisation, already has an open row for this master, or was already marked done." });
                    }
                }
            }
        }

        // Reuse an existing draft child for this master+type instead of creating duplicates (e.g. after tapping Request twice).
        if (request.MasterPermitId.HasValue && permitTypeId.HasValue)
        {
            var typeRow = await _db.PermitTypes.AsNoTracking().FirstOrDefaultAsync(pt => pt.Id == permitTypeId.Value, ct);
            if (typeRow != null && !typeRow.IsWorkAuthorisation)
            {
                var existingDraft = await _db.JobPermits
                    .Include(p => p.PermitTemplate)
                    .FirstOrDefaultAsync(p => p.JobCardId == job.Id
                        && p.MasterPermitId == request.MasterPermitId
                        && p.PermitTemplate != null && p.PermitTemplate.PermitTypeId == permitTypeId.Value
                        && p.Status == PermitStatus.Draft, ct);
                if (existingDraft != null)
                {
                    await _realtimeHub.NotifyJobCardUpdatedAsync(job.Id, ct);
                    return CreatedAtAction(null, new { id = existingDraft.Id });
                }
            }
        }

        // Same permit type can only be requested again the next day (IKE: "same permit only next day")
        var todayUtc = DateTime.UtcNow.Date;
        var sameTypeApprovedToday = await _db.JobPermits
            .Include(p => p.PermitTemplate)
            .Where(p => p.JobCardId == job.Id && !p.HiddenFromUiForHistory && p.PermitTemplate.PermitTypeId == permitTypeId.Value)
            .Where(p => p.Status == PermitStatus.Active || p.Status == PermitStatus.Approved)
            .Where(p => (p.ApprovedAt ?? p.RequestedAt).Date == todayUtc)
            .AnyAsync(ct);
        if (sameTypeApprovedToday)
            return BadRequest(new { message = "Same permit type can only be requested again the next day. Need more work? Request tomorrow." });

        var template = await _db.PermitTemplates
            .Where(pt => pt.PermitTypeId == permitTypeId.Value && pt.IsActive)
            .OrderBy(pt => pt.SiteId == null ? 0 : 1)
            .ThenBy(pt => pt.CompanyId == null ? 0 : 1)
            .FirstOrDefaultAsync(ct);
        if (template == null)
        {
            var permitType = await _db.PermitTypes.FindAsync([permitTypeId.Value], ct);
            if (permitType == null)
                return BadRequest(new { message = "Permit type not found." });
            template = new PermitTemplate
            {
                Id = Guid.NewGuid(),
                PermitTypeId = permitType.Id,
                Name = permitType.Name,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.PermitTemplates.Add(template);
            await _db.SaveChangesAsync(ct);
        }

        var requestedPermitType = await _db.PermitTypes.AsNoTracking().FirstOrDefaultAsync(pt => pt.Id == permitTypeId.Value, ct);

        JobPermit? childPredecessorForCopy = null;
        if (requestedPermitType != null && !requestedPermitType.IsWorkAuthorisation)
        {
            childPredecessorForCopy = await _db.JobPermits
                .AsNoTracking()
                .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
                .Where(p => p.JobCardId == job.Id && !p.HiddenFromUiForHistory && p.PermitTemplate != null && p.PermitTemplate.PermitTypeId == permitTypeId.Value)
                .OrderByDescending(p => p.PermitNumber)
                .FirstOrDefaultAsync(ct);
        }

        if (requestedPermitType != null && !requestedPermitType.IsWorkAuthorisation)
        {
            var waPermits = await _db.JobPermits
                .Include(p => p.PermitTemplate).ThenInclude(t => t.PermitType)
                .Where(p => p.JobCardId == job.Id && !p.HiddenFromUiForHistory && p.PermitTemplate.PermitType.IsWorkAuthorisation)
                .ToListAsync(ct);
            if (!waPermits.Any(p => MasterPermitAllowsChildPermitRequests(p, utcNow)))
                return BadRequest(new { message = "A valid Work Authorisation is required before requesting other permits." });
        }
        else if (requestedPermitType?.IsWorkAuthorisation == true)
        {
            var waPermits = await _db.JobPermits
                .Include(p => p.PermitTemplate).ThenInclude(t => t.PermitType)
                .Where(p => p.JobCardId == job.Id && !p.HiddenFromUiForHistory && p.PermitTemplate.PermitType.IsWorkAuthorisation)
                .ToListAsync(ct);
            if (waPermits.Any(p => IsWorkAuthorisationBlockingNewRequest(p, utcNow)))
                return BadRequest(new { message = "A Work Authorisation already exists for this job until its validity ends. After the expiry date, you can request a new one from the following calendar day." });
        }

        JobPermit? waPredecessorForReplacement = null;
        if (requestedPermitType?.IsWorkAuthorisation == true)
        {
            waPredecessorForReplacement = await _db.JobPermits
                .AsNoTracking()
                .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
                .Where(p => p.JobCardId == job.Id && !p.HiddenFromUiForHistory && p.PermitTemplate != null && p.PermitTemplate.PermitType.IsWorkAuthorisation)
                .OrderByDescending(p => p.PermitNumber)
                .FirstOrDefaultAsync(ct);
        }

        var clientCompanyId = job.Site?.CompanyId;
        var permitNumbersQuery = _db.JobPermits
            .Where(p => p.PermitTemplate.PermitTypeId == permitTypeId.Value);
        if (clientCompanyId.HasValue)
        {
            permitNumbersQuery = permitNumbersQuery.Where(p => p.JobCard.Site.CompanyId == clientCompanyId.Value);
        }
        var maxPermitNumber = await permitNumbersQuery
            .Select(p => (int?)p.PermitNumber)
            .MaxAsync(ct) ?? 0;

        var permit = new JobPermit
        {
            Id = Guid.NewGuid(),
            PermitNumber = maxPermitNumber + 1,
            JobCardId = job.Id,
            PermitTemplateId = template.Id,
            Status = PermitStatus.Draft,
            RequestedAt = DateTime.UtcNow,
            RequestedByUserId = userId,
            MasterPermitId = request.MasterPermitId
        };

        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            _db.JobPermits.Add(permit);
            await _db.SaveChangesAsync(ct);

            var waReplacementApplied = false;
            if (requestedPermitType?.IsWorkAuthorisation == true
                && waPredecessorForReplacement != null
                && waPredecessorForReplacement.Id != permit.Id
                && !IsWorkAuthorisationBlockingNewRequest(waPredecessorForReplacement, utcNow))
            {
                PermitRolloverHelper.ApplyReplacementDraftFromPredecessorWa(permit, waPredecessorForReplacement);
                await PermitRolloverHelper.RelinkChildPermitsFromExpiredWaToNewAsync(_db, waPredecessorForReplacement.Id, permit.Id, utcNow, ct);
                waReplacementApplied = true;
            }

            if (requestedPermitType != null && !requestedPermitType.IsWorkAuthorisation && childPredecessorForCopy != null)
                PermitRolloverHelper.CopyChildDraftFromPredecessorIfExpired(permit, childPredecessorForCopy, utcNow);

            if (requestedPermitType?.IsWorkAuthorisation != true)
            {
                if (job.PaperPermitMode && permit.MasterPermitId.HasValue)
                {
                    var exists = await _db.JobPermitMasterLinks.AnyAsync(x => x.MasterPermitId == permit.MasterPermitId.Value && x.ChildPermitId == permit.Id, ct);
                    if (!exists)
                        _db.JobPermitMasterLinks.Add(new JobPermitMasterLink { MasterPermitId = permit.MasterPermitId.Value, ChildPermitId = permit.Id, LinkedAt = DateTime.UtcNow });
                }
                else if (!job.PaperPermitMode)
                {
                    var waIds = await _db.JobPermits
                        .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
                        .Where(p => p.JobCardId == job.Id && !p.HiddenFromUiForHistory && p.PermitTemplate != null && p.PermitTemplate.PermitType.IsWorkAuthorisation)
                        .Select(p => p.Id)
                        .ToListAsync(ct);
                    foreach (var waId in waIds.Distinct())
                    {
                        var exists = await _db.JobPermitMasterLinks.AnyAsync(x => x.MasterPermitId == waId && x.ChildPermitId == permit.Id, ct);
                        if (!exists)
                            _db.JobPermitMasterLinks.Add(new JobPermitMasterLink { MasterPermitId = waId, ChildPermitId = permit.Id, LinkedAt = DateTime.UtcNow });
                    }
                }

                await _db.SaveChangesAsync(ct);
            }
            else if (waReplacementApplied)
                await _db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        }

        await _realtimeHub.NotifyJobCardUpdatedAsync(job.Id, ct);

        return CreatedAtAction(null, new { id = permit.Id });
    }

    /// <summary>
    /// Upload a permit document (from client). Multiple files allowed per permit.
    /// </summary>
    [HttpPost("{id:guid}/upload")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UploadAttachment(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided." });
        if (file.Length > MaxFileSizeBytes)
            return BadRequest(new { message = "File too large (max 10 MB)." });
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            return BadRequest(new { message = "Allowed types: PDF, PNG, JPG." });
        var sigErr = await FileContentSignatureHelper.ValidateContentMatchesExtensionAsync(file, ext, ct);
        if (sigErr != null)
            return BadRequest(new { message = sigErr });

        var permit = await _db.JobPermits
            .Include(p => p.JobCard)
            .ThenInclude(j => j!.Site).ThenInclude(s => s!.Company)
            .Include(p => p.Attachments)
            .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (permit == null)
            return NotFound();
        if (!await CanAccessJobInScopeAsync(permit.JobCardId, ct))
            return NotFound();
        if (await PaidJobCardLockHelper.IsLockedAsync(_db, permit.JobCardId, ct))
            return BadRequest(new { message = PaidJobCardLockHelper.UserMessage });
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (permit.JobCard?.PaperPermitMode == true)
        {
            var u = await _userManager.FindByIdAsync(userId);
            if (u == null || !await _permissionService.HasPermissionAsync(u, "AssignTechnicians"))
                return StatusCode(403, new { message = "Permit file uploads for paper jobs are only available to staff with Assign Technicians (use the web console)." });
        }
        else if (!await CanPerformTechnicianWorkOnJobAsync(permit.JobCardId, userId, ct))
            return Forbid();

        var isWa = permit.PermitTemplate?.PermitType?.IsWorkAuthorisation == true;
        var currentStatusForStandstill = (permit.Status ?? "").Trim();
        var wouldActivateFromUpload = !PermitStatus.IsActiveLike(currentStatusForStandstill);
        if (await WaAmendmentSignOffHelper.JobHasPendingWaAmendmentAsync(_db, permit.JobCardId, ct)
            && !isWa
            && wouldActivateFromUpload)
            return StatusCode(403, new { message = WaAmendmentSignOffHelper.PermitActionBlockedMessage });
        if (await WaAmendmentSignOffHelper.JobHasExpiredWorkAuthorisationStandstillAsync(_db, permit.JobCardId, ct)
            && !isWa)
            return StatusCode(403, new { message = WaAmendmentSignOffHelper.WaExpiredStandstillMessage });

        var dir = Path.Combine(_env.ContentRootPath, PermitUploadFolder);
        Directory.CreateDirectory(dir);
        var attachmentId = Guid.NewGuid();
        var fileName = attachmentId.ToString("N") + ext;
        var relativePath = PermitUploadFolder + "/" + fileName;
        var fullPath = Path.Combine(dir, fileName);
        await using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(stream, ct);
        }

        var attachment = new JobPermitAttachment
        {
            Id = attachmentId,
            JobPermitId = permit.Id,
            FileName = file.FileName,
            FilePath = relativePath,
            UploadedAt = DateTime.UtcNow,
            UploadedByUserId = userId
        };
        _db.JobPermitAttachments.Add(attachment);

            var currentStatus = (permit.Status ?? "").Trim();
            if (!PermitStatus.IsActiveLike(currentStatus))
            {
                var fromStatus = PermitStatus.Draft;
                if (string.Equals(currentStatus, PermitStatus.Signed, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(currentStatus, PermitStatus.Captured, StringComparison.OrdinalIgnoreCase))
                    fromStatus = currentStatus;
                if (!PermitStatus.CanTransition(fromStatus, PermitStatus.Active))
                    return BadRequest(new { message = "Permit cannot transition to Active from current state." });
            permit.Status = PermitStatus.Active;
            permit.ApprovedAt = DateTime.UtcNow;
            permit.ApprovedByUserId = userId;
            permit.ValidFrom = DateTime.UtcNow;
            // Default validity: 12 hours (half day). Can be overridden by ValidityRulesJson on template.
            var templateLoaded = await _db.PermitTemplates.AsNoTracking()
                .Include(t => t.PermitType)
                .FirstOrDefaultAsync(t => t.Id == permit.PermitTemplateId, ct);
            var durationHours = PermitTemplateDurationHelper.ResolvePermitDurationHours(templateLoaded);
            var calculatedValidTo = DateTime.UtcNow.AddHours(durationHours);
            if (permit.MasterPermitId.HasValue)
            {
                var masterValidTo = await _db.JobPermits
                    .Where(mp => mp.Id == permit.MasterPermitId.Value)
                    .Select(mp => mp.ValidTo)
                    .FirstOrDefaultAsync(ct);
                if (masterValidTo.HasValue && masterValidTo.Value < calculatedValidTo)
                    calculatedValidTo = masterValidTo.Value;
            }
            permit.ValidTo = calculatedValidTo;
            await _auditService.LogAsync("PermitApproved", "JobPermit", id.ToString(), $"JobCard: {permit.JobCardId}", ct);
        }

        if (isWa)
        {
            var waDto = WaAmendmentSignOffHelper.DeserializeWaPayload(permit.ChecklistSnapshotJson);
            var allNames = permit.Attachments.Select(a => a.FileName).Append(file.FileName);
            if (WaAmendmentSignOffHelper.WaPayloadIndicatesClientSignOff(waDto, allNames))
            {
                if (waDto == null)
                {
                    var waNum = $"WA-{DateTime.UtcNow:yyyyMMdd}-{permit.Id.ToString("N")[..6]}";
                    waDto = WorkAuthorizationMasterPermitDefaults.CreateTemplate(permit.Id, waNum);
                }
                permit.WaSignedBusinessContentHash = WaAmendmentSignOffHelper.ComputeBusinessContentHash(waDto);
                permit.PendingWaAmendmentSignOff = false;
            }
        }

        await _db.SaveChangesAsync(ct);
        if (isWa)
        {
            var actingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            await WorkAuthorizationChildPermitSyncHelper.SyncJobChildPermitsFromWorkAuthorisationAsync(
                _db, permit.JobCardId, _workAuthRules, _env, _auditService, actingUserId, ct);
        }
        await _realtimeHub.NotifyJobCardUpdatedAsync(permit.JobCardId, ct);
        return NoContent();
    }

    /// <summary>
    /// Get permits expiring within the given hours for a job card. Used for "need more work?" flow.
    /// </summary>
    [HttpGet("expiring")]
    [ProducesResponseType(typeof(List<ExpiringPermitDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ExpiringPermitDto>>> GetExpiringPermits([FromQuery] Guid jobCardId, [FromQuery] int hours = 24, CancellationToken ct = default)
    {
        if (jobCardId == Guid.Empty)
            return BadRequest(new { message = "jobCardId is required." });
        if (!await CanAccessJobInScopeAsync(jobCardId, ct))
            return NotFound();
        var cutoff = DateTime.UtcNow.AddHours(hours);
        var list = await _db.JobPermits.AsNoTracking()
            .Where(p => p.JobCardId == jobCardId
                && !p.HiddenFromUiForHistory
                && (p.Status == PermitStatus.Active || p.Status == PermitStatus.Approved)
                && p.ValidTo.HasValue && p.ValidTo.Value <= cutoff && p.ValidTo.Value >= DateTime.UtcNow)
            .Select(p => new ExpiringPermitDto
            {
                Id = p.Id,
                PermitTemplateName = p.PermitTemplate != null
                    ? (p.PermitTemplate.PermitType != null ? p.PermitTemplate.PermitType.Name : p.PermitTemplate.Name) ?? ""
                    : "",
                ValidTo = p.ValidTo!.Value
            })
            .ToListAsync(ct);
        return Ok(list);
    }

    /// <summary>
    /// Download a permit attachment.
    /// </summary>
    [HttpGet("attachments/{attachmentId:guid}")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttachment(Guid attachmentId, CancellationToken ct)
    {
        var att = await _db.JobPermitAttachments.AsNoTracking()
            .Include(a => a.JobPermit)
            .ThenInclude(p => p.JobCard)
            .ThenInclude(j => j.Site)
            .ThenInclude(s => s!.Company)
            .FirstOrDefaultAsync(a => a.Id == attachmentId, ct);
        if (att == null)
            return NotFound();
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (companyId.HasValue)
        {
            var site = att.JobPermit?.JobCard?.Site;
            if (site == null) return NotFound();
            var inScope = isClient
                ? site.CompanyId == companyId
                : (site.CompanyId == companyId || (site.Company != null && site.Company.ParentCompanyId == companyId));
            if (!inScope) return NotFound();
        }
        var validatedPath = Ike.Api.Helpers.FilePathHelper.ValidateAndNormalize(att.FilePath);
        if (validatedPath == null)
            return NotFound();
        var fullPath = Path.Combine(_env.ContentRootPath, validatedPath);
        if (!System.IO.File.Exists(fullPath))
            return NotFound();
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var contentType = Path.GetExtension(att.FileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
        return File(stream, contentType, att.FileName);
    }

    /// <summary>
    /// Download a single PDF combining permit form, checklist, file list, and embedded client signature (non–Work Authorisation permits).
    /// </summary>
    [HttpGet("{id:guid}/documentation-pdf")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocumentationPdf(Guid id, CancellationToken ct = default)
    {
        var permit = await _db.JobPermits.AsNoTracking()
            .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .Include(p => p.JobCard).ThenInclude(j => j!.Site).ThenInclude(s => s!.Company)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (permit == null)
            return NotFound();
        if (permit.PermitTemplate?.PermitType?.IsWorkAuthorisation == true)
            return BadRequest(new { message = "Use Work Authorisation → Download PDF for the master permit." });

        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (companyId.HasValue)
        {
            var site = permit.JobCard?.Site;
            if (site == null) return NotFound();
            var inScope = isClient
                ? site.CompanyId == companyId
                : (site.CompanyId == companyId || (site.Company != null && site.Company.ParentCompanyId == companyId));
            if (!inScope) return NotFound();
        }

        var bytes = await _childPermitPdf.RenderAsync(id, ct);
        if (bytes == null || bytes.Length == 0)
            return NotFound();
        var typeName = PermitTemplateDurationHelper.PrimaryDisplayName(permit.PermitTemplate) ?? "permit";
        var safe = string.Concat(typeName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safe)) safe = "permit";
        return File(bytes, "application/pdf", $"{safe}-documentation.pdf");
    }

    /// <summary>Email client signed permit files (work permits, not the WA master — use work-authorizations email for master).</summary>
    [HttpPost("{id:guid}/email-client")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> EmailPermitDocumentationToClient(Guid id, CancellationToken ct)
    {
        var permit = await _db.JobPermits
            .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .Include(p => p.Attachments)
            .Include(p => p.JobCard)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (permit == null)
            return NotFound(new { message = "Permit not found." });
        if (permit.PermitTemplate?.PermitType?.IsWorkAuthorisation == true)
            return BadRequest(new { message = "Use Work Authorisation → Email client for the master permit." });
        if (await PaidJobCardLockHelper.IsLockedAsync(_db, permit.JobCardId, ct))
            return BadRequest(new { message = PaidJobCardLockHelper.UserMessage });
        if (await WaAmendmentSignOffHelper.JobHasPendingWaAmendmentAsync(_db, permit.JobCardId, ct)
            && !WaAmendmentSignOffHelper.ChildPermitMayContinueDuringAmendment(permit, permit.JobCard!, WaAmendmentSignOffHelper.HasClientSignOffForPermit(permit)))
            return StatusCode(403, new { message = WaAmendmentSignOffHelper.PermitActionBlockedMessage });
        if (await WaAmendmentSignOffHelper.JobHasExpiredWorkAuthorisationStandstillAsync(_db, permit.JobCardId, ct))
            return StatusCode(403, new { message = WaAmendmentSignOffHelper.WaExpiredStandstillMessage });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var user = await _userManager.FindByIdAsync(userId);
        var isPermitManager = await _db.JobCardAssignments
            .AnyAsync(a => a.JobCardId == permit.JobCardId && a.UserId == userId && a.IsPermitManager, ct);
        var canAssign = user != null && await _permissionService.HasPermissionAsync(user, "AssignTechnicians");
        if (!isPermitManager && !canAssign)
            return Forbid();

        var s = (permit.Status ?? "").Trim();
        var hasSignOff = PermitStatus.IsActiveLike(s)
            || permit.PaperClientSignedOffAt.HasValue
            || permit.Attachments.Any(a => (a.FileName ?? "").Contains("signature", StringComparison.OrdinalIgnoreCase));
        if (!hasSignOff)
            return BadRequest(new { message = "Client sign-off is required before emailing permit documentation." });

        var sent = await _emailService.SendPermitDocumentationPackageToClientAsync(id, null, null, null, ct);
        if (!sent)
            return BadRequest(ApiResponseBodies.Message("Permit email was not sent. Check that the client email, SMTP settings, and permit document files are available."));
        return NoContent();
    }

    /// <summary>
    /// Submit Work Authorisation checklist for a permit. Items are stored in ChecklistSnapshotJson.
    /// </summary>
    [HttpPatch("{id:guid}/checklist")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SubmitChecklist(Guid id, [FromBody] SubmitPermitChecklistRequest request, CancellationToken ct)
    {
        var permit = await _db.JobPermits
            .Include(p => p.JobCard)
            .ThenInclude(j => j!.Site).ThenInclude(s => s!.Company)
            .Include(p => p.Attachments)
            .Include(p => p.PermitTemplate).ThenInclude(t => t.PermitType)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (permit == null)
            return NotFound();
        if (!await CanAccessJobInScopeAsync(permit.JobCardId, ct))
            return NotFound();
        if (await PaidJobCardLockHelper.IsLockedAsync(_db, permit.JobCardId, ct))
            return BadRequest(new { message = PaidJobCardLockHelper.UserMessage });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var isPermitManager = await _db.JobCardAssignments
            .AnyAsync(a => a.JobCardId == permit.JobCardId && a.UserId == userId && a.IsPermitManager, ct);
        if (!isPermitManager)
            return Forbid();

        if (permit.PermitTemplate?.PermitType?.IsWorkAuthorisation == true)
            return BadRequest(new { message = "Use Work Authorisation endpoints for the master permit." });

        if (permit.JobCard?.PaperPermitMode == true)
            return BadRequest(new { message = "This job uses paper permits; use paper permit actions instead of the digital checklist." });

        if (await WaAmendmentSignOffHelper.JobHasPendingWaAmendmentAsync(_db, permit.JobCardId, ct)
            && !WaAmendmentSignOffHelper.ChildPermitMayContinueDuringAmendment(permit, permit.JobCard!, WaAmendmentSignOffHelper.HasClientSignOffForPermit(permit)))
            return StatusCode(403, new { message = WaAmendmentSignOffHelper.PermitActionBlockedMessage });
        if (await WaAmendmentSignOffHelper.JobHasExpiredWorkAuthorisationStandstillAsync(_db, permit.JobCardId, ct))
            return StatusCode(403, new { message = WaAmendmentSignOffHelper.WaExpiredStandstillMessage });

        var hasChecklistTemplate = PermitFormJsonHelper.ParseChecklistTemplate(permit.PermitTemplate!.ChecklistJson).Count > 0;
        var hasFormSchema = PermitFormJsonHelper.ParseSchema(permit.PermitTemplate.FormSchemaJson).Count > 0;
        var itemsEmpty = request?.Items == null || request.Items.Count == 0;
        var formEmpty = request?.Form == null || request.Form.Count == 0;

        if (itemsEmpty && formEmpty)
        {
            permit.ChecklistSnapshotJson = null;
            permit.FormSnapshotJson = null;
        }
        else
        {
            request ??= new SubmitPermitChecklistRequest();
            if (!TryValidateChildPermitChecklistAndForm(permit.PermitTemplate, request, out var validationError))
                return BadRequest(new { message = validationError });

            if (hasChecklistTemplate)
            {
                var snapshot = request.Items!.Select(i => new { id = i.Id ?? "", label = i.Label ?? "", @checked = i.Checked }).ToList();
                permit.ChecklistSnapshotJson = JsonSerializer.Serialize(snapshot);
            }
            else
                permit.ChecklistSnapshotJson = null;

            permit.FormSnapshotJson = hasFormSchema ? PermitFormJsonHelper.SerializeValues(request.Form) : null;
            var currentStatus = (permit.Status ?? "").Trim();
            if (PermitStatus.CanTransition(currentStatus, PermitStatus.Captured))
                permit.Status = PermitStatus.Captured;
        }

        await _db.SaveChangesAsync(ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(permit.JobCardId, ct);
        return NoContent();
    }

    /// <summary>All template checklist lines must be checked; all required structured fields must be filled.</summary>
    private static bool TryValidateChildPermitChecklistAndForm(
        PermitTemplate template,
        SubmitPermitChecklistRequest request,
        out string errorMessage)
    {
        errorMessage = "";
        var checklistPairs = PermitFormJsonHelper.ParseChecklistTemplate(template.ChecklistJson);
        if (checklistPairs.Count > 0)
        {
            if (request.Items == null || request.Items.Count == 0)
            {
                errorMessage = "Safety commitments are required for this permit.";
                return false;
            }

            foreach (var t in checklistPairs)
            {
                var match = request.Items.FirstOrDefault(i =>
                    string.Equals(i.Id, t.Id, StringComparison.OrdinalIgnoreCase));
                if (match == null || !match.Checked)
                {
                    errorMessage = "All safety commitments must be acknowledged before sign-off.";
                    return false;
                }
            }
        }

        var schema = PermitFormJsonHelper.ParseSchema(template.FormSchemaJson);
        var form = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (request.Form != null)
        {
            foreach (var kv in request.Form)
                form[kv.Key] = kv.Value;
        }

        foreach (var f in schema.Where(x => x.Required))
        {
            form.TryGetValue(f.Id, out var v);
            var type = (f.Type ?? "text").Trim();
            if (type.Equals(PermitFormFieldDefinition.TypeBool, StringComparison.OrdinalIgnoreCase))
            {
                if (!PermitFormJsonHelper.IsTruthy(v))
                {
                    errorMessage = $"Confirm or fill required field: {f.Label}";
                    return false;
                }
            }
            else if (string.IsNullOrWhiteSpace(v))
            {
                errorMessage = $"Required field missing: {f.Label}";
                return false;
            }
        }

        return true;
    }

    /// <summary>Paper mode: set or update the physical permit reference (required before client paper sign-off).</summary>
    [HttpPatch("{id:guid}/paper-number")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SetPaperPermitNumber(Guid id, [FromBody] SetPaperPermitNumberRequest? request, CancellationToken ct)
    {
        var permit = await _db.JobPermits
            .Include(p => p.JobCard)
            .ThenInclude(j => j!.Site).ThenInclude(s => s!.Company)
            .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (permit == null) return NotFound();
        if (!await CanAccessJobInScopeAsync(permit.JobCardId, ct))
            return NotFound();
        if (permit.HiddenFromUiForHistory)
            return BadRequest(new { message = "This permit is archived." });
        if (permit.JobCard?.PaperPermitMode != true)
            return BadRequest(new { message = "Paper permit number can only be set when the job is in paper permit mode." });
        if (await PaidJobCardLockHelper.IsLockedAsync(_db, permit.JobCardId, ct))
            return BadRequest(new { message = PaidJobCardLockHelper.UserMessage });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var isPermitManager = await _db.JobCardAssignments
            .AnyAsync(a => a.JobCardId == permit.JobCardId && a.UserId == userId && a.IsPermitManager, ct);
        if (!isPermitManager) return Forbid();

        if (permit.PaperClientSignedOffAt.HasValue)
            return BadRequest(new { message = "The paper permit number cannot be changed after client sign-off." });

        var raw = request?.PaperPermitNumber?.Trim() ?? "";
        if (raw.Length > PaperPermitModeHelper.PaperPermitNumberMaxLength)
            return BadRequest(new { message = $"Paper permit number must be at most {PaperPermitModeHelper.PaperPermitNumberMaxLength} characters." });
        permit.PaperPermitNumber = string.IsNullOrEmpty(raw) ? null : raw;
        await _auditService.LogAsync("PaperPermitNumberSet", "JobPermit", id.ToString(), "Paper permit reference updated", ct);
        await _db.SaveChangesAsync(ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(permit.JobCardId, ct);
        return NoContent();
    }

    /// <summary>Paper mode: record that the client signed the paper permit; activates the permit (same validity rules as file upload).</summary>
    [HttpPatch("{id:guid}/paper-client-sign-off")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> PaperClientSignOff(Guid id, CancellationToken ct)
    {
        var permit = await _db.JobPermits
            .Include(p => p.JobCard)
            .ThenInclude(j => j!.Site).ThenInclude(s => s!.Company)
            .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (permit == null) return NotFound();
        if (!await CanAccessJobInScopeAsync(permit.JobCardId, ct))
            return NotFound();
        if (permit.HiddenFromUiForHistory)
            return BadRequest(new { message = "This permit is archived." });
        if (permit.JobCard?.PaperPermitMode != true)
            return BadRequest(new { message = "This action is only for jobs in paper permit mode." });
        if (await PaidJobCardLockHelper.IsLockedAsync(_db, permit.JobCardId, ct))
            return BadRequest(new { message = PaidJobCardLockHelper.UserMessage });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var isPermitManager = await _db.JobCardAssignments
            .AnyAsync(a => a.JobCardId == permit.JobCardId && a.UserId == userId && a.IsPermitManager, ct);
        if (!isPermitManager) return Forbid();

        if (permit.PaperClientSignedOffAt.HasValue)
            return NoContent();

        if (string.IsNullOrWhiteSpace(permit.PaperPermitNumber))
            return BadRequest(new { message = "Enter the paper permit number before recording client sign-off." });

        var currentStatus = (permit.Status ?? "").Trim();
        if (!PermitStatus.IsActiveLike(currentStatus))
        {
            var fromStatus = PermitStatus.Draft;
            if (string.Equals(currentStatus, PermitStatus.Signed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(currentStatus, PermitStatus.Captured, StringComparison.OrdinalIgnoreCase))
                fromStatus = currentStatus;
            if (!PermitStatus.CanTransition(fromStatus, PermitStatus.Active))
                return BadRequest(new { message = "Permit cannot be activated from its current state." });
        }

        var utcNow = DateTime.UtcNow;
        permit.PaperClientSignedOffAt = utcNow;
        permit.PaperClientSignedOffByUserId = string.IsNullOrEmpty(userId) ? null : userId;

        if (!await TryActivatePermitAfterPaperClientSignOffAsync(permit, userId, ct))
            return BadRequest(new { message = "Permit cannot be activated from its current state." });

        var isWa = permit.PermitTemplate?.PermitType?.IsWorkAuthorisation == true;
        if (isWa)
            permit.PendingWaAmendmentSignOff = false;

        await _db.SaveChangesAsync(ct);
        if (isWa)
        {
            var actingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            await WorkAuthorizationChildPermitSyncHelper.SyncJobChildPermitsFromWorkAuthorisationAsync(
                _db, permit.JobCardId, _workAuthRules, _env, _auditService, actingUserId, ct);
        }

        await _realtimeHub.NotifyJobCardUpdatedAsync(permit.JobCardId, ct);
        return NoContent();
    }

    private async Task<bool> TryActivatePermitAfterPaperClientSignOffAsync(JobPermit permit, string userId, CancellationToken ct)
    {
        var currentStatus = (permit.Status ?? "").Trim();
        if (PermitStatus.IsActiveLike(currentStatus))
            return true;

        var fromStatus = PermitStatus.Draft;
        if (string.Equals(currentStatus, PermitStatus.Signed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentStatus, PermitStatus.Captured, StringComparison.OrdinalIgnoreCase))
            fromStatus = currentStatus;
        if (!PermitStatus.CanTransition(fromStatus, PermitStatus.Active))
            return false;

        permit.Status = PermitStatus.Active;
        permit.ApprovedAt = DateTime.UtcNow;
        permit.ApprovedByUserId = string.IsNullOrEmpty(userId) ? null : userId;
        permit.ValidFrom = DateTime.UtcNow;
        var templateLoaded = await _db.PermitTemplates.AsNoTracking()
            .Include(t => t.PermitType)
            .FirstOrDefaultAsync(t => t.Id == permit.PermitTemplateId, ct);
        var durationHours = PermitTemplateDurationHelper.ResolvePermitDurationHours(templateLoaded);
        var calculatedValidTo = DateTime.UtcNow.AddHours(durationHours);
        if (permit.MasterPermitId.HasValue)
        {
            var masterValidTo = await _db.JobPermits
                .Where(mp => mp.Id == permit.MasterPermitId.Value)
                .Select(mp => mp.ValidTo)
                .FirstOrDefaultAsync(ct);
            if (masterValidTo.HasValue && masterValidTo.Value < calculatedValidTo)
                calculatedValidTo = masterValidTo.Value;
        }

        permit.ValidTo = calculatedValidTo;
        await _auditService.LogAsync("PermitApproved", "JobPermit", permit.Id.ToString(), $"Paper client sign-off; JobCard: {permit.JobCardId}", ct);
        return true;
    }

    /// <summary>Update permit status (e.g. Close). Valid transitions per state machine: Draft→Closed, Signed→Closed, Active→Closed, Expired→Closed.</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UpdateStatus(Guid id, [FromBody] UpdatePermitStatusRequest request, CancellationToken ct)
    {
        var permit = await _db.JobPermits
            .Include(p => p.JobCard)
            .ThenInclude(j => j!.Site).ThenInclude(s => s!.Company)
            .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .Include(p => p.Attachments)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (permit == null) return NotFound();
        if (!await CanAccessJobInScopeAsync(permit.JobCardId, ct))
            return NotFound();
        if (await PaidJobCardLockHelper.IsLockedAsync(_db, permit.JobCardId, ct))
            return BadRequest(new { message = PaidJobCardLockHelper.UserMessage });
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var isPermitManager = await _db.JobCardAssignments
            .AnyAsync(a => a.JobCardId == permit.JobCardId && a.UserId == userId && a.IsPermitManager, ct);
        if (!isPermitManager) return Forbid();
        var newStatus = request?.Status?.Trim();
        if (string.IsNullOrWhiteSpace(newStatus)) return BadRequest(new { message = "Status is required." });
        var current = (permit.Status ?? "").Trim();
        if (await WaAmendmentSignOffHelper.JobHasPendingWaAmendmentAsync(_db, permit.JobCardId, ct))
        {
            var activating = PermitStatus.IsActiveLike(newStatus)
                && !PermitStatus.IsActiveLike(current);
            if (activating)
                return StatusCode(403, new { message = WaAmendmentSignOffHelper.PermitActionBlockedMessage });
        }
        if (await WaAmendmentSignOffHelper.JobHasExpiredWorkAuthorisationStandstillAsync(_db, permit.JobCardId, ct))
            return StatusCode(403, new { message = WaAmendmentSignOffHelper.WaExpiredStandstillMessage });
        if (!PermitStatus.CanTransition(current, newStatus))
            return BadRequest(new { message = $"Cannot transition from {current} to {newStatus}." });
        permit.Status = newStatus;
        await _auditService.LogAsync("PermitStatusChange", "JobPermit", id.ToString(), $"Status: {newStatus}", ct);
        await _db.SaveChangesAsync(ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(permit.JobCardId, ct);
        return NoContent();
    }

    /// <summary>
    /// Deletes child permits only when they are no longer required by the saved Work Authorisation checklist (unsigned drafts are removed by sync).
    /// Draft Work Authorisation rows cannot be deleted here.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeletePermit(Guid id, CancellationToken ct)
    {
        var permit = await _db.JobPermits
            .Include(p => p.Attachments)
            .Include(p => p.JobCard).ThenInclude(j => j!.Site).ThenInclude(s => s!.Company)
            .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (permit == null)
            return NotFound();
        if (!await CanAccessJobInScopeAsync(permit.JobCardId, ct))
            return NotFound();
        if (await PaidJobCardLockHelper.IsLockedAsync(_db, permit.JobCardId, ct))
            return BadRequest(new { message = PaidJobCardLockHelper.UserMessage });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var isPermitManager = await _db.JobCardAssignments
            .AnyAsync(a => a.JobCardId == permit.JobCardId && a.UserId == userId && a.IsPermitManager, ct);
        if (!isPermitManager)
            return Forbid();

        var isWa = permit.PermitTemplate?.PermitType?.IsWorkAuthorisation == true;
        var st = (permit.Status ?? "").Trim();

        if (isWa)
            return BadRequest(new { message = "Work Authorisation permits cannot be deleted with this action." });

        if (!permit.MasterPermitId.HasValue)
            return BadRequest(new { message = "This permit cannot be deleted with this action." });

        if (permit.JobCard?.PaperPermitMode == true)
        {
            await DeleteSingleChildPermitAsync(permit, ct);
            return NoContent();
        }

        var master = await _db.JobPermits
            .Include(m => m.PermitTemplate).ThenInclude(t => t!.PermitType)
            .FirstOrDefaultAsync(m => m.Id == permit.MasterPermitId.Value, ct);
        if (master == null || master.PermitTemplate?.PermitType?.IsWorkAuthorisation != true)
            return BadRequest(new { message = "Child permit must be linked to a Work Authorisation." });

        var companyId = permit.JobCard?.Site?.CompanyId;
        var scoped = await WorkAuthorizationRequestablePermitsHelper
            .ActiveChildPermitTypesInSiteScope(_db.PermitTypes.AsNoTracking(), companyId, permit.JobCard?.Site?.Company)
            .ToListAsync(ct);
        var typeId = permit.PermitTemplate!.PermitTypeId;
        var stillRequired = WorkAuthorizationRequestablePermitsHelper.IsPermitTypeRequiredByMasterChecklist(
            master, typeId, scoped, _workAuthRules);
        if (!stillRequired)
        {
            if (string.Equals(st, PermitStatus.Draft, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Draft permits are removed automatically when they are no longer required by the Work Authorisation." });

            if (string.Equals(st, PermitStatus.Captured, StringComparison.OrdinalIgnoreCase)
                || string.Equals(st, PermitStatus.Signed, StringComparison.OrdinalIgnoreCase)
                || PermitStatus.IsActiveLike(st))
            {
                if (PermitStatus.IsActiveLike(st)
                    && !WaAmendmentSignOffHelper.HasClientSignOffForPermit(permit))
                    return BadRequest(new { message = "Only permits with client sign-off can be removed when they are no longer required by the Work Authorisation." });
                await DeleteSingleChildPermitAsync(permit, ct);
                return NoContent();
            }

            return BadRequest(new { message = "This permit cannot be deleted in its current state." });
        }

        return BadRequest(new { message = "This permit is still required by the Work Authorisation." });
    }

    private async Task DeleteSingleChildPermitAsync(JobPermit permit, CancellationToken ct)
    {
        TryDeletePermitAttachmentFiles(_env, permit.Attachments);
        var jobCard = permit.JobCard;
        if (jobCard != null && jobCard.ActiveJobPermitId == permit.Id)
            jobCard.ActiveJobPermitId = null;
        _db.JobPermits.Remove(permit);
        await _auditService.LogAsync("PermitDeleted", "JobPermit", permit.Id.ToString(), "Child permit deleted", ct);
        await _db.SaveChangesAsync(ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(permit.JobCardId, ct);
    }

    private static void TryDeletePermitAttachmentFiles(IWebHostEnvironment env, IEnumerable<JobPermitAttachment> attachments)
    {
        foreach (var att in attachments)
        {
            var rel = FilePathHelper.ValidateAndNormalize(att.FilePath);
            if (rel == null) continue;
            var fullPath = Path.Combine(env.ContentRootPath, rel);
            try
            {
                if (System.IO.File.Exists(fullPath))
                    System.IO.File.Delete(fullPath);
            }
            catch
            {
                // best-effort
            }
        }
    }

    private async Task<bool> CanAccessJobInScopeAsync(Guid jobCardId, CancellationToken ct)
    {
        var job = await _db.JobCards.AsNoTracking()
            .Include(j => j.Site).ThenInclude(s => s!.Company)
            .FirstOrDefaultAsync(j => j.Id == jobCardId, ct);
        if (job?.Site == null) return false;
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (!companyId.HasValue) return true;
        return isClient
            ? job.Site.CompanyId == companyId
            : (job.Site.CompanyId == companyId || (job.Site.Company != null && job.Site.Company.ParentCompanyId == companyId));
    }

}

public class UpdatePermitStatusRequest
{
    public string? Status { get; set; }
}

public class SetPaperPermitNumberRequest
{
    public string? PaperPermitNumber { get; set; }
}

public class SubmitPermitChecklistRequest
{
    public List<ChecklistItemRequest> Items { get; set; } = new();

    /// <summary>Structured permit form values (field id → value). Required fields come from the permit template form schema.</summary>
    public Dictionary<string, string?>? Form { get; set; }
}

public class ChecklistItemRequest
{
    public string? Id { get; set; }
    public string? Label { get; set; }
    public bool Checked { get; set; }
}

public class RequestJobPermitRequest
{
    public Guid JobCardId { get; set; }
    /// <summary>Optional. When job has no RequiredPermitTypeId, the API may use a fallback permit type for the company.</summary>
    public Guid? PermitTypeId { get; set; }
    /// <summary>Optional. When set, links this permit to a master (Work Authorisation) permit. The master must be Approved.</summary>
    public Guid? MasterPermitId { get; set; }
}

public class ExpiringPermitDto
{
    public Guid Id { get; set; }
    public string PermitTemplateName { get; set; } = string.Empty;
    public DateTime ValidTo { get; set; }
}
