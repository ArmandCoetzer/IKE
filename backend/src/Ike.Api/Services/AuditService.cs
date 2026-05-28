using Ike.Api.Data;
using Ike.Api.Models;

namespace Ike.Api.Services;

public class AuditService : IAuditService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public AuditService(ApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task LogAsync(string action, string entityType, string? entityId = null, string? details = null, CancellationToken ct = default)
    {
        var entry = new AuditLog
        {
            Id = Guid.NewGuid(),
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            UserId = _currentUser.UserId,
            CreatedAt = DateTime.UtcNow,
            Details = details != null && details.Length > 2000 ? details[..2000] : details
        };
        _db.AuditLogs.Add(entry);
        await _db.SaveChangesAsync(ct);
    }
}
