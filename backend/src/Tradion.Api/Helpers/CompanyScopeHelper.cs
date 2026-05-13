using Microsoft.EntityFrameworkCore;
using Tradion.Api.Data;
using Tradion.Api.Models;

namespace Tradion.Api.Helpers;

public static class CompanyScopeHelper
{
    /// <summary>
    /// Resolves the main (contractor) company id used for staff notification scoping.
    /// Client companies map to <see cref="Company.ParentCompanyId"/>; main companies map to themselves.
    /// </summary>
    public static async Task<Guid?> ResolveMainCompanyScopeIdAsync(ApplicationDbContext db, Guid companyId, CancellationToken ct = default)
    {
        var row = await db.Companies.AsNoTracking()
            .Where(c => c.Id == companyId)
            .Select(c => new { c.Type, c.ParentCompanyId })
            .FirstOrDefaultAsync(ct);
        if (row == null)
            return null;
        if (row.Type == CompanyType.Main)
            return companyId;
        return row.ParentCompanyId;
    }
}
