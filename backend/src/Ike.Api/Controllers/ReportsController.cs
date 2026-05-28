using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.DTOs.Invoices;
using Ike.Api.Models;
using Ike.Api.Services;

namespace Ike.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentService _documentService;

    public ReportsController(ApplicationDbContext db, ICurrentUserService currentUser, IDocumentService documentService)
    {
        _db = db;
        _currentUser = currentUser;
        _documentService = documentService;
    }

    /// <summary>Client progress report: job cards with labour hours from invoices. Scoped to client's company. Optional filters for Admin.</summary>
    [HttpGet("progress")]
    [Authorize(Policy = "RequireViewReports")]
    [ProducesResponseType(typeof(ProgressReportDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProgressReportDto>> GetProgressReport(
        [FromQuery] Guid? companyId, [FromQuery] Guid? siteId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        var report = await BuildProgressReportAsync(companyId, siteId, from, to, ct);
        return Ok(report);
    }

    /// <summary>Download progress report as PDF.</summary>
    [HttpGet("progress/pdf")]
    [Authorize(Policy = "RequireViewReports")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProgressReportPdf(
        [FromQuery] Guid? companyId, [FromQuery] Guid? siteId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        var report = await BuildProgressReportAsync(companyId, siteId, from, to, ct);
        var pdf = await _documentService.GetProgressReportPdfAsync(report, ct);
        return File(pdf, "application/pdf", $"progress-report-{DateTime.UtcNow:yyyyMMdd}.pdf");
    }

    [HttpGet("summary")]
    [Authorize(Policy = "RequireViewReports")]
    [ProducesResponseType(typeof(ReportsSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReportsSummaryDto>> GetSummary(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var fromDate = from ?? DateTime.UtcNow.Date.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow.Date.AddDays(1);

        IQueryable<JobCard> jobQuery = _db.JobCards.AsNoTracking()
            .Include(j => j.Site).ThenInclude(s => s!.Company);
        if (companyId.HasValue)
        {
            if (isClient)
                jobQuery = jobQuery.Where(j => j.Site != null && j.Site.CompanyId == companyId);
            else
                jobQuery = jobQuery.Where(j => j.Site != null && j.Site.Company != null && j.Site.Company.ParentCompanyId == companyId);
        }
        jobQuery = jobQuery.Where(j => j.CreatedAt >= fromDate && j.CreatedAt <= toDate);
        var jobIds = await jobQuery.Select(j => j.Id).ToListAsync(ct);

        var permitsPending = await _db.JobPermits.CountAsync(p => jobIds.Contains(p.JobCardId) && p.Status == PermitStatus.Pending, ct);
        var permitsApproved = await _db.JobPermits.CountAsync(p => jobIds.Contains(p.JobCardId) && p.Status == PermitStatus.Approved, ct);
        var incidents = await _db.IncidentReports.CountAsync(i => jobIds.Contains(i.JobCardId), ct);
        var activeJobCards = await jobQuery.CountAsync(j => j.Status != JobCardStatus.Completed && j.Status != JobCardStatus.Cancelled, ct);
        var completedJobCards = await jobQuery.CountAsync(j => j.Status == JobCardStatus.Completed, ct);

        return Ok(new ReportsSummaryDto
        {
            PermitsPending = permitsPending,
            PermitsApproved = permitsApproved,
            Incidents = incidents,
            ActiveJobCards = activeJobCards,
            CompletedJobCards = completedJobCards
        });
    }

    [HttpGet("permits-by-type-status")]
    [Authorize(Policy = "RequireViewReports")]
    [ProducesResponseType(typeof(List<PermitByTypeStatusDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PermitByTypeStatusDto>>> GetPermitsByTypeStatus(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var fromDate = from ?? DateTime.UtcNow.Date.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow.Date.AddDays(1);

        IQueryable<JobCard> jobQuery = _db.JobCards.AsNoTracking().Include(j => j.Site).ThenInclude(s => s!.Company);
        if (companyId.HasValue)
        {
            if (isClient)
                jobQuery = jobQuery.Where(j => j.Site != null && j.Site.CompanyId == companyId);
            else
                jobQuery = jobQuery.Where(j => j.Site != null && j.Site.Company != null && j.Site.Company.ParentCompanyId == companyId);
        }
        jobQuery = jobQuery.Where(j => j.CreatedAt >= fromDate && j.CreatedAt <= toDate);
        var jobIds = await jobQuery.Select(j => j.Id).ToListAsync(ct);

        var permits = await _db.JobPermits.AsNoTracking()
            .Include(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .Where(p => jobIds.Contains(p.JobCardId))
            .ToListAsync(ct);

        var grouped = permits
            .GroupBy(p => new { TypeName = p.PermitTemplate?.PermitType?.Name ?? "Unknown", p.Status })
            .Select(g => new PermitByTypeStatusDto { TypeName = g.Key.TypeName, Status = g.Key.Status, Count = g.Count() })
            .OrderBy(x => x.TypeName).ThenBy(x => x.Status)
            .ToList();

        return Ok(grouped);
    }

    [HttpGet("incidents")]
    [Authorize(Policy = "RequireViewReports")]
    [ProducesResponseType(typeof(List<ReportsIncidentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ReportsIncidentDto>>> GetIncidents(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var fromDate = from ?? DateTime.UtcNow.Date.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow.Date.AddDays(1);

        IQueryable<IncidentReport> query = _db.IncidentReports.AsNoTracking()
            .Include(i => i.JobCard).ThenInclude(j => j!.Site).ThenInclude(s => s!.Company)
            .Include(i => i.ReportedByUser);
        if (companyId.HasValue)
        {
            if (isClient)
                query = query.Where(i => i.JobCard != null && i.JobCard.Site != null && i.JobCard.Site.CompanyId == companyId);
            else
                query = query.Where(i => i.JobCard != null && i.JobCard.Site != null && i.JobCard.Site.Company != null && i.JobCard.Site.Company.ParentCompanyId == companyId);
        }
        var list = await query
            .Where(i => i.CreatedAt >= fromDate && i.CreatedAt <= toDate)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new ReportsIncidentDto
            {
                Id = i.Id,
                JobCardId = i.JobCardId,
                JobCardNumber = i.JobCard != null ? i.JobCard.JobCardNumber : null,
                SiteName = i.JobCard != null && i.JobCard.Site != null ? i.JobCard.Site.Name : null,
                Description = i.Description,
                Severity = i.Severity,
                ReportedByUserName = i.ReportedByUser != null ? (i.ReportedByUser.FullName ?? i.ReportedByUser.Email) : null,
                CreatedAt = i.CreatedAt
            })
            .ToListAsync(ct);
        return Ok(list);
    }

    private async Task<ProgressReportDto> BuildProgressReportAsync(Guid? filterCompanyId, Guid? filterSiteId, DateTime? filterFrom, DateTime? filterTo, CancellationToken ct)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (companyId.HasValue && filterCompanyId.HasValue)
        {
            var allowed = isClient
                ? filterCompanyId == companyId
                : await _db.Companies.AsNoTracking().AnyAsync(c => c.Id == filterCompanyId.Value && c.ParentCompanyId == companyId, ct);
            if (!allowed)
                filterCompanyId = null;
        }
        IQueryable<JobCard> jobQuery = _db.JobCards.AsNoTracking()
            .Include(j => j.Site)
            .ThenInclude(s => s!.Company)
            .Include(j => j.ServiceRequest);
        if (companyId.HasValue)
        {
            if (isClient)
                jobQuery = jobQuery.Where(j => j.Site != null && j.Site.CompanyId == companyId);
            else
                jobQuery = jobQuery.Where(j => j.Site != null && j.Site.Company != null && j.Site.Company.ParentCompanyId == companyId);
        }
        if (filterCompanyId.HasValue)
            jobQuery = jobQuery.Where(j => j.Site != null && j.Site.CompanyId == filterCompanyId.Value);
        if (filterSiteId.HasValue)
            jobQuery = jobQuery.Where(j => j.SiteId == filterSiteId.Value);
        if (filterFrom.HasValue)
            jobQuery = jobQuery.Where(j => j.CreatedAt >= filterFrom.Value);
        if (filterTo.HasValue)
            jobQuery = jobQuery.Where(j => j.CreatedAt <= filterTo.Value.AddDays(1));
        var jobs = await jobQuery.OrderByDescending(j => j.CreatedAt).ToListAsync(ct);
        var jobIds = jobs.Select(j => j.Id).ToList();
        var invoices = await _db.Invoices.AsNoTracking()
            .Include(i => i.LineItems)
            .Where(i => jobIds.Contains(i.JobCardId))
            .ToListAsync(ct);
        var items = new List<ProgressReportItemDto>();
        foreach (var j in jobs)
        {
            var invs = invoices.Where(i => i.JobCardId == j.Id).ToList();
            var labourHours = invs
                .SelectMany(i => i.LineItems)
                .Where(li => string.Equals(li.LineType, "Labour", StringComparison.OrdinalIgnoreCase))
                .Sum(li => li.Quantity);
            var totalAmount = invs.Sum(i => i.Amount);
            items.Add(new ProgressReportItemDto
            {
                JobCardId = j.Id,
                JobCardNumber = j.JobCardNumber,
                ServiceRequestNumber = j.ServiceRequest?.RequestNumber,
                ClientName = j.Site?.Company?.Name,
                SiteName = j.Site?.Name,
                Description = j.Description,
                Status = j.Status,
                CreatedAt = j.CreatedAt,
                LabourHours = labourHours,
                TotalAmount = invs.FirstOrDefault()?.Currency ?? "ZAR",
                InvoiceAmount = totalAmount,
                HasInvoice = invs.Count > 0
            });
        }
        ClientBudgetSummaryDto? budget = null;
        var budgetCompanyId = isClient ? companyId : filterCompanyId;
        if (budgetCompanyId.HasValue)
        {
            var cb = await _db.ClientBudgets.AsNoTracking()
                .FirstOrDefaultAsync(b => b.CompanyId == budgetCompanyId.Value, ct);
            if (cb != null)
            {
                budget = new ClientBudgetSummaryDto
                {
                    ThresholdAmount = cb.ThresholdAmount,
                    SpentAmount = cb.SpentAmount,
                    Currency = cb.Currency,
                    WorkPaused = cb.WorkPaused
                };
            }
        }

        return new ProgressReportDto
        {
            Items = items,
            TotalLabourHours = items.Sum(i => i.LabourHours),
            TotalAmount = items.Sum(i => i.InvoiceAmount),
            Budget = budget
        };
    }

    [HttpGet("invoices-by-period")]
    [Authorize(Policy = "RequireViewReports")]
    [ProducesResponseType(typeof(List<InvoiceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<InvoiceDto>>> InvoicesByPeriod([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var fromDate = from ?? DateTime.UtcNow.Date.AddMonths(-1);
        var toDate = to ?? DateTime.UtcNow.Date;
        if (fromDate > toDate)
            (fromDate, toDate) = (toDate, fromDate);

        IQueryable<Invoice> query = _db.Invoices.AsNoTracking()
            .Include(i => i.Company)
            .Include(i => i.Site)
            .Include(i => i.JobCard);
        if (companyId.HasValue)
        {
            if (isClient)
                query = query.Where(i => i.CompanyId == companyId);
            else
                query = query.Where(i => i.Company != null && i.Company.ParentCompanyId == companyId);
        }
        var list = await query
            .Where(i => i.CreatedAt >= fromDate && i.CreatedAt <= toDate.AddDays(1))
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new InvoiceDto
            {
                Id = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                JobCardId = i.JobCardId,
                JobCardNumber = i.JobCard != null ? i.JobCard.JobCardNumber : null,
                ClientId = i.CompanyId,
                ClientName = i.Company != null ? i.Company.Name : null,
                SiteId = i.SiteId,
                SiteName = i.Site != null ? i.Site.Name : null,
                Amount = i.Amount,
                Currency = i.Currency,
                Status = i.Status,
                DueDate = i.DueDate,
                SentAt = i.SentAt,
                PaidAt = i.PaidAt,
                CreatedAt = i.CreatedAt,
                Notes = i.Notes
            })
            .ToListAsync(ct);
        return Ok(list);
    }
}

public class ProgressReportDto
{
    public List<ProgressReportItemDto> Items { get; set; } = new();
    public decimal TotalLabourHours { get; set; }
    public decimal TotalAmount { get; set; }
    public ClientBudgetSummaryDto? Budget { get; set; }
}

public class ClientBudgetSummaryDto
{
    public decimal ThresholdAmount { get; set; }
    public decimal SpentAmount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public bool WorkPaused { get; set; }
}

public class ProgressReportItemDto
{
    public Guid JobCardId { get; set; }
    public string JobCardNumber { get; set; } = "";
    public string? ServiceRequestNumber { get; set; }
    public string? ClientName { get; set; }
    public string? SiteName { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public decimal LabourHours { get; set; }
    public string TotalAmount { get; set; } = "";
    public decimal InvoiceAmount { get; set; }
    public bool HasInvoice { get; set; }
}

public class ReportsSummaryDto
{
    public int PermitsPending { get; set; }
    public int PermitsApproved { get; set; }
    public int Incidents { get; set; }
    public int ActiveJobCards { get; set; }
    public int CompletedJobCards { get; set; }
}

public class PermitByTypeStatusDto
{
    public string TypeName { get; set; } = "";
    public string Status { get; set; } = "";
    public int Count { get; set; }
}

public class ReportsIncidentDto
{
    public Guid Id { get; set; }
    public Guid JobCardId { get; set; }
    public string? JobCardNumber { get; set; }
    public string? SiteName { get; set; }
    public string Description { get; set; } = "";
    public string Severity { get; set; } = "";
    public string? ReportedByUserName { get; set; }
    public DateTime CreatedAt { get; set; }
}
