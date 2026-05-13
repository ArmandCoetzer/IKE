using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Tradion.Api.Hubs;

[Authorize]
public class AppHub : Hub
{
    /// <summary>
    /// Server sends JobCardUpdated to all clients when a job card is created/updated.
    /// </summary>
    public const string JobCardUpdated = "JobCardUpdated";

    /// <summary>
    /// Server sends NotificationReceived to a specific user when they get a new notification.
    /// </summary>
    public const string NotificationReceived = "NotificationReceived";
}
