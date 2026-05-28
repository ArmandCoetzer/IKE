using Microsoft.AspNetCore.SignalR;
using Ike.Api.Hubs;

namespace Ike.Api.Services;

public class RealtimeHub : IRealtimeHub
{
    private readonly IHubContext<AppHub> _hub;

    public RealtimeHub(IHubContext<AppHub> hub)
    {
        _hub = hub;
    }

    public Task NotifyJobCardUpdatedAsync(Guid jobCardId, CancellationToken ct = default)
    {
        return _hub.Clients.All.SendAsync(AppHub.JobCardUpdated, jobCardId.ToString(), ct);
    }

    public Task NotifyUserNotificationAsync(string userId, CancellationToken ct = default)
    {
        return _hub.Clients.User(userId).SendAsync(AppHub.NotificationReceived, ct);
    }
}
