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
    decimal CircuitBreakerPct = 0.5m,
    // Optional: tag this standalone run to a scenario for grouping in Previous Runs.
    Guid? ScenarioId = null);

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
    Guid? ScenarioId = null,
    // ── Advanced risk analytics (#89) ─────────────────────────────────────────
    decimal VaR95 = 0m,
    decimal CVaR95 = 0m,
    decimal OmegaRatio = 0m,
    decimal Skewness = 0m,
    decimal Kurtosis = 0m,
    // Deployment readiness: "Green" | "Amber" | "Red"
    string DeploymentRating = "",
    string? DeploymentRationale = null,
    // ── Signal diagnostics (#46) ────────────────────────────────────────────────
    int SkippedSignalCount = 0);

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
/// Contains all fields needed for professional trade analysis.
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
    string ExitTime,    // ISO UTC string
    // ── Excursion analytics ─────────────────────────────────────────────────
    decimal Mae = 0m,   // Maximum Adverse Excursion (price units from entry)
    decimal Mfe = 0m,   // Maximum Favorable Excursion (price units from entry)
    // ── Cost breakdown ──────────────────────────────────────────────────────
    decimal EntryCommission = 0m,
    decimal ExitCommission  = 0m,
    decimal TotalCost       = 0m,  // EntryCommission + ExitCommission
    decimal SlippageAmount  = 0m,  // ₹ cost of slippage at entry fill
    // ── Risk levels ─────────────────────────────────────────────────────────
    decimal StopLoss   = 0m,       // initial stop-loss price
    decimal TakeProfit = 0m,       // take-profit / premium-capture target
    // ── Duration & R ────────────────────────────────────────────────────────
    int     HoldingBars = 0,       // candle bars from entry fill to exit
    decimal RMultiple   = 0m,      // NetPnl ÷ initial risk ₹ (R-multiple)
    // ── Spread legs ─────────────────────────────────────────────────────────
    // For spread trades only: JSON [{strike, type, direction, premium, expiry, brokerage}]
    string? LegsJson = null);

/// <summary>Single option leg inside a spread trade (deserialised from LegsJson).</summary>
public record BacktestTradeLegDto(
    decimal Strike,
    string  Type,       // "CE" | "PE"
    string  Direction,  // "BUY" | "SELL"
    decimal Premium,    // entry premium per contract
    string  Expiry,     // "YYYY-MM-DD"
    decimal Brokerage); // flat brokerage for this leg

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
    BacktestResultDto? Result,
    DateTimeOffset StartedAt = default);

public record BacktestCostProfileDto(
    Guid Id, string Name, decimal BrokeragePct, decimal SttPct, decimal GstPct,
    decimal SebiChargesPct, decimal StampDutyPct, decimal SlippagePct,
    string Description, DateTimeOffset CreatedAt);

public record CreateBacktestCostProfileDto(
    string Name, decimal BrokeragePct, decimal SttPct, decimal GstPct,
    decimal SebiChargesPct, decimal StampDutyPct, decimal SlippagePct, string Description);

// ── Monte Carlo DTOs ───────────────────────────────────────────────────────────

public record MonteCarloRequestDto(int Simulations = 1000, int? Seed = null);

public record MonteCarloSimulationDto(
    decimal DrawdownP5,
    decimal DrawdownP50,
    decimal DrawdownP95,
    decimal EquityP5,
    decimal EquityP50,
    decimal EquityP95,
    decimal ProbabilityOfRuin);
