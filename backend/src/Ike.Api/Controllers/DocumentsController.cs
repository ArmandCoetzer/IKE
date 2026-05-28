using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.Helpers;
using Ike.Api.Models;
using Ike.Api.Services;

namespace Ike.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IEmailService _emailService;
    private readonly ICurrentUserService _currentUser;
    private readonly IScopeGuardService _scopeGuard;

    public DocumentsController(
        IDocumentService documentService,
        ApplicationDbContext db,
        IWebHostEnvironment env,
        IEmailService emailService,
        ICurrentUserService currentUser,
        IScopeGuardService scopeGuard)
    {
        _documentService = documentService;
        _db = db;
        _env = env;
        _emailService = emailService;
        _currentUser = currentUser;
        _scopeGuard = scopeGuard;
    }

    [HttpGet("quote/{id:guid}/pdf")]
    [Authorize(Policy = "RequireViewPurchaseOrders")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetQuotePdf(Guid id, CancellationToken ct)
    {
        if (!await CanAccessQuoteInScopeAsync(id, ct))
            return NotFound();
        var bytes = await _documentService.GetQuotePdfAsync(id, ct);
        if (bytes == null || bytes.Length == 0)
            return NotFound();
        return File(bytes, "application/pdf", $"Quote-{id}.pdf");
    }

    [HttpGet("invoice/{id:guid}/pdf")]
    [Authorize(Policy = "RequireViewReports")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvoicePdf(Guid id, CancellationToken ct)
    {
        if (!await CanAccessInvoiceInScopeAsync(id, ct))
            return NotFound();
        var bytes = await _documentService.GetInvoicePdfAsync(id, ct);
        if (bytes == null || bytes.Length == 0)
            return NotFound();
        return File(bytes, "application/pdf", $"Invoice-{id}.pdf");
    }

    [HttpGet("job-card/{id:guid}/pdf")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetJobCardPdf(Guid id, CancellationToken ct = default)
    {
        if (!await CanAccessJobCardInScopeAsync(id, ct))
            return NotFound();
        var bytes = await _documentService.GetJobCardPdfAsync(id, ct);
        if (bytes == null || bytes.Length == 0)
            return NotFound();
        return File(bytes, "application/pdf", $"JobCard-{id}.pdf");
    }

    /// <summary>Email the job card summary PDF to the site client (company contact email).</summary>
    [HttpPost("job-card/{id:guid}/email-client")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EmailJobCardPdfToClient(Guid id, CancellationToken ct = default)
    {
        var job = await _db.JobCards.AsNoTracking()
            .Include(j => j.Site).ThenInclude(s => s!.Company)
            .FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job == null)
            return NotFound();
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (companyId.HasValue && job.Site != null)
        {
            var inScope = isClient
                ? job.Site.CompanyId == companyId
                : (job.Site.CompanyId == companyId || (job.Site.Company != null && job.Site.Company.ParentCompanyId == companyId));
            if (!inScope)
                return NotFound();
        }

        if (string.IsNullOrWhiteSpace(job.Site?.Company?.ContactEmail))
            return BadRequest(new { message = "The client company has no contact email configured." });

        await _emailService.SendJobCardPdfToClientAsync(id, null, ct);
        return NoContent();
    }

    [HttpGet("purchase-order/{id:guid}/pdf")]
    [Authorize(Policy = "RequireViewPurchaseOrders")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPurchaseOrderPdf(Guid id, CancellationToken ct)
    {
        if (!await CanAccessPurchaseOrderInScopeAsync(id, ct))
            return NotFound();
        var bytes = await _documentService.GetPurchaseOrderPdfAsync(id, ct);
        if (bytes == null || bytes.Length == 0)
            return NotFound();
        return File(bytes, "application/pdf", $"PO-{id}.pdf");
    }

    [HttpGet("purchase-order/{id:guid}/client-po")]
    [Authorize(Policy = "RequireViewPurchaseOrders")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPurchaseOrderClientPO(Guid id, CancellationToken ct)
    {
        var po = await _db.PurchaseOrders.AsNoTracking()
            .Include(p => p.Company)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (po == null)
            return NotFound();
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (companyId.HasValue)
        {
            var inScope = isClient
                ? po.CompanyId == companyId
                : po.Company != null && po.Company.ParentCompanyId == companyId;
            if (!inScope)
                return NotFound();
        }
        var filePath = po.ClientPOFilePath;
        var validatedPath = FilePathHelper.ValidateAndNormalize(filePath);
        if (validatedPath == null)
            return NotFound();
        var fullPath = Path.Combine(_env.ContentRootPath, validatedPath);
        if (!System.IO.File.Exists(fullPath))
            return NotFound();
        var contentType = Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fileName = "ClientPO-" + id.ToString("N") + Path.GetExtension(fullPath);
        return File(stream, contentType, fileName);
    }

    private async Task<bool> CanAccessQuoteInScopeAsync(Guid id, CancellationToken ct)
    {
        var quote = await _db.Quotes.AsNoTracking()
            .Include(q => q.Company)
            .FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote == null) return false;
        return await IsCompanyInScopeAsync(quote.CompanyId, quote.Company, ct);
    }

    private async Task<bool> CanAccessInvoiceInScopeAsync(Guid id, CancellationToken ct)
    {
        var invoice = await _db.Invoices.AsNoTracking()
            .Include(i => i.Company)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice == null) return false;
        return await IsCompanyInScopeAsync(invoice.CompanyId, invoice.Company, ct);
    }

    private async Task<bool> CanAccessJobCardInScopeAsync(Guid id, CancellationToken ct)
    {
        return await _scopeGuard.CanAccessJobCardAsync(id, ct);
    }

    private async Task<bool> CanAccessPurchaseOrderInScopeAsync(Guid id, CancellationToken ct)
    {
        var po = await _db.PurchaseOrders.AsNoTracking()
            .Include(p => p.Company)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (po == null) return false;
        return await IsCompanyInScopeAsync(po.CompanyId, po.Company, ct);
    }

    private async Task<bool> IsCompanyInScopeAsync(Guid? entityCompanyId, Company? entityCompany, CancellationToken ct)
    {
        return await _scopeGuard.CanAccessCompanyAsync(entityCompanyId, entityCompany, ct);
    }
}
