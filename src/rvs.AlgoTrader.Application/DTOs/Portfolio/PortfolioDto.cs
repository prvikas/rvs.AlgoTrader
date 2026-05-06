using rvs.AlgoTrader.Domain.Enums;

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

/// <summary>Open position row for GET /api/portfolio/positions.</summary>
public record PositionDto(
    string Id,
    string BrokerName,
    string InternalSymbol,
    long Quantity,
    decimal AvgPrice,
    decimal CurrentPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    decimal UnrealizedPnl,
    decimal RealizedPnl,
    string ProductType,
    string? StrategyRunId,
    DateTimeOffset? OpenedAt);

/// <summary>Order row for GET /api/portfolio/orders.</summary>
public record OrderDto(
    string Id,
    string BrokerName,
    string? BrokerOrderId,
    string InternalSymbol,
    OrderType OrderType,
    OrderDirection Direction,
    OrderStatus Status,
    long Quantity,
    long FilledQuantity,
    decimal? Price,
    decimal? FillPrice,
    decimal? TriggerPrice,
    string Exchange,
    string ProductType,
    string? StrategyRunId,
    string? RejectionReason,
    DateTimeOffset? PlacedAt,
    DateTimeOffset? FilledAt,
    DateTimeOffset CreatedAt);

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
