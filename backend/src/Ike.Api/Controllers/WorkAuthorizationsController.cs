using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Ike.Api.Data;
using Ike.Api.DTOs.WorkAuthorizations;
using Ike.Api.Helpers;
using Ike.Api.Models;
using Ike.Api.Services;

namespace Ike.Api.Controllers;

[ApiController]
[Route("api/work-authorizations/master-permit")]
[Authorize]
public class WorkAuthorizationsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IWorkAuthorizationDocumentRenderer _renderer;
    private readonly IWorkAuthorizationPermitRulesService _rules;
    private readonly IEmailService _emailService;
    private readonly ICurrentUserService _currentUser;
    private readonly IWebHostEnvironment _env;
    private readonly IAuditService _auditService;
    private readonly IRealtimeHub _realtimeHub;
    private readonly ILogger<WorkAuthorizationsController> _logger;

    public WorkAuthorizationsController(
        ApplicationDbContext db,
        IWorkAuthorizationDocumentRenderer renderer,
        IWorkAuthorizationPermitRulesService rules,
        IEmailService emailService,
        ICurrentUserService currentUser,
        IWebHostEnvironment env,
        IAuditService auditService,
        IRealtimeHub realtimeHub,
        ILogger<WorkAuthorizationsController> logger)
    {
        _db = db;
        _renderer = renderer;
        _rules = rules;
        _emailService = emailService;
        _currentUser = currentUser;
        _env = env;
        _auditService = auditService;
        _realtimeHub = realtimeHub;
        _logger = logger;
    }

    [HttpPost("render-html")]
    [Produces("text/html")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/html")]
    public ActionResult RenderHtml([FromBody] WorkAuthorizationMasterPermitDto permit)
    {
        NormalizeMasterPermitForRules(permit);
        permit.DerivedRequiredPermits = _rules.GetDerivedPermits(permit);
        var html = _renderer.RenderHtml(permit);
        return Content(html, "text/html");
    }

    [HttpPost("render-pdf")]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public IActionResult RenderPdf([FromBody] WorkAuthorizationMasterPermitDto permit)
    {
        NormalizeMasterPermitForRules(permit);
        permit.DerivedRequiredPermits = _rules.GetDerivedPermits(permit);
        var bytes = _renderer.RenderPdf(permit);
        return File(bytes, "application/pdf", $"work-authorisation-{permit.WorkAuthorizationNumber}.pdf");
    }

    /// <summary>Recomputes required child permits from the current (unsaved) WA payload — same labels as persistence and PDF.</summary>
    [HttpPost("derive-required-permits")]
    [ProducesResponseType(typeof(List<WorkAuthorizationDerivedPermitDto>), StatusCodes.Status200OK)]
    public ActionResult<List<WorkAuthorizationDerivedPermitDto>> DeriveRequiredPermits([FromBody] WorkAuthorizationMasterPermitDto permit)
    {
        NormalizeMasterPermitForRules(permit);
        return Ok(_rules.GetDerivedPermits(permit));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SaveToPermit(Guid id, [FromBody] WorkAuthorizationMasterPermitDto permit, CancellationToken ct)
    {
        if (!await CanAccessPermitInScopeAsync(id, ct))
            return NotFound(new { message = "Job permit not found." });
        var jobPermit = await _db.JobPermits
            .Include(p => p.Attachments)
            .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (jobPermit == null) return NotFound(new { message = "Job permit not found." });
        if (jobPermit.PermitTemplate?.PermitType?.IsWorkAuthorisation != true)
            return BadRequest(new { message = "This permit is not a Work Authorisation." });
        if (await PaidJobCardLockHelper.IsLockedAsync(_db, jobPermit.JobCardId, ct))
            return BadRequest(new { message = PaidJobCardLockHelper.UserMessage });
        var jobForPaper = await _db.JobCards.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobPermit.JobCardId, ct);
        if (jobForPaper?.PaperPermitMode == true)
            return BadRequest(new { message = "This job uses paper permits; the digital Work Authorisation form is not available." });

        permit.PermitGuid = id;
        var waNumber = string.IsNullOrWhiteSpace(permit.WorkAuthorizationNumber)
            ? $"WA-{DateTime.UtcNow:yyyyMMdd}-{id.ToString("N")[..6]}"
            : permit.WorkAuthorizationNumber.Trim();
        permit.WorkAuthorizationNumber = waNumber;

        var template = WorkAuthorizationMasterPermitDefaults.CreateTemplate(id, waNumber);
        WorkAuthorizationMasterPermitDefaults.MergeInto(permit, template);
        permit.Declaration ??= new WorkAuthorizationDeclarationSectionDto();
        permit.Declaration.DeclarationText = WorkAuthorizationMasterPermitDefaults.DeclarationTextFull;

        permit.ModifiedDateUtc = DateTime.UtcNow;
        permit.DerivedRequiredPermits = _rules.GetDerivedPermits(permit);

        var previousDto = WaAmendmentSignOffHelper.DeserializeWaPayload(jobPermit.ChecklistSnapshotJson);
        var attachmentNames = jobPermit.Attachments?.Select(a => a.FileName);
        var hadPreviousSignOff = WaAmendmentSignOffHelper.WaPayloadIndicatesClientSignOff(previousDto, attachmentNames);
        var oldBaseline = jobPermit.WaSignedBusinessContentHash;
        if (string.IsNullOrWhiteSpace(oldBaseline) && hadPreviousSignOff && previousDto != null)
            oldBaseline = WaAmendmentSignOffHelper.ComputeBusinessContentHash(previousDto);

        var newHash = WaAmendmentSignOffHelper.ComputeBusinessContentHash(permit);
        if (hadPreviousSignOff && !string.IsNullOrWhiteSpace(oldBaseline)
            && !string.Equals(newHash, oldBaseline, StringComparison.OrdinalIgnoreCase))
        {
            jobPermit.PendingWaAmendmentSignOff = true;
            WaAmendmentSignOffHelper.StripSiteClientSignOffFromPayload(permit);
            var st = (jobPermit.Status ?? "").Trim();
            if (PermitStatus.IsActiveLike(st))
                jobPermit.Status = PermitStatus.Captured;
        }

        jobPermit.ChecklistSnapshotJson = JsonSerializer.Serialize(permit);

        if (!string.Equals(jobPermit.Status, PermitStatus.Active, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(jobPermit.Status, PermitStatus.Closed, StringComparison.OrdinalIgnoreCase))
            jobPermit.Status = PermitStatus.Captured;

        await _db.SaveChangesAsync(ct);
        var actingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await WorkAuthorizationChildPermitSyncHelper.SyncAfterWorkAuthorisationSavedAsync(
            _db, id, _rules, _env, _auditService, actingUserId, ct);
        await _realtimeHub.NotifyJobCardUpdatedAsync(jobPermit.JobCardId, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WorkAuthorizationMasterPermitDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkAuthorizationMasterPermitDto>> Get(Guid id, CancellationToken ct)
    {
        if (!await CanAccessPermitInScopeAsync(id, ct))
            return NotFound(new { message = "Job permit not found." });
        var jobPermit = await _db.JobPermits.AsNoTracking()
            .Include(p => p.Attachments)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (jobPermit == null) return NotFound(new { message = "Job permit not found." });

        WorkAuthorizationMasterPermitDto? existing = null;
        if (!string.IsNullOrWhiteSpace(jobPermit.ChecklistSnapshotJson))
            existing = JsonSerializer.Deserialize<WorkAuthorizationMasterPermitDto>(jobPermit.ChecklistSnapshotJson);

        var hydrated = HydrateMasterPermit(jobPermit, existing);
        return Ok(hydrated);
    }

    [HttpGet("{id:guid}/document")]
    [Produces("text/html")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/html")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetDocument(Guid id, CancellationToken ct)
    {
        if (!await CanAccessPermitInScopeAsync(id, ct))
            return NotFound(new { message = "Master permit payload not found for this permit." });
        var jobPermit = await _db.JobPermits
            .AsNoTracking()
            .Include(p => p.Attachments)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (jobPermit == null || string.IsNullOrWhiteSpace(jobPermit.ChecklistSnapshotJson))
            return NotFound(new { message = "Master permit payload not found for this permit." });

        var permit = JsonSerializer.Deserialize<WorkAuthorizationMasterPermitDto>(jobPermit.ChecklistSnapshotJson);
        if (permit == null)
            return NotFound(new { message = "Invalid permit payload." });

        permit = HydrateMasterPermit(jobPermit, permit);
        var html = _renderer.RenderHtml(permit);
        return Content(html, "text/html");
    }

    [HttpGet("{id:guid}/document-pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetDocumentPdf(Guid id, CancellationToken ct)
    {
        if (!await CanAccessPermitInScopeAsync(id, ct))
            return NotFound(new { message = "Master permit payload not found for this permit." });
        var jobPermit = await _db.JobPermits.AsNoTracking()
            .Include(p => p.Attachments)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (jobPermit == null || string.IsNullOrWhiteSpace(jobPermit.ChecklistSnapshotJson))
            return NotFound(new { message = "Master permit payload not found for this permit." });

        var permit = JsonSerializer.Deserialize<WorkAuthorizationMasterPermitDto>(jobPermit.ChecklistSnapshotJson);
        if (permit == null)
            return NotFound(new { message = "Invalid permit payload." });

        permit = HydrateMasterPermit(jobPermit, permit);
        var pdf = _renderer.RenderPdf(permit);
        return File(pdf, "application/pdf", $"work-authorisation-{permit.WorkAuthorizationNumber}.pdf");
    }

    [HttpPost("{id:guid}/email-client")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> EmailClient(Guid id, CancellationToken ct)
    {
        if (!await CanAccessPermitInScopeAsync(id, ct))
            return NotFound(new { message = "Master permit not found." });
        var jobPermit = await _db.JobPermits.AsNoTracking()
            .Include(p => p.Attachments)
            .Include(p => p.JobCard).ThenInclude(j => j!.Site).ThenInclude(s => s!.Company)
            .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (jobPermit == null || string.IsNullOrWhiteSpace(jobPermit.ChecklistSnapshotJson))
            return NotFound(new { message = "Master permit not found." });
        if (await PaidJobCardLockHelper.IsLockedAsync(_db, jobPermit.JobCardId, ct))
            return BadRequest(new { message = PaidJobCardLockHelper.UserMessage });

        WorkAuthorizationMasterPermitDto? permit;
        try
        {
            permit = JsonSerializer.Deserialize<WorkAuthorizationMasterPermitDto>(jobPermit.ChecklistSnapshotJson);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Master permit email failed: invalid WA payload for permit {PermitId}.", id);
            return BadRequest(ApiResponseBodies.Message("The Work Authorisation payload is invalid. Re-open the permit, save it again, and then retry emailing the client."));
        }
        if (permit == null)
            return BadRequest(ApiResponseBodies.Message("The Work Authorisation payload is invalid. Re-open the permit, save it again, and then retry emailing the client."));

        var siteAck = permit.Declaration?.SiteAcknowledgement;
        var hasClientSignOff = siteAck?.SignedDateTime.HasValue == true &&
                               (!string.IsNullOrWhiteSpace(siteAck.SignatureImageBase64) || !string.IsNullOrWhiteSpace(siteAck.SignatureImageUrl));
        if (!hasClientSignOff)
        {
            // Backward-compatible fallback: allow signature file uploads as client sign-off evidence.
            hasClientSignOff = jobPermit.Attachments.Any(a => (a.FileName ?? string.Empty).ToLower().Contains("signature"));
        }
        if (!hasClientSignOff)
            return BadRequest(new { message = "Client sign-off is required before emailing the permit." });

        var recipient = jobPermit.JobCard?.Site?.Company?.ContactEmail;
        if (string.IsNullOrWhiteSpace(recipient))
            return BadRequest(ApiResponseBodies.Message("The client contact email is missing. Add a contact email on the client profile before emailing the permit."));

        permit = HydrateMasterPermit(jobPermit, permit);
        byte[] pdfBytes;
        try
        {
            _logger.LogInformation(
                "Emailing Work Authorisation master permit {PermitId} for job {JobCardId} to {Recipient}.",
                id,
                jobPermit.JobCardId,
                recipient);
            pdfBytes = _renderer.RenderPdf(permit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Master permit email failed: PDF rendering failed for permit {PermitId}.", id);
            return BadRequest(ApiResponseBodies.Message("The Work Authorisation PDF could not be generated. Check that the permit has valid saved details and signatures, then retry."));
        }

        if (pdfBytes.Length == 0)
            return BadRequest(ApiResponseBodies.Message("The Work Authorisation PDF could not be generated because the rendered file was empty."));

        var sent = await _emailService.SendPermitDocumentationPackageToClientAsync(id, pdfBytes,
            $"work-authorisation-{permit.WorkAuthorizationNumber}.pdf", null, ct);
        if (!sent)
            return BadRequest(ApiResponseBodies.Message("Permit email was not sent. Check the client contact email, SMTP settings, and uploaded permit document files."));
        return NoContent();
    }

    private void NormalizeMasterPermitForRules(WorkAuthorizationMasterPermitDto permit)
    {
        var id = permit.PermitGuid != Guid.Empty ? permit.PermitGuid : Guid.NewGuid();
        var wa = string.IsNullOrWhiteSpace(permit.WorkAuthorizationNumber) ? "WA-PREVIEW" : permit.WorkAuthorizationNumber.Trim();
        var template = WorkAuthorizationMasterPermitDefaults.CreateTemplate(id, wa);
        WorkAuthorizationMasterPermitDefaults.MergeInto(permit, template);
        permit.PermitGuid = id;
        permit.WorkAuthorizationNumber = wa;
        permit.Declaration ??= new WorkAuthorizationDeclarationSectionDto();
        if (string.IsNullOrWhiteSpace(permit.Declaration.DeclarationText))
            permit.Declaration.DeclarationText = WorkAuthorizationMasterPermitDefaults.DeclarationTextFull;
    }

    private WorkAuthorizationMasterPermitDto HydrateMasterPermit(JobPermit jobPermit, WorkAuthorizationMasterPermitDto? existing)
    {
        var waNumber = existing?.WorkAuthorizationNumber;
        if (string.IsNullOrWhiteSpace(waNumber))
            waNumber = $"WA-{DateTime.UtcNow:yyyyMMdd}-{jobPermit.Id.ToString("N")[..6]}";

        var template = WorkAuthorizationMasterPermitDefaults.CreateTemplate(jobPermit.Id, waNumber);

        if (existing == null)
        {
            template.DerivedRequiredPermits = _rules.GetDerivedPermits(template);
            MergeSignatureImagesFromAttachments(jobPermit, template);
            return template;
        }

        WorkAuthorizationMasterPermitDefaults.MergeInto(existing, template);
        existing.PermitGuid = jobPermit.Id;
        if (string.IsNullOrWhiteSpace(existing.WorkAuthorizationNumber))
            existing.WorkAuthorizationNumber = waNumber;

        existing.Declaration ??= new WorkAuthorizationDeclarationSectionDto();
        existing.Declaration.DeclarationText = WorkAuthorizationMasterPermitDefaults.DeclarationTextFull;

        existing.DerivedRequiredPermits = _rules.GetDerivedPermits(existing);
        MergeSignatureImagesFromAttachments(jobPermit, existing);
        return existing;
    }

    /// <summary>
    /// When signatures were captured as uploaded images only, embed them as base64 so HTML/PDF renderers show them.
    /// Matches filenames when possible; otherwise assigns a single orphan signature image to client (site) acknowledgement.
    /// </summary>
    private void MergeSignatureImagesFromAttachments(JobPermit jobPermit, WorkAuthorizationMasterPermitDto dto)
    {
        var images = LoadPermitSignatureCandidateImages(jobPermit);
        if (images.Count == 0) return;

        var available = images.ToList();

        bool IsEmpty(string? b64) => string.IsNullOrWhiteSpace(b64);

        void Consume(WorkAuthorizationSignatureDto sig, params string[] tokens)
        {
            if (!IsEmpty(sig.SignatureImageBase64)) return;
            foreach (var t in tokens)
            {
                var idx = available.FindIndex(i => i.FileName.Contains(t, StringComparison.OrdinalIgnoreCase));
                if (idx < 0) continue;
                sig.SignatureImageBase64 = Convert.ToBase64String(available[idx].Bytes);
                available.RemoveAt(idx);
                return;
            }
        }

        dto.Declaration.IssuingAuthority ??= new WorkAuthorizationSignatureDto();
        dto.Declaration.PerformingAuthority ??= new WorkAuthorizationSignatureDto();
        dto.Declaration.SiteAcknowledgement ??= new WorkAuthorizationSignatureDto();

        Consume(dto.Declaration.IssuingAuthority, "issuing", "issuer", "issue");
        Consume(dto.Declaration.PerformingAuthority, "performing", "performer", "perform");
        Consume(dto.Declaration.SiteAcknowledgement, "client", "site", "acknowledgement", "acknowledge");

        var site = dto.Declaration.SiteAcknowledgement;
        if (IsEmpty(site.SignatureImageBase64))
        {
            var idx = available.FindIndex(i =>
                i.FileName.Contains("signature", StringComparison.OrdinalIgnoreCase)
                || i.FileName.Contains("sign-off", StringComparison.OrdinalIgnoreCase)
                || i.FileName.Contains("signoff", StringComparison.OrdinalIgnoreCase)
                || i.FileName.Contains("signed", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                site.SignatureImageBase64 = Convert.ToBase64String(available[idx].Bytes);
                available.RemoveAt(idx);
            }
        }

        if (IsEmpty(site.SignatureImageBase64) && available.Count == 1)
            site.SignatureImageBase64 = Convert.ToBase64String(available[0].Bytes);
    }

    private List<(string FileName, byte[] Bytes)> LoadPermitSignatureCandidateImages(JobPermit jobPermit)
    {
        var list = new List<(string FileName, byte[] Bytes)>();
        foreach (var att in jobPermit.Attachments ?? Enumerable.Empty<JobPermitAttachment>())
        {
            var ext = Path.GetExtension(att.FileName ?? "").ToLowerInvariant();
            if (ext is not ".png" and not ".jpg" and not ".jpeg") continue;
            var rel = FilePathHelper.ValidateAndNormalize(att.FilePath);
            if (rel == null) continue;
            var full = Path.Combine(_env.ContentRootPath, rel);
            if (!System.IO.File.Exists(full)) continue;
            try
            {
                var b = System.IO.File.ReadAllBytes(full);
                if (b.Length > 0)
                    list.Add((att.FileName ?? Path.GetFileName(rel), b));
            }
            catch
            {
                // ignore
            }
        }

        return list;
    }

    private async Task<bool> CanAccessPermitInScopeAsync(Guid permitId, CancellationToken ct)
    {
        var permit = await _db.JobPermits.AsNoTracking()
            .Include(p => p.JobCard).ThenInclude(j => j!.Site).ThenInclude(s => s!.Company)
            .FirstOrDefaultAsync(p => p.Id == permitId, ct);
        if (permit?.JobCard?.Site == null)
            return false;

        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (!companyId.HasValue)
            return true;

        var site = permit.JobCard.Site;
        return isClient
            ? site.CompanyId == companyId
            : (site.CompanyId == companyId || (site.Company != null && site.Company.ParentCompanyId == companyId));
    }

}
