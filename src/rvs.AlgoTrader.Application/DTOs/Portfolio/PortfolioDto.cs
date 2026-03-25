namespace rvs.AlgoTrader.Application.DTOs.Portfolio;

/// <summary>Per-strategy P&amp;L breakdown row for the portfolio dashboard.</summary>
public record StrategyPnlRowDto(
    string InstanceId,
    string Name,
    string StrategyType,
    string InternalSymbol,
    string Mode,
    string Status,
    decimal AllocatedCapital,
    decimal TodayRealizedPnl,
    decimal TodayUnrealizedPnl,
    decimal TodayTotalPnl,
    decimal PnlPercent);

/// <summary>
/// Aggregate portfolio summary across all strategy instances.
/// Returned by GET /api/portfolio/summary.
/// </summary>
public record PortfolioSummaryDto(
    decimal TodayTotalRealizedPnl,
    decimal TodayTotalUnrealizedPnl,
    decimal TodayTotalPnl,
    decimal TotalAllocatedCapital,
    int RunningCount,
    int PausedCount,
    int StoppedCount,
    int ForwardTestCount,
    IReadOnlyList<StrategyPnlRowDto> ByStrategy);
