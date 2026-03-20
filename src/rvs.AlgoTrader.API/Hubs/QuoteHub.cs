using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace rvs.AlgoTrader.API.Hubs;

/// <summary>
/// Real-time market quote streaming hub.
/// Clients subscribe to symbols and receive live LTP/OHLCV updates.
/// </summary>
[Authorize]
public class QuoteHub(ILogger<QuoteHub> logger) : Hub
{

    public async Task SubscribeToSymbols(IEnumerable<string> symbols)
    {
        foreach (var symbol in symbols)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"quote:{symbol}");
            logger.LogDebug("[QuoteHub] {Connection} subscribed to {Symbol}", Context.ConnectionId, symbol);
        }
    }

    public async Task UnsubscribeFromSymbols(IEnumerable<string> symbols)
    {
        foreach (var symbol in symbols)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"quote:{symbol}");
    }

    public override async Task OnConnectedAsync()
    {
        logger.LogInformation("[QuoteHub] Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("[QuoteHub] Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
