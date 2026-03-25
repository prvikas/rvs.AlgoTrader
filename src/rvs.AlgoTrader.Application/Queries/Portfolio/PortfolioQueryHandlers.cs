using MediatR;
using rvs.AlgoTrader.Application.DTOs.Portfolio;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Queries.Portfolio;

public class GetPortfolioSummaryHandler(IStrategyInstanceRepository repo)
    : IRequestHandler<GetPortfolioSummaryQuery, PortfolioSummaryDto>
{
    public async Task<PortfolioSummaryDto> Handle(GetPortfolioSummaryQuery request, CancellationToken ct)
    {
        var all = await repo.GetAllAsync(ct);

        var rows = all.Select(i =>
        {
            var total = i.TodayRealizedPnl + i.TodayUnrealizedPnl;
            var pct = i.AllocatedCapital > 0 ? total / i.AllocatedCapital * 100m : 0m;
            return new StrategyPnlRowDto(
                InstanceId: i.Id.ToString(),
                Name: i.Name,
                StrategyType: i.StrategyType,
                InternalSymbol: i.InternalSymbol,
                Mode: i.Mode.ToString(),
                Status: i.Status.ToString(),
                AllocatedCapital: i.AllocatedCapital,
                TodayRealizedPnl: i.TodayRealizedPnl,
                TodayUnrealizedPnl: i.TodayUnrealizedPnl,
                TodayTotalPnl: total,
                PnlPercent: pct);
        }).ToList();

        return new PortfolioSummaryDto(
            TodayTotalRealizedPnl: rows.Sum(r => r.TodayRealizedPnl),
            TodayTotalUnrealizedPnl: rows.Sum(r => r.TodayUnrealizedPnl),
            TodayTotalPnl: rows.Sum(r => r.TodayTotalPnl),
            TotalAllocatedCapital: rows.Sum(r => r.AllocatedCapital),
            RunningCount: all.Count(i => string.Equals(i.Status.ToString(), "Running", StringComparison.OrdinalIgnoreCase) && string.Equals(i.Mode.ToString(), "Live", StringComparison.OrdinalIgnoreCase)),
            PausedCount: all.Count(i => string.Equals(i.Status.ToString(), "Paused", StringComparison.OrdinalIgnoreCase)),
            StoppedCount: all.Count(i => string.Equals(i.Status.ToString(), "Stopped", StringComparison.OrdinalIgnoreCase)),
            ForwardTestCount: all.Count(i => string.Equals(i.Mode.ToString(), "Forward", StringComparison.OrdinalIgnoreCase) && string.Equals(i.Status.ToString(), "Running", StringComparison.OrdinalIgnoreCase)),
            ByStrategy: rows);
    }
}
