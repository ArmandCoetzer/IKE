using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tradion.Api.Data;
using Tradion.Api.Models;
using Tradion.Api.Services;

namespace Tradion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public DashboardController(ApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet("counts")]
    [ProducesResponseType(typeof(DashboardCountsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardCountsDto>> GetCounts(CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        IQueryable<ServiceRequest> srQuery = _db.ServiceRequests.Include(sr => sr.Site).ThenInclude(s => s!.Company);
        IQueryable<JobCard> jcQuery = _db.JobCards.Include(j => j.Site).ThenInclude(s => s!.Company);
        if (companyId.HasValue)
        {
            if (isClient)
            {
                srQuery = srQuery.Where(sr => sr.Site != null && sr.Site.CompanyId == companyId);
                jcQuery = jcQuery.Where(j => j.Site != null && j.Site.CompanyId == companyId);
            }
            else
            {
                srQuery = srQuery.Where(sr => sr.Site != null && sr.Site.Company != null && sr.Site.Company.ParentCompanyId == companyId);
                jcQuery = jcQuery.Where(j => j.Site != null && j.Site.Company != null && j.Site.Company.ParentCompanyId == companyId);
            }
        }
        var unprocessedStatuses = new[] { ServiceRequestStatus.New, ServiceRequestStatus.Pending, ServiceRequestStatus.Open };
        var unprocessedCount = await srQuery
            .CountAsync(sr => unprocessedStatuses.Contains(sr.Status), ct);
        var ongoingStatuses = new[] { JobCardStatus.Open, JobCardStatus.InProgress, JobCardStatus.InProgressCompact, JobCardStatus.Scheduled };
        var ongoingCount = await jcQuery
            .CountAsync(j => ongoingStatuses.Contains(j.Status), ct);
        var jobCardIds = await jcQuery.Select(j => j.Id).ToListAsync(ct);
        var overdueInvoices = companyId.HasValue
            ? await _db.Invoices.CountAsync(i => i.Status != InvoiceStatus.Paid && i.DueDate < DateTime.UtcNow.Date && jobCardIds.Contains(i.JobCardId), ct)
            : await _db.Invoices.CountAsync(i => i.Status != InvoiceStatus.Paid && i.DueDate < DateTime.UtcNow.Date, ct);
        var requestIdsWithJob = await jcQuery.Where(j => j.ServiceRequestId != null).Select(j => j.ServiceRequestId!.Value).Distinct().ToListAsync(ct);
        var requestsWithoutJob = await srQuery
            .CountAsync(sr => unprocessedStatuses.Contains(sr.Status) && !requestIdsWithJob.Contains(sr.Id), ct);
        var completedJobStatuses = new[] { JobCardStatus.Completed, JobCardStatus.Done, JobCardStatus.Closed };
        var jobIdsWithInvoice = companyId.HasValue
            ? await _db.Invoices.Where(i => jobCardIds.Contains(i.JobCardId)).Select(i => i.JobCardId).Distinct().ToListAsync(ct)
            : await _db.Invoices.Select(i => i.JobCardId).Distinct().ToListAsync(ct);
        var jobsWithoutInvoice = await jcQuery
            .CountAsync(j => completedJobStatuses.Contains(j.Status) && !jobIdsWithInvoice.Contains(j.Id), ct);
        var lowStockParts = 0;
        if (!isClient && companyId.HasValue)
        {
            var companyIds = await _db.Companies
                .Where(c => c.Id == companyId.Value || c.ParentCompanyId == companyId.Value)
                .Select(c => c.Id)
                .ToListAsync(ct);
            lowStockParts = await _db.Parts.CountAsync(
                p => p.CompanyId.HasValue && companyIds.Contains(p.CompanyId.Value) && p.Quantity <= p.ReorderLevel, ct);
        }
        var completedJobsCount = await jcQuery
            .CountAsync(j => completedJobStatuses.Contains(j.Status), ct);
        return Ok(new DashboardCountsDto
        {
            UnprocessedRequests = unprocessedCount,
            OngoingJobCards = ongoingCount,
            OverdueInvoices = overdueInvoices,
            RequestsWithoutJobCard = requestsWithoutJob,
            CompletedJobsWithoutInvoice = jobsWithoutInvoice,
            LowStockPartsCount = lowStockParts,
            CompletedJobsCount = completedJobsCount
        });
    }
}

public class DashboardCountsDto
{
    public int UnprocessedRequests { get; set; }
    public int OngoingJobCards { get; set; }
    public int OverdueInvoices { get; set; }
    public int RequestsWithoutJobCard { get; set; }
    public int CompletedJobsWithoutInvoice { get; set; }
    public int LowStockPartsCount { get; set; }
    public int CompletedJobsCount { get; set; }
}
