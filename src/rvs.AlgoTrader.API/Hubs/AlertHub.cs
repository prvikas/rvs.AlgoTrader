using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace rvs.AlgoTrader.API.Hubs;

/// <summary>
/// Real-time alert streaming hub.
/// Pushes AlertTriggered, MonitoringAlertTriggered, StreamDisconnected/Reconnected.
/// </summary>
[Authorize]
public class AlertHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "alerts");
        await base.OnConnectedAsync();
    }
}
