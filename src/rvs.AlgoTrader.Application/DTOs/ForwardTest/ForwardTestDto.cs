namespace rvs.AlgoTrader.Application.DTOs.ForwardTest;

/// <summary>
/// Key metrics snapshot of a backtest run, embedded inside ForwardTestSessionDetailDto
/// when the session was promoted from a backtest. Allows side-by-side comparison in the UI.
/// </summary>
public record BacktestSnapshotDto(
    string BacktestId,
    decimal TotalPnl,
    decimal TotalReturn,
    decimal WinRate,
    decimal MaxDrawdown,
    decimal SharpeRatio,
    decimal ExpectancyPerTrade,
    int TotalTrades);

/// <summary>
/// Equity curve point for forward test chart — one point per completed trade.
/// </summary>
public record ForwardEquityPoint(
    string Time,   // UTC ISO string
    decimal Equity,
    decimal Pnl);

/// <summary>
/// Full detail DTO for a forward test session, enriched with strategy instance info.
/// Matches the ForwardTestSession frontend interface exactly.
/// </summary>
public record ForwardTestSessionDetailDto(
    string InstanceId,
    string InstanceName,
    string StrategyType,
    string InternalSymbol,
    string Timeframe,
    string? BrokerName,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    decimal InitialCapital,
    decimal CurrentEquity,
    decimal TotalPnl,
    decimal TotalReturn,
    decimal MaxDrawdown,
    decimal SharpeRatio,
    decimal WinRate,
    int TotalTrades,
    int OpenPositionCount,
    string? SourceBacktestId,
    BacktestSnapshotDto? SourceBacktest,           // null if not promoted from backtest
    IReadOnlyList<ForwardEquityPoint> EquityCurvePoints);

/// <summary>
/// Command to promote a backtest result into a new ForwardTest-mode strategy instance.
/// Carries strategy, symbol, timeframe, and parameters from the backtest.
/// </summary>
public record PromoteBacktestToForwardTestRequest(
    string BacktestId,
    string InstanceName,
    string BrokerName,
    decimal InitialCapital,
    string? ScheduleJson);

/// <summary>
/// Command to promote a completed/stopped forward test into a Live strategy instance.
/// Runs pre-flight checks before creating the live instance.
/// </summary>
public record PromoteForwardTestToLiveRequest(
    string ForwardTestInstanceId,
    string BrokerName,
    decimal AllocatedCapital,
    string? ScheduleJson);

/// <summary>Result of a single pre-flight check before going live.</summary>
public record PreFlightCheckDto(string Name, bool Passed, string? Reason);

/// <summary>Result of the promote-to-live operation, including all pre-flight check details.</summary>
public record PromoteToLiveResultDto(
    bool Success,
    string? NewStrategyInstanceId,
    IReadOnlyList<PreFlightCheckDto> Checks,
    string? Error);
