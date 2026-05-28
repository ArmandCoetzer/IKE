namespace Ike.Api.Services;

public interface ICurrentUserService
{
    string? UserId { get; }
    Task<(Guid? CompanyId, bool IsClient)> GetClientScopeAsync(CancellationToken ct = default);
}
