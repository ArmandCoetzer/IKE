using Microsoft.EntityFrameworkCore;
using Tradion.Api.Data;
using Tradion.Api.Models;

namespace Tradion.Api.Services;

public class ScopeGuardService : IScopeGuardService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public ScopeGuardService(ApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<bool> CanAccessCompanyAsync(Guid? targetCompanyId, Company? targetCompany, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (!companyId.HasValue) return true;
        if (!targetCompanyId.HasValue) return false;

        return isClient
            ? targetCompanyId == companyId
            : targetCompany != null && targetCompany.ParentCompanyId == companyId;
    }

    public async Task<bool> CanAccessJobCardAsync(Guid jobCardId, CancellationToken ct = default)
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

    public async Task<bool> CanAccessPermitAsync(Guid permitId, CancellationToken ct = default)
    {
        var permit = await _db.JobPermits.AsNoTracking()
            .Where(p => p.Id == permitId)
            .Select(p => new { p.JobCardId })
            .FirstOrDefaultAsync(ct);
        if (permit == null) return false;
        return await CanAccessJobCardAsync(permit.JobCardId, ct);
    }
}
