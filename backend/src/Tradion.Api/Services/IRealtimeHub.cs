namespace Tradion.Api.Services;

public interface IRealtimeHub
{
    Task NotifyJobCardUpdatedAsync(Guid jobCardId, CancellationToken ct = default);
    Task NotifyUserNotificationAsync(string userId, CancellationToken ct = default);
}
