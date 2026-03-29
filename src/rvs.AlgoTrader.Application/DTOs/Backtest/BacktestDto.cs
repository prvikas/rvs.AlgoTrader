using NodaTime;
using rvs.AlgoTrader.Domain.Enums;

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
    decimal InitialCapital = 100_000m,
    decimal RiskPerTradePercent = 1.0m,
    // 0=NextBarOpen (default), 1=NextBarOpenPlusSlippage, 2=SignalBarClose
    int FillModel = 0,
    decimal SlippageBasisPoints = 5m,
    decimal BrokerageFlatPerSide = 20m,
    // Broker to use for auto-downloading missing history. Defaults to MStock.
    string BrokerName = "MStock",
    WalkForwardConfigDto? WalkForward = null,
    // Trailing stop parameters (0 = disabled)
    decimal TrailActivationR = 0m,
    decimal TrailOffsetR = 0.5m,
    bool BreakEvenAt1R = false,
    // Circuit breaker: stop early when equity < InitialCapital × CircuitBreakerPct.
    // Default 0.5 = stop at 50% drawdown. Set to 0 to disable.
    decimal CircuitBreakerPct = 0.5m);

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
    // Extended stats
    decimal SortinoRatio,
    decimal DailySharpe,
    decimal MonthlySharpe,
    decimal MonthlyWinRate,
    int DrawdownRecoveryBars,
    int MaxLots,
    string? DataHash,
    string? Error,
    DateTimeOffset? StartedAt,
    IReadOnlyList<BacktestTradeDto>? Trades,
    IReadOnlyList<BacktestMonthlyBreakdownDto>? MonthlyBreakdown,
    IReadOnlyList<BacktestYearlyBreakdownDto>? YearlyBreakdown,
    // Downsampled (≤ 2000 bars) candlestick + indicator data for the full replay chart.
    IReadOnlyList<BacktestChartBarDto>? ChartSample = null,
    bool CircuitBreakerHit = false,
    string? CircuitBreakerReason = null,
    // Scenario that triggered this run, if any.
    Guid? ScenarioId = null);

public record BacktestMonthlyBreakdownDto(int Year, int Month, decimal Pnl, int Trades, decimal WinRate);
public record BacktestYearlyBreakdownDto(int Year, decimal Pnl, decimal Return, int Trades, decimal WinRate);

/// <summary>
/// A single OHLCV bar with optional indicator values and signal marker.
/// Sent to the frontend both as rolling batches during the run (BacktestChartUpdate SignalR event)
/// and as a downsampled full ChartSample when the backtest completes.
/// </summary>
public record BacktestChartBarDto(
    long TimeMs,            // Unix epoch milliseconds
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    string? Signal,         // "BUY", "SELL", or null
    decimal? SignalPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    IReadOnlyDictionary<string, decimal>? Indicators);

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

/// <summary>Status of a running/completed async backtest job.</summary>
public record BacktestJobStatusDto(
    string JobId,
    BacktestJobStatus Status,
    decimal ProgressPct,    // 0–100
    int CurrentBar,
    int TotalBars,
    int TradesSoFar,
    decimal CurrentEquity,
    string? Error,
    BacktestResultDto? Result);

public record BacktestCostProfileDto(
    Guid Id, string Name, decimal BrokeragePct, decimal SttPct, decimal GstPct,
    decimal SebiChargesPct, decimal StampDutyPct, decimal SlippagePct,
    string Description, DateTimeOffset CreatedAt);

public record CreateBacktestCostProfileDto(
    string Name, decimal BrokeragePct, decimal SttPct, decimal GstPct,
    decimal SebiChargesPct, decimal StampDutyPct, decimal SlippagePct, string Description);
