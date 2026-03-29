using Microsoft.AspNetCore.SignalR;
using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.API.Hubs;

namespace rvs.AlgoTrader.API.Services;

/// <summary>
/// Implements IBacktestProgressPusher using SignalR's IHubContext&lt;BacktestHub&gt;.
/// Lives in the API project so Infrastructure does not need to reference Microsoft.AspNetCore.SignalR.
/// Registered in the API DI as IBacktestProgressPusher (singleton).
/// </summary>
public class SignalRBacktestProgressPusher(IHubContext<BacktestHub> hubContext) : IBacktestProgressPusher
{
    public Task PushProgressAsync(string jobId, BacktestJobStatusDto status)
        => hubContext.Clients.Group($"backtest:{jobId}")
            .SendAsync("BacktestProgress", status);

    public Task PushCompletedAsync(string jobId, BacktestJobStatusDto status)
        => hubContext.Clients.Group($"backtest:{jobId}")
            .SendAsync("BacktestCompleted", status);

    public Task PushChartUpdateAsync(string jobId, IReadOnlyList<BacktestChartBarDto> bars)
        => hubContext.Clients.Group($"backtest:{jobId}")
            .SendAsync("BacktestChartUpdate", new { jobId, bars });
}
