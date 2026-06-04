using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.DTOs.JobCardWork;
using Ike.Api.Helpers;
using Ike.Api.Models;
using Ike.Api.Services;

namespace Ike.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class JobCardWorkController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly INotificationService _notificationService;
    private readonly IAuditService _auditService;
    private readonly IRealtimeHub _realtimeHub;
    private readonly IPermissionService _permissionService;
    private readonly IWorkAuthorizationPermitRulesService _workAuthRules;
    private readonly IScopeGuardService _scopeGuard;
    private const string DocUploadFolder = "uploads/job-documents";
    private const string PartPhotoFolder = "uploads/part-photos";
    private const string IncidentPhotoFolder = "uploads/incident-photos";
    private static readonly string[] AllowedDocumentExtensions = { ".pdf", ".png", ".jpg", ".jpeg" };
    private static readonly string[] AllowedSignatureImageExtensions = { ".png", ".jpg", ".jpeg" };
    private const int MaxFileSizeBytes = 10 * 1024 * 1024;

    public JobCardWorkController(ApplicationDbContext db, ICurrentUserService currentUser, UserManager<ApplicationUser> userManager, IWebHostEnvironment env, INotificationService notificationService, IAuditService auditService, IRealtimeHub realtimeHub, IPermissionService permissionService, IWorkAuthorizationPermitRulesService workAuthRules, IScopeGuardService scopeGuard)
    {
        _db = db;
        _currentUser = currentUser;
        _userManager = userManager;
        _env = env;
        _notificationService = notificationService;
        _auditService = auditService;
        _realtimeHub = realtimeHub;
        _permissionService = permissionService;
        _workAuthRules = workAuthRules;
        _scopeGuard = scopeGuard;
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(JobCardWorkDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobCardWorkDto>> Get(Guid id, CancellationToken ct = default)
    {
        var actingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await PermitRolloverHelper.EnsureLazyRolloverOnReadAsync(_db, id, actingUserId, ct);
        await EnsurePlannedPartsSyncedFromRelatedQuotesAsync(id, ct);

        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        // Multiple collection Includes default to one SQL query (cartesian product) and can time out; split into several queries.
        var job = await _db.JobCards.AsNoTracking()
            .AsSplitQuery()
            .Include(j => j.Site).ThenInclude(s => s!.Company)
            .Include(j => j.ServiceRequest)
            .Include(j => j.RequiredPermitType)
            .Include(j => j.ActiveJobPermit).ThenInclude(p => p!.PermitTemplate).ThenInclude(t => t!.PermitType)
            .Include(j => j.Permits).ThenInclude(p => p.PermitTemplate!.PermitType)
            .Include(j => j.Permits).ThenInclude(p => p.Attachments)
            .Include(j => j.Documents).ThenInclude(d => d.SignedByUser)
            .Include(j => j.Documents).ThenInclude(d => d.PurchaseOrder)
            .Include(j => j.Parts)
            .Include(j => j.PlannedParts).ThenInclude(jpp => jpp.Part)
            .Include(j => j.IncidentReports)
            .Include(j => j.Assignments).ThenInclude(a => a.User)
            .FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job == null)
            return NotFound();
        if (companyId.HasValue && job.Site != null)
        {
            var inScope = isClient
                ? job.Site.CompanyId == companyId
                : (job.Site.CompanyId == companyId || (job.Site.Company != null && job.Site.Company.ParentCompanyId == companyId));
            if (!inScope)
                return NotFound();
        }

        var invoice = await _db.Invoices.AsNoTracking()
            .Where(i => i.JobCardId == id)
            .Select(i => new { i.Id, i.InvoiceNumber, i.Status })
            .FirstOrDefaultAsync(ct);
        var latestSignOffEvidence = await _db.JobCardSignOffEvidenceRecords.AsNoTracking()
            .Where(x => x.JobCardId == id)
            .OrderByDescending(x => x.CapturedAtUtc)
            .FirstOrDefaultAsync(ct);
        (Guid Id, string? QuoteNumber, decimal Amount, string Description, string Status)? quote = null;
        var byJobCard = await _db.Quotes.AsNoTracking()
            .Where(qu => qu.JobCardId == id)
            .Select(qu => new { qu.Id, qu.QuoteNumber, qu.Amount, qu.Description, qu.Status })
            .FirstOrDefaultAsync(ct);
        if (byJobCard != null)
            quote = (byJobCard.Id, byJobCard.QuoteNumber, byJobCard.Amount, byJobCard.Description ?? "", byJobCard.Status);
        else if (job.ServiceRequestId.HasValue)
        {
            var q = await _db.Quotes.AsNoTracking()
                .Where(qu => qu.ServiceRequestId == job.ServiceRequestId)
                .Select(qu => new { qu.Id, qu.QuoteNumber, qu.Amount, qu.Description, qu.Status })
                .FirstOrDefaultAsync(ct);
            if (q != null) quote = (q.Id, q.QuoteNumber, q.Amount, q.Description ?? "", q.Status);
        }
        var linkedPos = await _db.PurchaseOrders.AsNoTracking()
            .Where(po => po.JobCardId == id)
            .Select(po => new LinkedPoDto { Id = po.Id, PONumber = po.PONumber, ClientPONumber = po.ClientPONumber })
            .ToListAsync(ct);
        var visiblePermitsOrdered = job.PermitsRequired
            ? PaperPermitModeHelper.VisiblePermits(job.Permits).OrderByDescending(p => p.PermitNumber).ToList()
            : new List<JobPermit>();
        var allPermitsForWaBuilder = visiblePermitsOrdered;
        var allTriggerIds = visiblePermitsOrdered
            .SelectMany(p => ParsePermitTypeIdsJson(p.PermitTemplate?.PermitType?.TriggersPermitTypeIdsJson) ?? Enumerable.Empty<Guid>())
            .Distinct()
            .ToList();
        var permitTypeNameLookup = allTriggerIds.Count > 0
            ? await _db.PermitTypes.AsNoTracking()
                .Where(pt => allTriggerIds.Contains(pt.Id))
                .ToDictionaryAsync(pt => pt.Id, pt => pt.Name ?? "", ct)
            : new Dictionary<Guid, string>();
        var companyIdForPermits = job.Site?.CompanyId;
        var scopedPermitTypesForWa = await WorkAuthorizationRequestablePermitsHelper
            .ActiveChildPermitTypesInSiteScope(_db.PermitTypes.AsNoTracking(), companyIdForPermits, job.Site?.Company)
            .ToListAsync(ct);
        var allPermitsOnJob = allPermitsForWaBuilder;
        var utcNowForPermitOptions = DateTime.UtcNow;
        var requestDescription = job.ServiceRequest != null ? job.ServiceRequest.Description : null;
        var userId = _currentUser.UserId;
        var isTechnician = false;
        var waExpiredStandstill = await WaAmendmentSignOffHelper.JobHasExpiredWorkAuthorisationStandstillAsync(_db, id, ct);
        if (!string.IsNullOrEmpty(userId))
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                var roles = await _userManager.GetRolesAsync(user);
                isTechnician = roles.Contains(SeedData.RoleTechnician);
            }
        }
        var dto = new JobCardWorkDto
        {
            Id = job.Id,
            JobCardNumber = job.JobCardNumber,
            CreatedAt = job.CreatedAt,
            Description = job.Description,
            ServiceRequestId = job.ServiceRequestId,
            ServiceRequestNumber = job.ServiceRequest?.RequestNumber,
            ServiceRequestDescription = requestDescription,
            QuoteId = quote.HasValue ? quote.Value.Id : null,
            QuoteNumber = quote?.QuoteNumber,
            QuoteAmount = isTechnician ? null : quote?.Amount,
            QuoteDescription = quote?.Description,
            QuoteStatus = quote.HasValue ? quote.Value.Status : null,
            SiteId = job.SiteId,
            SiteName = job.Site?.Name,
            SiteAddress = job.Site?.Address,
            SiteLatitude = job.Site?.Latitude,
            SiteLongitude = job.Site?.Longitude,
            RequiredBadgeIds = new List<Guid>(),
            RequiredBadgeNames = new List<string>(),
            Status = job.Status,
            Priority = job.Priority,
            DueDate = job.DueDate,
            StartedAt = await _db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "JobCard" && a.EntityId == id.ToString() && a.Action == "JobCardStatusChange" && a.Details != null
                    && (EF.Functions.Like(a.Details, "%" + JobCardStatus.InProgress + "%") || EF.Functions.Like(a.Details, "%" + JobCardStatus.InProgressCompact + "%")))
                .OrderBy(a => a.CreatedAt)
                .Select(a => (DateTime?)a.CreatedAt)
                .FirstOrDefaultAsync(ct),
            CompletedAt = await _db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "JobCard" && a.EntityId == id.ToString() && a.Action == "JobCardStatusChange" && a.Details != null
                    && (a.Details.Contains(JobCardStatus.Completed) || a.Details.Contains(JobCardStatus.Done) || a.Details.Contains(JobCardStatus.Closed)))
                .OrderBy(a => a.CreatedAt)
                .Select(a => (DateTime?)a.CreatedAt)
                .FirstOrDefaultAsync(ct),
            FirstPermitRequestedAt = job.PermitsRequired
                ? visiblePermitsOrdered.OrderBy(p => p.RequestedAt).Select(p => (DateTime?)p.RequestedAt).FirstOrDefault()
                : null,
            FirstPermitApprovedAt = job.PermitsRequired
                ? visiblePermitsOrdered.Where(p => p.ApprovedAt.HasValue).OrderBy(p => p.ApprovedAt).Select(p => p.ApprovedAt).FirstOrDefault()
                : null,
            FirstSitePhotoAt = job.Documents
                .Where(d => d.DocumentType == "BeforeWork" || d.DocumentType == "MidWork" || d.DocumentType == "AfterWork")
                .OrderBy(d => d.SignedAt)
                .Select(d => (DateTime?)d.SignedAt)
                .FirstOrDefault(),
            LinkedPOs = linkedPos,
            InvoiceId = isTechnician ? null : invoice?.Id,
            InvoiceNumber = isTechnician ? null : invoice?.InvoiceNumber,
            InvoiceStatus = isTechnician ? null : invoice?.Status,
            Documents = job.Documents.Select(d => new JobCardDocumentDto
            {
                Id = d.Id,
                DocumentType = d.DocumentType,
                SignedAt = d.SignedAt,
                SignedByUserName = d.SignedByUser?.FullName ?? d.SignedByUser?.Email,
                Notes = d.Notes,
                PurchaseOrderId = d.PurchaseOrderId,
                PurchaseOrderNumber = d.PurchaseOrder?.PONumber,
                FilePath = d.FilePath
            }).ToList(),
            Parts = job.Parts.Select(p => new JobPartDto
            {
                Id = p.Id,
                Brand = p.Brand,
                SerialNumber = p.SerialNumber,
                Description = p.Description,
                OldPartPhotoPath = p.OldPartPhotoPath,
                NewPartPhotoPath = p.NewPartPhotoPath
            }).ToList(),
            Permits = BuildJobPermitDtos(
                job,
                visiblePermitsOrdered,
                allPermitsOnJob,
                scopedPermitTypesForWa,
                utcNowForPermitOptions,
                permitTypeNameLookup,
                _workAuthRules,
                job.PaperPermitMode),
            IncidentReports = job.IncidentReports.Select(ir => new IncidentReportDto
            {
                Id = ir.Id,
                Description = ir.Description,
                Severity = ir.Severity,
                Status = string.IsNullOrEmpty(ir.Status) ? IncidentStatus.Open : ir.Status,
                Resolution = ir.Resolution,
                PhotoPaths = ParsePhotosJson(ir.PhotosJson),
                CreatedAt = ir.CreatedAt
            }).ToList(),
            CompanyId = job.Site?.CompanyId,
            PermitsRequired = job.PermitsRequired,
            RequiredPermitTypeId = job.RequiredPermitTypeId,
            RequiredPermitTypeName = job.RequiredPermitType?.Name,
            RequiredPermitTypeIdsFromEquipment = new List<Guid>(),
            PartsRequired = job.PartsRequired,
            Assignments = await BuildAssignmentsWithBadgesAsync(job.Assignments.ToList(), ct),
            PlannedParts = job.PlannedParts.Select(jpp => new PlannedPartDto
            {
                Id = jpp.Id,
                PartId = jpp.PartId,
                PartName = jpp.Part?.Name ?? "",
                Quantity = jpp.Quantity,
                StockQuantity = jpp.Part?.Quantity ?? 0,
                ReorderLevel = jpp.Part?.ReorderLevel ?? 0
            }).ToList(),
            ActivePermitId = job.PermitsRequired && job.ActiveJobPermitId.HasValue && job.Permits.Any(p => p.Id == job.ActiveJobPermitId.Value && !p.HiddenFromUiForHistory)
                ? job.ActiveJobPermitId
                : null,
            ActivePermitName = job.PermitsRequired && job.ActiveJobPermitId.HasValue && job.Permits.Any(p => p.Id == job.ActiveJobPermitId.Value && !p.HiddenFromUiForHistory)
                ? PermitTemplateDurationHelper.PrimaryDisplayName(job.ActiveJobPermit?.PermitTemplate)
                : null,
            BlockedReason = job.BlockedReason,
            PendingWaAmendmentSignOff = job.PermitsRequired && visiblePermitsOrdered.Any(p => p.PendingWaAmendmentSignOff),
            WaExpiredStandstill = job.PermitsRequired && waExpiredStandstill,
            PaperPermitMode = job.PermitsRequired && job.PaperPermitMode,
            CanActivatePaperPermitMode = job.PermitsRequired
                && PaperPermitModeHelper.CanActivatePaperPermitMode(job)
                && !string.IsNullOrEmpty(userId)
                && await PaperPermitModeHelper.UserMayActivatePaperPermitModeAsync(_db, job.Id, userId!, _userManager, _permissionService, ct),
            Budget = await GetBudgetForJobAsync(job, ct),
            FinalClientSignOffAt = job.FinalClientSignOffAt
                ?? job.Documents
                    .Where(d => string.Equals(d.DocumentType, JobCardFinalSignOffHelper.DocumentType, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(d => d.SignedAt)
                    .Select(d => (DateTime?)d.SignedAt)
                    .FirstOrDefault(),
            FinalClientSignOffByName = string.IsNullOrEmpty(job.FinalClientSignOffByUserId)
                ? null
                : (await _userManager.FindByIdAsync(job.FinalClientSignOffByUserId)) is { } signOffUser
                    ? signOffUser.FullName ?? signOffUser.Email
                    : null,
            FinalClientSignerName = job.Documents
                .Where(d => string.Equals(d.DocumentType, JobCardFinalSignOffHelper.DocumentType, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.SignedAt)
                .Select(d => string.IsNullOrWhiteSpace(d.Notes) ? null : d.Notes.Trim())
                .FirstOrDefault(),
            FinalClientSignOffFileSha256 = job.FinalClientSignOffFileSha256,
            FinalClientSignOffCaptureSource = job.FinalClientSignOffCaptureSource,
            FinalClientSignOffEvidenceHash = latestSignOffEvidence?.EvidenceHash ?? job.FinalClientSignOffEvidenceHash,
            FinalClientSignOffEvidenceRecordedAt = latestSignOffEvidence?.CapturedAtUtc ?? job.FinalClientSignOffEvidenceRecordedAt,
            FinalClientSignOffDeviceId = latestSignOffEvidence?.DeviceId,
            FinalClientSignOffAppVersion = latestSignOffEvidence?.AppVersion
        };
        return Ok(dto);
    }

    /// <summary>
    /// Safety net: ensure planned parts are present on the job from related quote part lines.
    /// Covers both directly linked quotes and quotes linked through the job's service request.
    /// </summary>
    private async Task EnsurePlannedPartsSyncedFromRelatedQuotesAsync(Guid jobId, CancellationToken ct)
    {
        var job = await _db.JobCards
            .Include(j => j.Site).ThenInclude(s => s!.Company)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job?.Site?.CompanyId == null)
            return;

        var allowedCompanyIds = new HashSet<Guid> { job.Site.CompanyId.Value };
        if (job.Site.Company?.ParentCompanyId.HasValue == true)
            allowedCompanyIds.Add(job.Site.Company.ParentCompanyId.Value);

        var query = _db.QuoteLineItems
            .Where(li =>
                (li.LineType == "Part" || li.LineType == "part")
                && li.PartId.HasValue
                && li.Quantity > 0
                && (li.Quote.JobCardId == jobId
                    || (job.ServiceRequestId.HasValue && li.Quote.ServiceRequestId == job.ServiceRequestId.Value)));

        var quotePartLines = await query
            .Select(li => new { PartId = li.PartId!.Value, li.Quantity })
            .ToListAsync(ct);
        if (quotePartLines.Count == 0)
            return;

        var partIds = quotePartLines.Select(x => x.PartId).Distinct().ToList();
        var validPartIds = await _db.Parts.AsNoTracking()
            .Where(p =>
                partIds.Contains(p.Id)
                && p.CompanyId.HasValue
                && allowedCompanyIds.Contains(p.CompanyId.Value)
                && !p.IsLabour)
            .Select(p => p.Id)
            .ToListAsync(ct);
        if (validPartIds.Count == 0)
            return;
        var validSet = validPartIds.ToHashSet();

        var requiredQtyByPart = quotePartLines
            .Where(x => validSet.Contains(x.PartId))
            .GroupBy(x => x.PartId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => (int)Math.Max(1, Math.Ceiling(x.Quantity))));

        var existing = await _db.JobCardPlannedParts
            .Where(jpp => jpp.JobCardId == jobId && requiredQtyByPart.Keys.Contains(jpp.PartId))
            .ToListAsync(ct);
        var existingByPart = existing.ToDictionary(x => x.PartId);

        var changed = false;
        foreach (var kv in requiredQtyByPart)
        {
            if (existingByPart.TryGetValue(kv.Key, out var row))
            {
                if (row.Quantity < kv.Value)
                {
                    row.Quantity = kv.Value;
                    changed = true;
                }
            }
            else
            {
                _db.JobCardPlannedParts.Add(new JobCardPlannedPart
                {
                    Id = Guid.NewGuid(),
                    JobCardId = jobId,
                    PartId = kv.Key,
                    Quantity = kv.Value
                });
                changed = true;
            }
        }
        if (requiredQtyByPart.Count > 0 && !job.PartsRequired)
        {
            job.PartsRequired = true;
            changed = true;
        }

        if (changed)
            await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Records final client sign-off for job completion: requires a captured client signature image (multipart <c>file</c>), stored like permit signature evidence.
    /// Optional <c>signerName</c> is the client's printed name. Same readiness rules as completing the job.
    /// </summary>
    [HttpPost("{id:guid}/final-client-sign-off")]
    [Authorize(Policy = "RequireViewJobCards")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(JobCardDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobCardDocumentDto>> FinalClientSignOff(
        Guid id,
        [FromForm] IFormFile? file,
        [FromForm] string? signerName,
        [FromForm] string? deviceId,
        [FromForm] string? appVersion,
        [FromForm] string? captureSource,
        CancellationToken ct = default)
    {
        if (!await CanAccessJobInScopeAsync(id, ct))
            return NotFound();
        if (await RejectIfJobCardPaidLockedAsync(id, ct) is ActionResult paidLock)
            return paidLock;
        if (await WaAmendmentSignOffHelper.JobHasExpiredWorkAuthorisationStandstillAsync(_db, id, ct))
            return StatusCode(403, new { message = WaAmendmentSignOffHelper.WaExpiredStandstillMessage });
        if (await WaAmendmentSignOffHelper.JobHasPendingWaAmendmentAsync(_db, id, ct))
            return StatusCode(403, new { message = WaAmendmentSignOffHelper.PermitActionBlockedMessage });

        var job = await _db.JobCards
            .Include(j => j.Site)
            .Include(j => j.Documents)
            .Include(j => j.Permits).ThenInclude(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job == null)
            return NotFound();

        if (JobCardFinalSignOffHelper.HasCapturedSignature(job.Documents))
            return BadRequest(new { message = "Final client sign-off has already been recorded for this job." });

        var companyId = job.Site?.CompanyId;
        if (companyId.HasValue)
        {
            var budget = await _db.ClientBudgets.AsNoTracking().FirstOrDefaultAsync(b => b.CompanyId == companyId.Value, ct);
            if (budget != null && budget.ThresholdAmount > 0 && budget.WorkPaused)
                return StatusCode(403, new { message = "Work is paused because budget threshold was exceeded. Client must approve continuation before completing the job." });
        }

        var visible = PaperPermitModeHelper.VisiblePermits(job.Permits).ToList();
        if (!JobCardTechnicianCompletionGateHelper.TryValidate(
                job.Status,
                job.PermitsRequired,
                visible,
                job.Documents.ToList(),
                DateTime.UtcNow,
                out var gateError))
            return BadRequest(new { message = gateError });

        var actingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(actingUserId))
            return Unauthorized();
        if (!await CanPerformTechnicianWorkAsync(id, actingUserId, ct))
            return StatusCode(403, new { message = "You must be assigned to this job or have AssignTechnicians permission to record final client sign-off." });
        if (await IsBudgetBlockingWorkAsync(id, ct))
            return StatusCode(403, new { message = "Work is paused because budget threshold was exceeded. Client must approve continuation." });

        if (file == null || file.Length == 0 || file.Length > MaxFileSizeBytes)
            return BadRequest(new { message = "A signature image file is required (PNG or JPG, under 10 MB)." });
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !AllowedSignatureImageExtensions.Contains(ext))
            return BadRequest(new { message = "Allowed signature file types: PNG, JPG." });
        var sigErr = await FileContentSignatureHelper.ValidateContentMatchesExtensionAsync(file, ext, ct);
        if (sigErr != null)
            return BadRequest(new { message = sigErr });

        var dir = Path.Combine(_env.ContentRootPath, DocUploadFolder);
        Directory.CreateDirectory(dir);
        var docId = Guid.NewGuid();
        var safeExt = ext is ".jpeg" or ".jpg" or ".png" ? ext : ".png";
        var fileName = docId.ToString("N") + safeExt;
        var relativePath = DocUploadFolder + "/" + fileName;
        var fullPath = Path.Combine(dir, fileName);
        await using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            await file.CopyToAsync(stream, ct);
        var signOffHash = await ComputeFileSha256HexAsync(fullPath, ct);

        var notes = string.IsNullOrWhiteSpace(signerName) ? null : signerName.Trim();
        if (notes != null && notes.Length > 500)
            notes = notes[..500];
        var normalizedDeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim()[..Math.Min(128, deviceId.Trim().Length)];
        var normalizedAppVersion = string.IsNullOrWhiteSpace(appVersion) ? null : appVersion.Trim()[..Math.Min(64, appVersion.Trim().Length)];
        var normalizedCaptureSource = string.IsNullOrWhiteSpace(captureSource) ? "UploadedImage" : captureSource.Trim()[..Math.Min(64, captureSource.Trim().Length)];
        var previousEvidenceHash = await _db.JobCardSignOffEvidenceRecords.AsNoTracking()
            .Where(x => x.JobCardId == id)
            .OrderByDescending(x => x.CapturedAtUtc)
            .Select(x => x.EvidenceHash)
            .FirstOrDefaultAsync(ct);

        var utcNow = DateTime.UtcNow;
        var doc = new JobCardDocument
        {
            Id = docId,
            JobCardId = id,
            DocumentType = JobCardFinalSignOffHelper.DocumentType,
            SignedAt = utcNow,
            SignedByUserId = actingUserId,
            Notes = notes,
            FilePath = relativePath
        };
        _db.JobCardDocuments.Add(doc);
        var evidenceHash = ComputeEvidenceHash(id, docId, signOffHash, previousEvidenceHash, normalizedCaptureSource, normalizedDeviceId, normalizedAppVersion, actingUserId, utcNow);
        _db.JobCardSignOffEvidenceRecords.Add(new JobCardSignOffEvidence
        {
            Id = Guid.NewGuid(),
            JobCardId = id,
            JobCardDocumentId = docId,
            FileSha256 = signOffHash,
            PreviousEvidenceHash = previousEvidenceHash,
            EvidenceHash = evidenceHash,
            CaptureSource = normalizedCaptureSource,
            DeviceId = normalizedDeviceId,
            AppVersion = normalizedAppVersion,
            SignerDisplayName = notes,
            CapturedAtUtc = utcNow,
            RecordedByUserId = actingUserId
        });
        job.FinalClientSignOffAt = utcNow;
        job.FinalClientSignOffByUserId = actingUserId;
        job.FinalClientSignOffFileSha256 = signOffHash;
        job.FinalClientSignOffCaptureSource = normalizedCaptureSource;
        job.FinalClientSignOffEvidenceHash = evidenceHash;
        job.FinalClientSignOffEvidenceRecordedAt = utcNow;
        job.UpdatedAt = utcNow;
        await _auditService.LogAsync("JobCardFinalClientSignOff", "JobCard", id.ToString(), $"DocumentId: {docId}", ct);
        await _db.SaveChangesAsync(ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(id, ct);

        var loaded = await _db.JobCardDocuments.AsNoTracking()
            .Include(d => d.SignedByUser)
            .FirstAsync(d => d.Id == doc.Id, ct);
        return Ok(new JobCardDocumentDto
        {
            Id = loaded.Id,
            DocumentType = loaded.DocumentType,
            SignedAt = loaded.SignedAt,
            SignedByUserName = loaded.SignedByUser?.FullName ?? loaded.SignedByUser?.Email,
            Notes = loaded.Notes,
            FilePath = loaded.FilePath
        });
    }

    private static async Task<string> ComputeFileSha256HexAsync(string fullPath, CancellationToken ct)
    {
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeEvidenceHash(
        Guid jobCardId,
        Guid documentId,
        string fileSha256,
        string? previousEvidenceHash,
        string captureSource,
        string? deviceId,
        string? appVersion,
        string recordedByUserId,
        DateTime capturedAtUtc)
    {
        var payload = string.Join("|", new[]
        {
            jobCardId.ToString("N"),
            documentId.ToString("N"),
            fileSha256,
            previousEvidenceHash ?? "",
            captureSource,
            deviceId ?? "",
            appVersion ?? "",
            recordedByUserId,
            capturedAtUtc.ToString("O")
        });
        using var sha = SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Switch job to paper permit mode by resetting permit state:
    /// - existing permits are removed
    /// - a fresh Work Authorisation draft is created
    /// Permit manager or AssignTechnicians only.
    /// </summary>
    [HttpPost("{id:guid}/paper-permit-mode")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> ActivatePaperPermitMode(Guid id, [FromBody] ActivatePaperPermitModeRequest? request, CancellationToken ct = default)
    {
        if (request?.Enable != true)
            return BadRequest(new { message = "Enable must be true to switch to paper permit mode." });
        if (!await CanAccessJobInScopeAsync(id, ct))
            return NotFound();
        if (await RejectIfJobCardPaidLockedAsync(id, ct) is ActionResult paidLock)
            return paidLock;

        var job = await _db.JobCards
            .Include(j => j.Site).ThenInclude(s => s!.Company)
            .Include(j => j.Permits).ThenInclude(p => p.Attachments)
            .Include(j => j.Permits).ThenInclude(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job == null)
            return NotFound();

        var actingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (!await PaperPermitModeHelper.UserMayActivatePaperPermitModeAsync(_db, id, actingUserId, _userManager, _permissionService, ct))
            return Forbid();

        if (!PaperPermitModeHelper.CanActivatePaperPermitMode(job))
            return BadRequest(new { message = "Paper permit mode cannot be enabled (already enabled or permits are not required for this job)." });

        var existingPermits = job.Permits.ToList();
        var existingPermitIds = existingPermits.Select(p => p.Id).ToList();
        var existingAttachmentPaths = existingPermits
            .SelectMany(p => p.Attachments)
            .Select(a => a.FilePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Full reset requested for paper mode: delete existing permit rows and links.
        if (existingPermitIds.Count > 0)
        {
            var links = await _db.JobPermitMasterLinks
                .Where(l => existingPermitIds.Contains(l.MasterPermitId) || existingPermitIds.Contains(l.ChildPermitId))
                .ToListAsync(ct);
            if (links.Count > 0)
                _db.JobPermitMasterLinks.RemoveRange(links);

            var attachments = await _db.JobPermitAttachments
                .Where(a => existingPermitIds.Contains(a.JobPermitId))
                .ToListAsync(ct);
            if (attachments.Count > 0)
                _db.JobPermitAttachments.RemoveRange(attachments);

            var permitsToDelete = await _db.JobPermits
                .Where(p => existingPermitIds.Contains(p.Id))
                .ToListAsync(ct);
            if (permitsToDelete.Count > 0)
                _db.JobPermits.RemoveRange(permitsToDelete);
        }

        job.PaperPermitMode = true;
        job.PaperModeActivatedAt = DateTime.UtcNow;
        job.PaperModeActivatedByUserId = string.IsNullOrEmpty(actingUserId) ? null : actingUserId;
        job.ActiveJobPermitId = null;

        // Create a fresh master Work Authorisation draft so paper flow can continue immediately.
        var companyId = job.Site?.CompanyId;
        var waTypeId = await _db.PermitTypes
            .Where(pt => pt.IsActive && pt.IsWorkAuthorisation && (pt.CompanyId == null || pt.CompanyId == companyId))
            .OrderBy(pt => pt.CompanyId == null ? 1 : 0)
            .ThenBy(pt => pt.Name)
            .Select(pt => (Guid?)pt.Id)
            .FirstOrDefaultAsync(ct);
        if (!waTypeId.HasValue)
        {
            var childPermitTypeIds = await _db.PermitTypes
                .Where(pt => pt.IsActive && !pt.IsWorkAuthorisation && (pt.CompanyId == null || pt.CompanyId == companyId))
                .OrderBy(pt => pt.CompanyId == null ? 1 : 0)
                .ThenBy(pt => pt.Name)
                .Select(pt => pt.Id)
                .ToListAsync(ct);
            var defaultWaType = new PermitType
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
            _db.PermitTypes.Add(defaultWaType);
            await _db.SaveChangesAsync(ct);
            waTypeId = defaultWaType.Id;
        }

        var waTemplate = await _db.PermitTemplates
            .Where(pt => pt.PermitTypeId == waTypeId.Value && pt.IsActive)
            .OrderBy(pt => pt.SiteId == null ? 0 : 1)
            .ThenBy(pt => pt.CompanyId == null ? 0 : 1)
            .FirstOrDefaultAsync(ct);
        if (waTemplate == null)
        {
            var waType = await _db.PermitTypes.FirstAsync(pt => pt.Id == waTypeId.Value, ct);
            waTemplate = new PermitTemplate
            {
                Id = Guid.NewGuid(),
                PermitTypeId = waType.Id,
                Name = waType.Name,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.PermitTemplates.Add(waTemplate);
            await _db.SaveChangesAsync(ct);
        }

        var permitNumbersQuery = _db.JobPermits
            .Where(p => p.PermitTemplate.PermitTypeId == waTypeId.Value);
        if (companyId.HasValue)
            permitNumbersQuery = permitNumbersQuery.Where(p => p.JobCard.Site.CompanyId == companyId.Value);
        var maxPermitNumber = await permitNumbersQuery
            .Select(p => (int?)p.PermitNumber)
            .MaxAsync(ct) ?? 0;

        _db.JobPermits.Add(new JobPermit
        {
            Id = Guid.NewGuid(),
            PermitNumber = maxPermitNumber + 1,
            JobCardId = job.Id,
            PermitTemplateId = waTemplate.Id,
            Status = PermitStatus.Draft,
            RequestedAt = DateTime.UtcNow,
            RequestedByUserId = actingUserId
        });

        await _db.SaveChangesAsync(ct);

        foreach (var relPath in existingAttachmentPaths)
        {
            var fullPath = Path.Combine(_env.ContentRootPath, relPath!.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                if (System.IO.File.Exists(fullPath))
                    System.IO.File.Delete(fullPath);
            }
            catch
            {
                // Best-effort cleanup only; DB reset should still succeed.
            }
        }

        await _auditService.LogAsync(
            "PaperPermitModeActivated",
            "JobCard",
            id.ToString(),
            $"Paper mode reset: removed {existingPermitIds.Count} existing permit(s); created fresh Work Authorisation draft.",
            ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(id, ct);
        return NoContent();
    }

    /// <summary>Returns true when budget threshold exceeded and work is paused (block document/part/status updates; allow incidents).</summary>
    private async Task<bool> IsBudgetBlockingWorkAsync(Guid jobId, CancellationToken ct)
    {
        var job = await _db.JobCards.AsNoTracking().Include(j => j.Site).FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job?.Site?.CompanyId == null) return false;
        var budget = await _db.ClientBudgets.AsNoTracking().FirstOrDefaultAsync(b => b.CompanyId == job.Site!.CompanyId!.Value, ct);
        return budget != null && budget.ThresholdAmount > 0 && budget.WorkPaused;
    }

    private async Task<ActionResult?> RejectIfJobCardPaidLockedAsync(Guid jobCardId, CancellationToken ct)
    {
        if (await PaidJobCardLockHelper.IsLockedAsync(_db, jobCardId, ct))
            return BadRequest(ApiResponseBodies.Message(PaidJobCardLockHelper.UserMessage));
        return null;
    }

    private async Task<JobCardBudgetSummaryDto?> GetBudgetForJobAsync(JobCard job, CancellationToken ct)
    {
        var companyId = job.Site?.CompanyId;
        if (!companyId.HasValue) return null;
        var budget = await _db.ClientBudgets.AsNoTracking()
            .FirstOrDefaultAsync(b => b.CompanyId == companyId.Value, ct);
        if (budget == null || budget.ThresholdAmount <= 0) return null;
        return new JobCardBudgetSummaryDto
        {
            ThresholdAmount = budget.ThresholdAmount,
            SpentAmount = budget.SpentAmount,
            Currency = budget.Currency ?? "ZAR",
            WorkPaused = budget.WorkPaused
        };
    }

    private async Task<List<JobCardAssignmentDto>> BuildAssignmentsWithBadgesAsync(List<JobCardAssignment> assignments, CancellationToken ct)
    {
        if (assignments.Count == 0) return new List<JobCardAssignmentDto>();
        var userIds = assignments.Select(a => a.UserId).Distinct().ToList();
        var badgeByUser = await _db.UserBadges.AsNoTracking()
            .Where(ub => ub.UserId != null && userIds.Contains(ub.UserId) && ub.IsActive && ub.ExpiresAt > DateTime.UtcNow)
            .GroupBy(ub => ub.UserId!)
            .ToDictionaryAsync(g => g.Key, g => g.Select(ub => ub.BadgeId).ToList(), ct);
        return assignments.Select(a =>
        {
            badgeByUser.TryGetValue(a.UserId, out var badges);
            return new JobCardAssignmentDto
            {
                UserId = a.UserId,
                UserName = a.User?.FullName ?? a.User?.Email,
                AssignedAt = a.AssignedAt,
                IsPermitManager = a.IsPermitManager,
                BadgeIds = badges ?? new List<Guid>()
            };
        }).ToList();
    }

    [HttpPost("{id:guid}/documents")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(JobCardDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobCardDocumentDto>> SubmitDocument(Guid id, [FromBody] SubmitDocumentRequest request, CancellationToken ct)
    {
        if (!await CanAccessJobInScopeAsync(id, ct))
            return NotFound();
        if (string.IsNullOrWhiteSpace(request.DocumentType))
            return BadRequest(ApiResponseBodies.Message("DocumentType is required."));

        Guid? poId = request.PurchaseOrderId;
        if (poId.HasValue)
        {
            var po = await _db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(p => p.Id == poId.Value, ct);
            if (po == null)
                return BadRequest(ApiResponseBodies.Message("Purchase order not found."));
        }

        var job = await _db.JobCards.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job == null)
            return NotFound();
        if (await RejectIfJobCardPaidLockedAsync(id, ct) is ActionResult paidLockDoc)
            return paidLockDoc;

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();
        if (!await CanPerformTechnicianWorkAsync(id, userId, ct))
            return StatusCode(403, "You must be assigned to this job or have AssignTechnicians permission to add documents.");
        if (await IsBudgetBlockingWorkAsync(id, ct))
            return StatusCode(403, "Work is paused because budget threshold was exceeded. Client must approve continuation.");
        if (await WaAmendmentSignOffHelper.JobHasPendingWaAmendmentAsync(_db, id, ct))
            return StatusCode(403, WaAmendmentSignOffHelper.WorkStandstillMessage);
        if (await WaAmendmentSignOffHelper.JobHasExpiredWorkAuthorisationStandstillAsync(_db, id, ct))
            return StatusCode(403, WaAmendmentSignOffHelper.WaExpiredStandstillMessage);

        var doc = new JobCardDocument
        {
            Id = Guid.NewGuid(),
            JobCardId = id,
            DocumentType = request.DocumentType.Trim(),
            SignedAt = DateTime.UtcNow,
            SignedByUserId = userId,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            PurchaseOrderId = request.PurchaseOrderId
        };
        _db.JobCardDocuments.Add(doc);
        await _db.SaveChangesAsync(ct);

        var loaded = await _db.JobCardDocuments.AsNoTracking()
            .Include(d => d.SignedByUser)
            .Include(d => d.PurchaseOrder)
            .FirstAsync(d => d.Id == doc.Id, ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(id, ct);
        return Ok(new JobCardDocumentDto
        {
            Id = loaded.Id,
            DocumentType = loaded.DocumentType,
            SignedAt = loaded.SignedAt,
            SignedByUserName = loaded.SignedByUser?.FullName ?? loaded.SignedByUser?.Email,
            Notes = loaded.Notes,
            PurchaseOrderId = loaded.PurchaseOrderId,
            PurchaseOrderNumber = loaded.PurchaseOrder?.PONumber,
            FilePath = loaded.FilePath
        });
    }

    [HttpPost("{id:guid}/documents/upload")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(JobCardDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobCardDocumentDto>> SubmitDocumentWithFile(Guid id, [FromForm] string documentType, [FromForm] IFormFile? file, [FromForm] string? notes, CancellationToken ct = default)
    {
        if (!await CanAccessJobInScopeAsync(id, ct))
            return NotFound();
        if (string.IsNullOrWhiteSpace(documentType))
            return BadRequest(ApiResponseBodies.Message("DocumentType is required."));
        if (file != null)
        {
            if (file.Length == 0 || file.Length > MaxFileSizeBytes)
                return BadRequest(ApiResponseBodies.Message("File required and must be under 10 MB."));
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) || !AllowedDocumentExtensions.Contains(ext))
                return BadRequest(ApiResponseBodies.Message("Allowed types: PDF, PNG, JPG."));
            var sigErr = await FileContentSignatureHelper.ValidateContentMatchesExtensionAsync(file, ext, ct);
            if (sigErr != null)
                return BadRequest(ApiResponseBodies.Message(sigErr));
        }
        var job = await _db.JobCards.FindAsync([id], ct);
        if (job == null)
            return NotFound();
        if (await RejectIfJobCardPaidLockedAsync(id, ct) is ActionResult paidLockUpload)
            return paidLockUpload;
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();
        if (!await CanPerformTechnicianWorkAsync(id, userId, ct))
            return StatusCode(403, "You must be assigned to this job or have AssignTechnicians permission to add documents.");
        if (await IsBudgetBlockingWorkAsync(id, ct))
            return StatusCode(403, "Work is paused because budget threshold was exceeded. Client must approve continuation.");
        if (await WaAmendmentSignOffHelper.JobHasPendingWaAmendmentAsync(_db, id, ct))
            return StatusCode(403, WaAmendmentSignOffHelper.WorkStandstillMessage);
        if (await WaAmendmentSignOffHelper.JobHasExpiredWorkAuthorisationStandstillAsync(_db, id, ct))
            return StatusCode(403, WaAmendmentSignOffHelper.WaExpiredStandstillMessage);

        string? relativePath = null;
        if (file != null)
        {
            var dir = Path.Combine(_env.ContentRootPath, DocUploadFolder);
            Directory.CreateDirectory(dir);
            var docId = Guid.NewGuid();
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var fileName = docId.ToString("N") + ext;
            relativePath = DocUploadFolder + "/" + fileName;
            var fullPath = Path.Combine(dir, fileName);
            await using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
                await file.CopyToAsync(stream, ct);
        }

        var doc = new JobCardDocument
        {
            Id = Guid.NewGuid(),
            JobCardId = id,
            DocumentType = documentType.Trim(),
            SignedAt = DateTime.UtcNow,
            SignedByUserId = userId,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            FilePath = relativePath
        };
        _db.JobCardDocuments.Add(doc);
        await _db.SaveChangesAsync(ct);

        var loaded = await _db.JobCardDocuments.AsNoTracking()
            .Include(d => d.SignedByUser)
            .FirstAsync(d => d.Id == doc.Id, ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(id, ct);
        return Ok(new JobCardDocumentDto
        {
            Id = loaded.Id,
            DocumentType = loaded.DocumentType,
            SignedAt = loaded.SignedAt,
            SignedByUserName = loaded.SignedByUser?.FullName ?? loaded.SignedByUser?.Email,
            Notes = loaded.Notes,
            FilePath = loaded.FilePath
        });
    }

    [HttpGet("{id:guid}/documents/{docId:guid}/file")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocumentFile(Guid id, Guid docId, CancellationToken ct = default)
    {
        if (!await CanAccessJobInScopeAsync(id, ct))
            return NotFound();
        var doc = await _db.JobCardDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == docId && d.JobCardId == id, ct);
        var validatedPath = FilePathHelper.ValidateAndNormalize(doc?.FilePath);
        if (doc == null || validatedPath == null)
            return NotFound();
        var fullPath = Path.Combine(_env.ContentRootPath, validatedPath);
        if (!System.IO.File.Exists(fullPath))
            return NotFound();
        var ext = Path.GetExtension(validatedPath).ToLowerInvariant();
        var contentType = ext switch { ".pdf" => "application/pdf", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", _ => "application/octet-stream" };
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, contentType, Path.GetFileName(validatedPath));
    }

    [HttpPost("{id:guid}/incidents")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(IncidentReportDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IncidentReportDto>> CreateIncident(Guid id, [FromBody] CreateIncidentRequest request, CancellationToken ct = default)
    {
        if (!await CanAccessJobInScopeAsync(id, ct))
            return NotFound();
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest(ApiResponseBodies.Message("Description is required."));
        var job = await _db.JobCards.FindAsync([id], ct);
        if (job == null)
            return NotFound();
        if (await RejectIfJobCardPaidLockedAsync(id, ct) is ActionResult paidLockInc)
            return paidLockInc;
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();
        if (!await CanPerformTechnicianWorkAsync(id, userId, ct))
            return StatusCode(403, "You must be assigned to this job or have AssignTechnicians permission to create incidents.");
        var severity = string.IsNullOrWhiteSpace(request.Severity) ? "Medium" : request.Severity.Trim();
        var incident = new IncidentReport
        {
            Id = Guid.NewGuid(),
            JobCardId = id,
            ReportedByUserId = userId,
            Description = request.Description.Trim(),
            Severity = severity,
            Status = IncidentStatus.Open,
            CreatedAt = DateTime.UtcNow
        };
        _db.IncidentReports.Add(incident);
        await _db.SaveChangesAsync(ct);
        await _auditService.LogAsync("IncidentCreated", "IncidentReport", incident.Id.ToString(), $"JobCard: {id}, Severity: {severity}", ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(id, ct);
        return CreatedAtAction(null, new IncidentReportDto
        {
            Id = incident.Id,
            Description = incident.Description,
            Severity = incident.Severity,
            Status = incident.Status,
            PhotoPaths = new List<string>(),
            CreatedAt = incident.CreatedAt
        });
    }

    [HttpPost("{id:guid}/incidents/with-photos")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(IncidentReportDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IncidentReportDto>> CreateIncidentWithPhotos(Guid id, [FromForm] string description, [FromForm] string? severity, [FromForm] IFormFileCollection? photos, CancellationToken ct = default)
    {
        if (!await CanAccessJobInScopeAsync(id, ct))
            return NotFound();
        if (string.IsNullOrWhiteSpace(description))
            return BadRequest(ApiResponseBodies.Message("Description is required."));
        var job = await _db.JobCards.FindAsync([id], ct);
        if (job == null)
            return NotFound();
        if (await RejectIfJobCardPaidLockedAsync(id, ct) is ActionResult paidLockIncPh)
            return paidLockIncPh;
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();
        if (!await CanPerformTechnicianWorkAsync(id, userId, ct))
            return StatusCode(403, "You must be assigned to this job or have AssignTechnicians permission to create incidents.");
        var sev = string.IsNullOrWhiteSpace(severity) ? "Medium" : severity.Trim();
        var photoPaths = new List<string>();
        if (photos != null && photos.Count > 0)
        {
            var dir = Path.Combine(_env.ContentRootPath, IncidentPhotoFolder);
            Directory.CreateDirectory(dir);
            foreach (var f in photos)
            {
                if (f == null || f.Length == 0 || f.Length > MaxFileSizeBytes) continue;
                var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
                if (string.IsNullOrEmpty(ext) || !AllowedDocumentExtensions.Contains(ext)) continue;
                if (await FileContentSignatureHelper.ValidateContentMatchesExtensionAsync(f, ext, ct) != null) continue;
                var photoId = Guid.NewGuid();
                var fileName = photoId.ToString("N") + ext;
                var rel = IncidentPhotoFolder + "/" + fileName;
                var fullPath = Path.Combine(dir, fileName);
                await using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    await f.CopyToAsync(stream, ct);
                photoPaths.Add(rel);
            }
        }
        var photosJson = photoPaths.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(photoPaths) : null;
        var incident = new IncidentReport
        {
            Id = Guid.NewGuid(),
            JobCardId = id,
            ReportedByUserId = userId,
            Description = description.Trim(),
            Severity = sev,
            Status = IncidentStatus.Open,
            PhotosJson = photosJson,
            CreatedAt = DateTime.UtcNow
        };
        _db.IncidentReports.Add(incident);
        var blockJob = false;
        if (Request.Form.TryGetValue("blockJob", out var blockVals))
        {
            var bv = blockVals.ToString();
            blockJob = bv.Equals("true", StringComparison.OrdinalIgnoreCase) || bv == "1";
        }
        if (blockJob)
        {
            var prefix = "Paused: incident — ";
            var max = 500 - prefix.Length;
            if (max < 20) max = 20;
            var snippet = incident.Description.Trim();
            if (snippet.Length > max) snippet = snippet[..max];
            job.BlockedReason = prefix + snippet;
            job.BlockedAt = DateTime.UtcNow;
            job.BlockedByUserId = userId;
            await _auditService.LogAsync("JobCardBlocked", "JobCard", id.ToString(), $"Incident {incident.Id}: {job.BlockedReason}", ct);
        }
        await _db.SaveChangesAsync(ct);
        await _auditService.LogAsync("IncidentCreated", "IncidentReport", incident.Id.ToString(), $"JobCard: {id}, Severity: {sev}", ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(id, ct);
        return CreatedAtAction(null, new IncidentReportDto
        {
            Id = incident.Id,
            Description = incident.Description,
            Severity = incident.Severity,
            Status = incident.Status,
            PhotoPaths = photoPaths,
            CreatedAt = incident.CreatedAt
        });
    }

    [HttpPost("{id:guid}/incidents/{incidentId:guid}/photos")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(IncidentReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IncidentReportDto>> AddIncidentPhotos(Guid id, Guid incidentId, [FromForm] IFormFileCollection photos, CancellationToken ct = default)
    {
        if (!await CanAccessJobInScopeAsync(id, ct))
            return NotFound();
        var incident = await _db.IncidentReports.FirstOrDefaultAsync(ir => ir.Id == incidentId && ir.JobCardId == id, ct);
        if (incident == null)
            return NotFound();
        if (await RejectIfJobCardPaidLockedAsync(id, ct) is ActionResult paidLockAddPh)
            return paidLockAddPh;
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();
        if (!await CanPerformTechnicianWorkAsync(id, userId, ct))
            return StatusCode(403, "You must be assigned to this job or have AssignTechnicians permission to add incident photos.");
        if (photos == null || photos.Count == 0)
            return BadRequest(ApiResponseBodies.Message("At least one photo is required."));
        var existingPaths = ParsePhotosJson(incident.PhotosJson);
        var pathsBefore = existingPaths.Count;
        var dir = Path.Combine(_env.ContentRootPath, IncidentPhotoFolder);
        Directory.CreateDirectory(dir);
        var submitted = photos.Where(f => f != null && f.Length > 0).ToList();
        foreach (var f in submitted)
        {
            if (f.Length > MaxFileSizeBytes) continue;
            var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) || !AllowedDocumentExtensions.Contains(ext)) continue;
            if (await FileContentSignatureHelper.ValidateContentMatchesExtensionAsync(f, ext, ct) != null) continue;
            var photoId = Guid.NewGuid();
            var fileName = photoId.ToString("N") + ext;
            var rel = IncidentPhotoFolder + "/" + fileName;
            var fullPath = Path.Combine(dir, fileName);
            await using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
                await f.CopyToAsync(stream, ct);
            existingPaths.Add(rel);
        }
        if (submitted.Count > 0 && existingPaths.Count == pathsBefore)
            return BadRequest(ApiResponseBodies.Message("No valid photo files were saved. Use PDF, PNG, or JPG under 10 MB with content matching the extension."));
        incident.PhotosJson = existingPaths.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(existingPaths) : incident.PhotosJson;
        await _db.SaveChangesAsync(ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(id, ct);
        return Ok(new IncidentReportDto
        {
            Id = incident.Id,
            Description = incident.Description,
            Severity = incident.Severity,
            Status = incident.Status,
            Resolution = incident.Resolution,
            PhotoPaths = existingPaths,
            CreatedAt = incident.CreatedAt
        });
    }

    [HttpGet("{id:guid}/incidents/{incidentId:guid}/photos/{photoIndex:int}")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIncidentPhoto(Guid id, Guid incidentId, int photoIndex, CancellationToken ct = default)
    {
        if (!await CanAccessJobInScopeAsync(id, ct))
            return NotFound();
        var incident = await _db.IncidentReports.AsNoTracking()
            .FirstOrDefaultAsync(ir => ir.Id == incidentId && ir.JobCardId == id, ct);
        if (incident == null || string.IsNullOrEmpty(incident.PhotosJson))
            return NotFound();
        var paths = ParsePhotosJson(incident.PhotosJson);
        if (photoIndex < 0 || photoIndex >= paths.Count)
            return NotFound();
        var path = paths[photoIndex];
        var validatedPath = FilePathHelper.ValidateAndNormalize(path);
        if (validatedPath == null)
            return NotFound();
        var fullPath = Path.Combine(_env.ContentRootPath, validatedPath);
        if (!System.IO.File.Exists(fullPath))
            return NotFound();
        var ext = Path.GetExtension(validatedPath).ToLowerInvariant();
        var contentType = ext switch { ".pdf" => "application/pdf", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", _ => "application/octet-stream" };
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, contentType, Path.GetFileName(validatedPath));
    }

    [HttpPatch("{id:guid}/incidents/{incidentId:guid}")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(IncidentReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IncidentReportDto>> UpdateIncident(Guid id, Guid incidentId, [FromBody] UpdateIncidentRequest request, CancellationToken ct = default)
    {
        if (!await CanAccessJobInScopeAsync(id, ct))
            return NotFound();
        var incident = await _db.IncidentReports.FirstOrDefaultAsync(ir => ir.Id == incidentId && ir.JobCardId == id, ct);
        if (incident == null)
            return NotFound();
        if (await RejectIfJobCardPaidLockedAsync(id, ct) is ActionResult paidLockUpdInc)
            return paidLockUpdInc;
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();
        if (!await CanPerformTechnicianWorkAsync(id, userId, ct))
            return StatusCode(403, "You must be assigned to this job or have AssignTechnicians permission to update incidents.");
        if (!string.IsNullOrWhiteSpace(request.Status))
            incident.Status = request.Status.Trim();
        if (request.Resolution != null)
            incident.Resolution = string.IsNullOrWhiteSpace(request.Resolution) ? null : request.Resolution.Trim();
        await _db.SaveChangesAsync(ct);
        await _auditService.LogAsync("IncidentUpdated", "IncidentReport", incidentId.ToString(), $"Status: {incident.Status}", ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(id, ct);
        return Ok(new IncidentReportDto
        {
            Id = incident.Id,
            Description = incident.Description,
            Severity = incident.Severity,
            Status = incident.Status,
            Resolution = incident.Resolution,
            PhotoPaths = ParsePhotosJson(incident.PhotosJson),
            CreatedAt = incident.CreatedAt
        });
    }

    [HttpPost("{id:guid}/parts")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(JobPartDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobPartDto>> AddPart(Guid id, [FromForm] string brand, [FromForm] string? serialNumber, [FromForm] string? description, [FromForm] IFormFile? oldPartPhoto, [FromForm] IFormFile? newPartPhoto, CancellationToken ct = default)
    {
        if (!await CanAccessJobInScopeAsync(id, ct))
            return NotFound();
        if (string.IsNullOrWhiteSpace(brand))
            return BadRequest(ApiResponseBodies.Message("Brand is required."));
        var job = await _db.JobCards.FindAsync([id], ct);
        if (job == null)
            return NotFound();
        if (await RejectIfJobCardPaidLockedAsync(id, ct) is ActionResult paidLockPart)
            return paidLockPart;
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();
        if (!await CanPerformTechnicianWorkAsync(id, userId, ct))
            return StatusCode(403, "You must be assigned to this job or have AssignTechnicians permission to add parts.");
        if (await IsBudgetBlockingWorkAsync(id, ct))
            return StatusCode(403, "Work is paused because budget threshold was exceeded. Client must approve continuation.");
        if (await WaAmendmentSignOffHelper.JobHasPendingWaAmendmentAsync(_db, id, ct))
            return StatusCode(403, WaAmendmentSignOffHelper.WorkStandstillMessage);
        if (await WaAmendmentSignOffHelper.JobHasExpiredWorkAuthorisationStandstillAsync(_db, id, ct))
            return StatusCode(403, WaAmendmentSignOffHelper.WaExpiredStandstillMessage);
        var saveFile = async (IFormFile? f) =>
        {
            if (f == null || f.Length == 0) return (string?)null;
            if (f.Length > MaxFileSizeBytes) return (string?)null;
            var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) || !AllowedDocumentExtensions.Contains(ext)) return (string?)null;
            if (await FileContentSignatureHelper.ValidateContentMatchesExtensionAsync(f, ext, ct) != null) return (string?)null;
            var dir = Path.Combine(_env.ContentRootPath, PartPhotoFolder);
            Directory.CreateDirectory(dir);
            var photoId = Guid.NewGuid();
            var fileName = photoId.ToString("N") + ext;
            var rel = PartPhotoFolder + "/" + fileName;
            var full = Path.Combine(dir, fileName);
            await using (var stream = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None))
                await f.CopyToAsync(stream, ct);
            return rel;
        };
        var oldPath = await saveFile(oldPartPhoto);
        var newPath = await saveFile(newPartPhoto);
        var part = new JobPart
        {
            Id = Guid.NewGuid(),
            JobCardId = id,
            Brand = brand.Trim(),
            SerialNumber = string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            OldPartPhotoPath = oldPath,
            NewPartPhotoPath = newPath,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        _db.JobParts.Add(part);
        await _db.SaveChangesAsync(ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(id, ct);
        return CreatedAtAction(null, new JobPartDto { Id = part.Id, Brand = part.Brand, SerialNumber = part.SerialNumber, Description = part.Description, OldPartPhotoPath = part.OldPartPhotoPath, NewPartPhotoPath = part.NewPartPhotoPath });
    }

    [HttpGet("{id:guid}/parts/{partId:guid}/photo")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPartPhoto(Guid id, Guid partId, [FromQuery] string kind = "old", CancellationToken ct = default)
    {
        if (!await CanAccessJobInScopeAsync(id, ct))
            return NotFound();
        var part = await _db.JobParts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == partId && p.JobCardId == id, ct);
        if (part == null)
            return NotFound();
        var path = (kind ?? "old").ToLowerInvariant() == "new" ? part.NewPartPhotoPath : part.OldPartPhotoPath;
        var validatedPath = FilePathHelper.ValidateAndNormalize(path);
        if (validatedPath == null)
            return NotFound();
        var fullPath = Path.Combine(_env.ContentRootPath, validatedPath);
        if (!System.IO.File.Exists(fullPath))
            return NotFound();
        var ext = Path.GetExtension(validatedPath).ToLowerInvariant();
        var contentType = ext switch { ".pdf" => "application/pdf", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", _ => "application/octet-stream" };
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, contentType, Path.GetFileName(validatedPath));
    }

    [HttpGet("{id:guid}/assignable-technicians")]
    [Authorize(Policy = "RequireAssignTechnicians")]
    [ProducesResponseType(typeof(AssignableTechniciansResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssignableTechniciansResponse>> GetAssignableTechnicians(Guid id, CancellationToken ct = default)
    {
        if (!await CanAccessJobInScopeAsync(id, ct))
            return NotFound();

        var job = await _db.JobCards.AsNoTracking()
            .Include(j => j.Site).ThenInclude(s => s!.Company)
            .FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job == null)
            return NotFound();

        var mspCompanyId = GetMspCompanyIdForSiteCompany(job.Site?.Company);
        var clientIdsUnderMsp = mspCompanyId.HasValue
            ? await _db.Companies.AsNoTracking()
                .Where(c => c.ParentCompanyId == mspCompanyId.Value)
                .Select(c => c.Id)
                .ToListAsync(ct)
            : new List<Guid>();

        var assigned = await _db.JobCardAssignments.Where(a => a.JobCardId == id).Select(a => a.UserId).ToListAsync(ct);
        var technicians = await _userManager.GetUsersInRoleAsync(SeedData.RoleTechnician);
        var inOrganization = technicians
            .Where(u => u.IsActive
                && !assigned.Contains(u.Id)
                && mspCompanyId.HasValue
                && UserBelongsToMspOrganization(u, mspCompanyId.Value, clientIdsUnderMsp))
            .ToList();

        var availableIds = inOrganization.Select(u => u.Id).ToList();
        var badgeByUser = await _db.UserBadges.AsNoTracking()
            .Where(ub => ub.UserId != null && availableIds.Contains(ub.UserId) && ub.IsActive && ub.ExpiresAt > DateTime.UtcNow)
            .GroupBy(ub => ub.UserId!)
            .ToDictionaryAsync(g => g.Key, g => g.Select(ub => ub.BadgeId).ToList(), ct);
        var result = inOrganization
            .Select(u =>
            {
                badgeByUser.TryGetValue(u.Id, out var badgeIds);
                return new AssignableTechnicianDto
                {
                    UserId = u.Id,
                    UserName = u.FullName ?? u.Email ?? u.Id,
                    BadgeIds = badgeIds ?? new List<Guid>()
                };
            })
            .ToList();
        return Ok(new AssignableTechniciansResponse
        {
            Technicians = result,
            RequiredBadgeIds = new List<Guid>(),
            RequiredBadgeNames = new List<string>()
        });
    }

    [HttpPost("{id:guid}/assignments")]
    [Authorize(Policy = "RequireAssignTechnicians")]
    [ProducesResponseType(typeof(JobCardAssignmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobCardAssignmentDto>> AssignTechnician(Guid id, [FromBody] AssignTechnicianRequest request, CancellationToken ct = default)
    {
        if (!await CanAccessJobInScopeAsync(id, ct))
            return NotFound();
        if (await RejectIfJobCardPaidLockedAsync(id, ct) is ActionResult paidLockAssign)
            return paidLockAssign;

        var job = await _db.JobCards
            .Include(j => j.Site).ThenInclude(s => s!.Company)
            .FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job == null)
            return NotFound();

        var mspCompanyId = GetMspCompanyIdForSiteCompany(job.Site?.Company);
        if (!mspCompanyId.HasValue)
            return BadRequest(new { message = "Job site has no company; cannot assign technicians." });

        var clientIdsUnderMsp = await _db.Companies.AsNoTracking()
            .Where(c => c.ParentCompanyId == mspCompanyId.Value)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var user = await _db.Users.FindAsync(new object[] { request.UserId }, ct);
        if (user == null)
            return BadRequest(new { message = "User not found." });
        if (!UserBelongsToMspOrganization(user, mspCompanyId.Value, clientIdsUnderMsp))
            return BadRequest(new { message = "That user is not in your organization." });
        var userRoles = await _userManager.GetRolesAsync(user);
        if (!userRoles.Contains(SeedData.RoleTechnician))
            return BadRequest(new { message = "User is not a technician." });
        var existing = await _db.JobCardAssignments.FirstOrDefaultAsync(a => a.JobCardId == id && a.UserId == request.UserId, ct);
        if (existing != null)
            return Ok(new JobCardAssignmentDto { UserId = user.Id, UserName = user.FullName ?? user.Email, AssignedAt = existing.AssignedAt, IsPermitManager = existing.IsPermitManager });
        var assignedById = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isPermitManager = request.IsPermitManager;
        if (isPermitManager)
        {
            foreach (var a in await _db.JobCardAssignments.Where(a => a.JobCardId == id).ToListAsync(ct))
                a.IsPermitManager = false;
        }
        var assignment = new JobCardAssignment
        {
            JobCardId = id,
            UserId = request.UserId,
            AssignedAt = DateTime.UtcNow,
            AssignedById = assignedById,
            IsPermitManager = isPermitManager
        };
        _db.JobCardAssignments.Add(assignment);
        await _db.SaveChangesAsync(ct);
        await _auditService.LogAsync("TechnicianAssigned", "JobCardAssignment", id.ToString(), $"UserId: {request.UserId}", ct);
        await _notificationService.CreateForUserAsync(request.UserId, "Job card assigned", $"You have been assigned to job card {job.JobCardNumber ?? id.ToString()}.", "JobCard", id.ToString(), ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(id, ct);
        return Ok(new JobCardAssignmentDto { UserId = user.Id, UserName = user.FullName ?? user.Email, AssignedAt = assignment.AssignedAt, IsPermitManager = assignment.IsPermitManager });
    }

    [HttpPatch("{id:guid}/assignments/{userId}")]
    [Authorize(Policy = "RequireAssignTechnicians")]
    [ProducesResponseType(typeof(JobCardAssignmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobCardAssignmentDto>> SetPermitManager(Guid id, string userId, [FromBody] SetPermitManagerRequest request, CancellationToken ct = default)
    {
        if (!await CanAccessJobInScopeAsync(id, ct))
            return NotFound();
        if (await RejectIfJobCardPaidLockedAsync(id, ct) is ActionResult paidLockPm)
            return paidLockPm;

        var a = await _db.JobCardAssignments.Include(x => x.User).FirstOrDefaultAsync(x => x.JobCardId == id && x.UserId == userId, ct);
        if (a == null)
            return NotFound();
        if (request.IsPermitManager)
        {
            foreach (var other in await _db.JobCardAssignments.Where(x => x.JobCardId == id && x.UserId != userId).ToListAsync(ct))
                other.IsPermitManager = false;
        }
        a.IsPermitManager = request.IsPermitManager;
        await _db.SaveChangesAsync(ct);
        await _auditService.LogAsync("PermitManagerSet", "JobCardAssignment", id.ToString(), $"UserId: {userId}, IsPermitManager: {request.IsPermitManager}", ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(id, ct);
        return Ok(new JobCardAssignmentDto { UserId = a.UserId, UserName = a.User?.FullName ?? a.User?.Email, AssignedAt = a.AssignedAt, IsPermitManager = a.IsPermitManager });
    }

    [HttpDelete("{id:guid}/assignments/{userId}")]
    [Authorize(Policy = "RequireAssignTechnicians")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnassignTechnician(Guid id, string userId, CancellationToken ct = default)
    {
        if (!await CanAccessJobInScopeAsync(id, ct))
            return NotFound();
        if (await RejectIfJobCardPaidLockedAsync(id, ct) is ActionResult paidLockUnassign)
            return paidLockUnassign;

        var a = await _db.JobCardAssignments.FirstOrDefaultAsync(x => x.JobCardId == id && x.UserId == userId, ct);
        if (a == null)
            return NotFound();
        _db.JobCardAssignments.Remove(a);
        await _db.SaveChangesAsync(ct);
        await _auditService.LogAsync("TechnicianUnassigned", "JobCardAssignment", id.ToString(), $"UserId: {userId}", ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(id, ct);
        return NoContent();
    }

    /// <summary>Returns true if the user is assigned to the job OR has AssignTechnicians permission.</summary>
    private async Task<bool> CanPerformTechnicianWorkAsync(Guid jobId, string userId, CancellationToken ct)
    {
        var isAssigned = await _db.JobCardAssignments.AnyAsync(a => a.JobCardId == jobId && a.UserId == userId, ct);
        if (isAssigned) return true;
        var user = await _userManager.FindByIdAsync(userId);
        return user != null && await _permissionService.HasPermissionAsync(user, "AssignTechnicians");
    }

    private async Task<bool> CanAccessJobInScopeAsync(Guid jobId, CancellationToken ct)
    {
        return await _scopeGuard.CanAccessJobCardAsync(jobId, ct);
    }

    /// <summary>Employer org for the site: parent MSP when site is under a client company, otherwise the site company.</summary>
    private static Guid? GetMspCompanyIdForSiteCompany(Company? siteCompany)
    {
        if (siteCompany == null) return null;
        if (siteCompany.Type == CompanyType.Client && siteCompany.ParentCompanyId.HasValue)
            return siteCompany.ParentCompanyId;
        return siteCompany.Id;
    }

    /// <summary>Matches Users list scoping: main company or a client subsidiary under that MSP.</summary>
    private static bool UserBelongsToMspOrganization(ApplicationUser user, Guid mspCompanyId, List<Guid> clientCompanyIdsUnderMsp)
    {
        if (!user.CompanyId.HasValue) return false;
        if (user.CompanyId.Value == mspCompanyId) return true;
        return clientCompanyIdsUnderMsp.Contains(user.CompanyId.Value);
    }

    /// <summary>
    /// Effective validity end when exposing <see cref="JobPermitDto.ValidTo"/> (only after client sign-off): min(child, master) when both set.
    /// Draft children often have no ValidTo yet — use a prospective end (default duration from now, capped by master), not the master&apos;s date alone.
    /// </summary>
    private static DateTime? EffectivePermitValidToForApi(JobPermit p, IReadOnlyList<JobPermit> allPermitsOnJob, DateTime utcNow)
    {
        var v = p.ValidTo;
        if (!p.MasterPermitId.HasValue)
            return v;
        var master = allPermitsOnJob.FirstOrDefault(x => x.Id == p.MasterPermitId.Value);
        if (master?.PermitTemplate?.PermitType?.IsWorkAuthorisation != true)
            return v;
        if (!master.ValidTo.HasValue)
            return v;
        if (!v.HasValue)
        {
            var hours = PermitTemplateDurationHelper.ResolvePermitDurationHours(p.PermitTemplate);
            var previewEnd = utcNow.AddHours(hours);
            return previewEnd < master.ValidTo.Value ? previewEnd : master.ValidTo;
        }

        return v.Value < master.ValidTo.Value ? v : master.ValidTo;
    }

    private static List<JobPermitDto> BuildJobPermitDtos(
        JobCard job,
        IEnumerable<JobPermit> permitsInDisplayOrder,
        IReadOnlyList<JobPermit> allPermitsOnJob,
        IReadOnlyList<PermitType> scopedPermitTypes,
        DateTime utcNow,
        Dictionary<Guid, string> permitTypeNameLookup,
        IWorkAuthorizationPermitRulesService workAuthRules,
        bool paperPermitMode)
    {
        var list = new List<JobPermitDto>();
        foreach (var p in permitsInDisplayOrder)
        {
            var triggerIds = ParsePermitTypeIdsJson(p.PermitTemplate?.PermitType?.TriggersPermitTypeIdsJson) ?? new List<Guid>();
            var triggerNames = triggerIds.Select(tid => permitTypeNameLookup.TryGetValue(tid, out var n) ? n : "").Where(n => !string.IsNullOrEmpty(n)).ToList();
            List<Guid>? requestableIds = null;
            List<string>? requestableNames = null;
            if (!paperPermitMode && p.PermitTemplate?.PermitType?.IsWorkAuthorisation == true)
            {
                var opts = WorkAuthorizationRequestablePermitsHelper.GetRequestableWorkPermitTypes(p, allPermitsOnJob, scopedPermitTypes, utcNow, workAuthRules);
                requestableIds = opts.Select(o => o.TypeId).ToList();
                requestableNames = opts.Select(o => o.Name).ToList();
            }
            else if (p.PermitTemplate?.PermitType?.IsWorkAuthorisation == true)
                requestableNames = new List<string>();

            var formFieldSchema = PermitFormJsonHelper.ParseSchema(p.PermitTemplate?.FormSchemaJson);
            var formVals = PermitFormJsonHelper.ParseValues(p.FormSnapshotJson);
            if (!paperPermitMode)
            {
                JobPermit? masterForChild = null;
                if (p.MasterPermitId.HasValue)
                    masterForChild = allPermitsOnJob.FirstOrDefault(x => x.Id == p.MasterPermitId.Value);
                if (p.PermitTemplate?.PermitType?.IsWorkAuthorisation == false && formFieldSchema.Count > 0)
                    formVals = ChildPermitSuggestedFormValuesHelper.MergeSuggestedValues(job, p, masterForChild, formVals);
            }

            var hasClientSignOff = WaAmendmentSignOffHelper.HasClientSignOffForPermit(p);

            bool? stillRequiredByWa = null;
            var permitTypeId = p.PermitTemplate?.PermitTypeId;
            if (!paperPermitMode && p.MasterPermitId.HasValue && p.PermitTemplate?.PermitType?.IsWorkAuthorisation == false && permitTypeId.HasValue)
            {
                var master = allPermitsOnJob.FirstOrDefault(x => x.Id == p.MasterPermitId.Value);
                if (master?.PermitTemplate?.PermitType?.IsWorkAuthorisation == true)
                    stillRequiredByWa = WorkAuthorizationRequestablePermitsHelper.IsPermitTypeRequiredByMasterChecklist(
                        master, permitTypeId.Value, scopedPermitTypes, workAuthRules);
            }

            List<PermitFormFieldSchemaDto>? formFieldsOut = paperPermitMode ? null : (formFieldSchema.Count > 0 ? formFieldSchema : null);
            Dictionary<string, string>? formValsOut = paperPermitMode ? null : (formVals.Count > 0 ? formVals : null);

            list.Add(new JobPermitDto
            {
                Id = p.Id,
                PermitNumber = p.PermitNumber,
                Status = p.Status,
                PermitTypeId = permitTypeId,
                PermitTemplateName = PermitTemplateDurationHelper.PrimaryDisplayName(p.PermitTemplate),
                RequestedAt = p.RequestedAt,
                ApprovedAt = p.ApprovedAt,
                ValidFrom = p.ValidFrom,
                ValidTo = hasClientSignOff ? EffectivePermitValidToForApi(p, allPermitsOnJob, utcNow) : null,
                MasterPermitId = p.MasterPermitId,
                IsWorkAuthorisation = p.PermitTemplate?.PermitType?.IsWorkAuthorisation ?? false,
                HasClientSignOff = hasClientSignOff,
                PendingWaAmendmentSignOff = paperPermitMode ? false : p.PendingWaAmendmentSignOff,
                StillRequiredByWorkAuthorisation = stillRequiredByWa,
                TriggersPermitTypeIds = triggerIds.Count > 0 ? triggerIds : null,
                TriggersPermitTypeNames = triggerNames,
                RequestableWorkPermitTypeIds = requestableIds,
                RequestableWorkPermitTypeNames = requestableNames ?? new List<string>(),
                Attachments = p.Attachments.OrderByDescending(a => a.UploadedAt).Select(a => new JobPermitAttachmentDto
                {
                    Id = a.Id,
                    FileName = a.FileName ?? "",
                    UploadedAt = a.UploadedAt
                }).ToList(),
                ChecklistItems = paperPermitMode ? new List<ChecklistItemDto>() : ParseChecklistItems(p.PermitTemplate?.ChecklistJson, p.ChecklistSnapshotJson),
                FormFields = formFieldsOut,
                FormValues = formValsOut,
                PaperPermitNumber = p.PaperPermitNumber,
                PaperClientSignedOffAt = p.PaperClientSignedOffAt
            });
        }

        return list;
    }

    private static List<string> ParsePhotosJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            var arr = System.Text.Json.JsonSerializer.Deserialize<string[]>(json);
            return arr?.ToList() ?? new List<string>();
        }
        catch { return new List<string>(); }
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
        catch { return null; }
    }

    private static bool HasClientSignOff(string? permitStatus, string? checklistSnapshotJson, IEnumerable<string>? attachmentFileNames = null)
    {
        var st = (permitStatus ?? "").Trim();
        if (PermitStatus.IsActiveLike(st))
            return true;
        if (attachmentFileNames != null && attachmentFileNames.Any(n => (n ?? string.Empty).ToLower().Contains("signature")))
            return true;
        if (string.IsNullOrWhiteSpace(checklistSnapshotJson)) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(checklistSnapshotJson);
            if (!doc.RootElement.TryGetProperty("declaration", out var declaration)) return false;
            if (!declaration.TryGetProperty("siteAcknowledgement", out var siteAck)) return false;
            var hasSignedDate = siteAck.TryGetProperty("signedDateTime", out var signedDt) && signedDt.ValueKind == System.Text.Json.JsonValueKind.String && !string.IsNullOrWhiteSpace(signedDt.GetString());
            var hasSigB64 = siteAck.TryGetProperty("signatureImageBase64", out var sigB64) && sigB64.ValueKind == System.Text.Json.JsonValueKind.String && !string.IsNullOrWhiteSpace(sigB64.GetString());
            var hasSigUrl = siteAck.TryGetProperty("signatureImageUrl", out var sigUrl) && sigUrl.ValueKind == System.Text.Json.JsonValueKind.String && !string.IsNullOrWhiteSpace(sigUrl.GetString());
            return hasSignedDate && (hasSigB64 || hasSigUrl);
        }
        catch
        {
            return false;
        }
    }

    private static List<ChecklistItemDto> ParseChecklistItems(string? templateJson, string? snapshotJson)
    {
        var items = new List<ChecklistItemDto>();
        var template = ParseChecklistItemsFromJson(templateJson);
        var snapshot = ParseChecklistSnapshotFromJson(snapshotJson);
        if (template.Count > 0)
        {
            foreach (var t in template)
            {
                var snap = snapshot.FirstOrDefault(s => string.Equals(s.Id, t.Id, StringComparison.OrdinalIgnoreCase));
                items.Add(new ChecklistItemDto { Id = t.Id, Label = t.Label, Checked = snap.Checked });
            }
        }
        else if (snapshot.Count > 0)
        {
            foreach (var s in snapshot)
                items.Add(new ChecklistItemDto { Id = s.Id, Label = s.Label, Checked = s.Checked });
        }
        return items;
    }

    private static List<(string Id, string Label)> ParseChecklistItemsFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<(string, string)>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var list = new List<(string, string)>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var id = e.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                var label = e.TryGetProperty("label", out var lbProp) ? lbProp.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(id) || !string.IsNullOrEmpty(label))
                    list.Add((id, label));
            }
            return list;
        }
        catch { return new List<(string, string)>(); }
    }

    private static List<(string Id, string Label, bool Checked)> ParseChecklistSnapshotFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<(string, string, bool)>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var list = new List<(string, string, bool)>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var id = e.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                var label = e.TryGetProperty("label", out var lbProp) ? lbProp.GetString() ?? "" : "";
                var chk = e.TryGetProperty("checked", out var chkProp) && chkProp.GetBoolean();
                list.Add((id, label, chk));
            }
            return list;
        }
        catch { return new List<(string, string, bool)>(); }
    }
}

public class AssignTechnicianRequest
{
    public string UserId { get; set; } = string.Empty;
    public bool IsPermitManager { get; set; }
}

public class SetPermitManagerRequest
{
    public bool IsPermitManager { get; set; }
}

public class AssignableTechnicianDto
{
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public List<Guid> BadgeIds { get; set; } = new();
}

public class AssignableTechniciansResponse
{
    public List<AssignableTechnicianDto> Technicians { get; set; } = new();
    public List<Guid> RequiredBadgeIds { get; set; } = new();
    public List<string> RequiredBadgeNames { get; set; } = new();
}
