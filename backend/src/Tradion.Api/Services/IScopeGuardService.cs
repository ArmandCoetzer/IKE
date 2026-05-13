using Tradion.Api.Models;

namespace Tradion.Api.Services;

public interface IScopeGuardService
{
    Task<bool> CanAccessCompanyAsync(Guid? targetCompanyId, Company? targetCompany, CancellationToken ct = default);
    Task<bool> CanAccessJobCardAsync(Guid jobCardId, CancellationToken ct = default);
    Task<bool> CanAccessPermitAsync(Guid permitId, CancellationToken ct = default);
}
