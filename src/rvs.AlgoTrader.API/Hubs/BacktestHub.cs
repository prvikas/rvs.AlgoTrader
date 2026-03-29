using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace rvs.AlgoTrader.API.Hubs;

/// <summary>
/// Real-time backtest progress hub.
/// Client subscribes to a jobId group; server pushes BacktestProgress and BacktestCompleted events.
/// </summary>
[Authorize]
public class BacktestHub : Hub
{
    public async Task SubscribeToJob(string jobId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"backtest:{jobId}");

    public async Task UnsubscribeFromJob(string jobId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"backtest:{jobId}");
}
