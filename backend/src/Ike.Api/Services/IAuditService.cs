namespace Ike.Api.Services;

public interface IAuditService
{
    Task LogAsync(string action, string entityType, string? entityId = null, string? details = null, CancellationToken ct = default);
}
