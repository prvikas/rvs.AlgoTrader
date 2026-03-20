using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace rvs.AlgoTrader.API.Hubs;

/// <summary>
/// Strategy instance status and signal streaming hub.
/// Pushes: SignalGenerated, StrategyStatusChanged, ColdRestartPauseNotification.
/// </summary>
[Authorize]
public class StrategyHub : Hub
{
    public async Task SubscribeToInstance(Guid instanceId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"strategy:{instanceId}");

    public async Task UnsubscribeFromInstance(Guid instanceId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"strategy:{instanceId}");

    public override async Task OnConnectedAsync()
    {
        // Auto-join "all-strategies" group for global notifications
        await Groups.AddToGroupAsync(Context.ConnectionId, "all-strategies");
        await base.OnConnectedAsync();
    }
}
