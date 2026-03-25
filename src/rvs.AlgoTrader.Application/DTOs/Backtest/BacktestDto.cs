using NodaTime;

namespace rvs.AlgoTrader.Application.DTOs.Backtest;

/// <summary>
/// Request DTO — matches the frontend BacktestRequest interface exactly.
/// Replaces the old StrategyInstanceId + CostProfileId schema.
/// </summary>
public record BacktestRequestDto(
    string StrategyName,
    string ParametersJson,
    string InternalSymbol,
    string Timeframe,
    LocalDate FromDate,
    LocalDate ToDate,
    decimal InitialCapital,
    decimal RiskPerTradePercent = 1.0m,
    // 0=NextBarOpen (default), 1=NextBarOpenPlusSlippage, 2=SignalBarClose
    int FillModel = 0,
    decimal SlippageBasisPoints = 5m,
    decimal BrokerageFlatPerSide = 20m,
    WalkForwardConfigDto? WalkForward = null);

public record WalkForwardConfigDto(int InSampleDays, int OutOfSampleDays, int StepDays);

/// <summary>
/// Full backtest result DTO — all metrics surfaced to the frontend.
/// Maps 1:1 from BacktestResult produced by BacktestEngine.
/// </summary>
public record BacktestResultDto(
    string? Id,
    bool Success,
    string StrategyName,
    string Symbol,
    string Timeframe,
    LocalDate FromDate,
    LocalDate ToDate,
    decimal InitialCapital,
    decimal FinalEquity,
    decimal TotalPnl,
    decimal TotalReturn,
    decimal MaxDrawdown,
    decimal SharpeRatio,
    decimal CalmarRatio,
    decimal ProfitFactor,
    decimal WinRate,
    int TotalTrades,
    int WinCount,
    int LossCount,
    decimal AvgWin,
    decimal AvgLoss,
    int MaxConsecutiveLosses,
    decimal ExpectancyPerTrade,
    string? DataHash,
    string? Error,
    DateTimeOffset? StartedAt,
    IReadOnlyList<BacktestTradeDto>? Trades);

/// <summary>
/// Individual trade for the per-trade breakdown table in the frontend.
/// </summary>
public record BacktestTradeDto(
    string Direction,
    decimal EntryPrice,
    decimal ExitPrice,
    int Quantity,
    string ExitReason,
    decimal GrossPnl,
    decimal NetPnl,
    string EntryTime,   // ISO UTC string
    string ExitTime);   // ISO UTC string

public record BacktestCostProfileDto(
    Guid Id, string Name, decimal BrokeragePct, decimal SttPct, decimal GstPct,
    decimal SebiChargesPct, decimal StampDutyPct, decimal SlippagePct,
    string Description, DateTimeOffset CreatedAt);

public record CreateBacktestCostProfileDto(
    string Name, decimal BrokeragePct, decimal SttPct, decimal GstPct,
    decimal SebiChargesPct, decimal StampDutyPct, decimal SlippagePct, string Description);
