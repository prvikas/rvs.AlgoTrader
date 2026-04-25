using NodaTime;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Application.Services;

// ─────────────────────────────────────────────────────────────────────────────
// IBacktestEngine — Application-layer interface for the backtesting engine.
//
// Defined here (not in rvs.AlgoTrader.Backtesting) so that Infrastructure can
// depend on it without violating the layer dependency rule:
//   Infrastructure → Application  (allowed)
//   Infrastructure → Backtesting  (NOT allowed)
//
// BacktestEngine in rvs.AlgoTrader.Backtesting implements this interface.
// BacktestService in rvs.AlgoTrader.Infrastructure consumes it.
// ─────────────────────────────────────────────────────────────────────────────

public interface IBacktestEngine
{
    Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken ct,
        IProgress<BacktestProgress>? progress = null);
}

/// <summary>
/// A single OHLCV bar with indicator snapshot + optional signal, streamed to the frontend
/// during the backtest run so the chart can update in rolling-window mode, and included
/// in the downsampled ChartSample after completion for the full replay chart.
/// </summary>
public record BacktestChartBar(
    long TimeMs,            // Unix epoch milliseconds (IST timestamp)
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    string? Signal,         // "BUY", "SELL", or null (no signal this bar)
    decimal? SignalPrice,   // Entry price when signal != null
    decimal? StopLoss,
    decimal? TakeProfit,
    IReadOnlyDictionary<string, decimal>? Indicators); // e.g. { "ema5": 452.3, "vwap": 453.1 }

public record BacktestProgress(
    string JobId,
    int CurrentBar,
    int TotalBars,
    decimal ProgressPct,   // 0–100
    int TradesSoFar,
    decimal CurrentEquity,
    // Rolling 200-bar window sent with each 1% progress update.
    // Frontend renders this as a live-updating candlestick chart.
    IReadOnlyList<BacktestChartBar>? ChartBatch = null);

public record BacktestMonthlyBreakdown(
    int Year,
    int Month,
    decimal Pnl,
    int Trades,
    decimal WinRate);

public record BacktestYearlyBreakdown(
    int Year,
    decimal Pnl,
    decimal Return,
    int Trades,
    decimal WinRate);

public record BacktestRequest(
    string StrategyName,
    string ParametersJson,
    string InternalSymbol,
    string Timeframe,
    LocalDate FromDate,
    LocalDate ToDate,
    decimal InitialCapital,
    decimal RiskPerTradePercent = 1.0m,
    string ProductType = "MIS",
    FillModel FillModel = FillModel.NextBarOpen,
    decimal SlippageBasisPoints = 5m,
    decimal BrokerageFlatPerSide = 20m,
    decimal? BrokeragePct = null,
    decimal? SttPct = null,
    decimal? GstPct = null,
    decimal? SebiChargesPct = null,
    decimal? StampDutyPct = null,
    string? JobId = null,

    // ── Position sizing cap ───────────────────────────────────────────────────
    // Maximum fraction of equity to deploy per trade (0 = use default 0.95).
    // Pine's default is effectively 1.0 (full equity); set to 0.95 to leave headroom for commissions.
    // The old hardcoded 0.25 cap was 4× too conservative and the primary cause of under-sized returns.
    decimal MaxCapitalUsagePct = 0.95m,
    // ── Trailing stop parameters ──────────────────────────────────────────
    decimal TrailActivationR = 0m,
    decimal TrailOffsetR = 0.5m,
    bool BreakEvenAt1R = false,
    // ── Circuit breaker ───────────────────────────────────────────────────
    // Stop the backtest early if equity falls below this fraction of InitialCapital.
    // Default 0.5 = 50%.  Set to 0 to disable.
    decimal CircuitBreakerPct = 0,
    // ── Warmup period ─────────────────────────────────────────────────────
    // Bars skipped at the start so indicators have enough history before trading.
    // Default 50. Strategy should set this to its longest lookback period.
    int WarmupBars = 50);

public record BacktestResult(
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
    // New stats
    decimal SortinoRatio,
    decimal DailySharpe,
    decimal MonthlySharpe,
    decimal MonthlyWinRate,
    int DrawdownRecoveryBars,
    int MaxLots,
    IReadOnlyList<BacktestMonthlyBreakdown> MonthlyBreakdown,
    IReadOnlyList<BacktestYearlyBreakdown> YearlyBreakdown,
    IReadOnlyList<BacktestTrade> Trades,
    string? DataHash,
    string? Error,
    // Downsampled to ≤ 2000 bars for the post-run full replay chart.
    // Contains all signal bars plus a uniform sample of the remaining bars.
    IReadOnlyList<BacktestChartBar>? ChartSample = null,
    // Circuit breaker: set when backtest is stopped early due to capital loss
    bool CircuitBreakerHit = false,
    string? CircuitBreakerReason = null,
    // ── Advanced risk analytics (#89) ─────────────────────────────────────────
    decimal VaR95 = 0m,
    decimal CVaR95 = 0m,
    decimal OmegaRatio = 0m,
    decimal Skewness = 0m,
    decimal Kurtosis = 0m,
    string DeploymentRating = "",
    string? DeploymentRationale = null,
    // ── Signal diagnostics (#46) ────────────────────────────────────────────────
    // Count of BUY/SELL signals that were silently dropped because CalculatePositionSize returned 0.
    // Causes: null EntryPrice/StopLoss, zero stop distance, or insufficient equity.
    int SkippedSignalCount = 0)
{
    public static BacktestResult Failed(string error) => new(
        false, "", "", "", LocalDate.MinIsoValue, LocalDate.MinIsoValue,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, [], [], [], null, error, null);

    /// <summary>Compute BacktestResult metrics directly from a trade list (for unit tests).</summary>
    public static BacktestResult FromTrades(IReadOnlyList<BacktestTrade> trades, decimal initialCapital)
    {
        if (trades.Count == 0) return Failed("No trades");

        var wins   = trades.Where(t => t.NetPnl > 0).ToList();
        var losses = trades.Where(t => t.NetPnl <= 0).ToList();
        var totalPnl     = trades.Sum(t => t.NetPnl);
        var grossProfit  = wins.Sum(t => t.NetPnl);
        var grossLoss    = Math.Abs(losses.Sum(t => t.NetPnl));
        var profitFactor = grossLoss == 0 ? decimal.MaxValue : grossProfit / grossLoss;
        var winRate      = (decimal)wins.Count / trades.Count;
        var avgWin       = wins.Count   > 0 ? wins.Average(t => t.NetPnl)                : 0m;
        var avgLoss      = losses.Count > 0 ? Math.Abs(losses.Average(t => t.NetPnl))    : 0m;
        var lossRate     = 1m - winRate;
        var expectancy   = winRate * avgWin - lossRate * avgLoss;

        var maxConsecLosses = 0;
        var curConsec = 0;
        foreach (var t in trades) { if (t.NetPnl <= 0) { curConsec++; if (curConsec > maxConsecLosses) maxConsecLosses = curConsec; } else curConsec = 0; }

        var returns   = trades.Select(t => (double)(t.NetPnl / initialCapital)).ToArray();
        var avgReturn = returns.Average();
        var stdDev    = Math.Sqrt(returns.Select(r => Math.Pow(r - avgReturn, 2)).Average());
        var sharpe    = stdDev == 0 ? 0m : (decimal)(avgReturn / stdDev * Math.Sqrt(252));

        var equity = initialCapital;
        var peak   = equity;
        var maxDd  = 0m;
        foreach (var t in trades)
        {
            equity += t.NetPnl;
            if (equity > peak) peak = equity;
            var dd = peak > 0 ? (peak - equity) / peak : 0m;
            if (dd > maxDd) maxDd = dd;
        }

        return new BacktestResult(
            Success: true, StrategyName: "Test", Symbol: "TEST", Timeframe: "5m",
            FromDate: LocalDate.MinIsoValue, ToDate: LocalDate.MinIsoValue,
            InitialCapital: initialCapital, FinalEquity: initialCapital + totalPnl,
            TotalPnl: totalPnl, TotalReturn: totalPnl / initialCapital,
            MaxDrawdown: maxDd, SharpeRatio: sharpe,
            CalmarRatio: maxDd == 0 ? 0m : (totalPnl / initialCapital) / maxDd,
            ProfitFactor: profitFactor, WinRate: winRate,
            TotalTrades: trades.Count, WinCount: wins.Count, LossCount: losses.Count,
            AvgWin: avgWin, AvgLoss: avgLoss,
            MaxConsecutiveLosses: maxConsecLosses, ExpectancyPerTrade: expectancy,
            SortinoRatio: 0m, DailySharpe: 0m, MonthlySharpe: 0m, MonthlyWinRate: 0m,
            DrawdownRecoveryBars: 0, MaxLots: 0,
            MonthlyBreakdown: [], YearlyBreakdown: [],
            Trades: trades, DataHash: null, Error: null, ChartSample: null);
    }
}

public record BacktestTrade(
    Guid Id,
    string Symbol,
    string Direction,
    int Quantity,
    decimal EntryPrice,
    decimal ExitPrice,
    decimal StopLoss,
    decimal TakeProfit,
    ZonedDateTime EntryTime,
    ZonedDateTime ExitTime,
    decimal GrossPnl,
    decimal NetPnl,
    string ExitReason,
    // ── Trailing stop state (managed by BacktestEngine, not strategy) ─────
    decimal InitialStopLoss  = 0m,   // original SL at entry — never changes, used to compute R
    decimal BestPrice        = 0m,   // running high (longs) or running low (shorts) since entry — used for MFE
    decimal WorstPrice       = 0m,   // running low (longs) or running high (shorts) since entry — used for MAE
    bool TrailActive         = false, // true once trailing has been activated
    decimal EntryCommission  = 0m,   // commission deducted from equity at trade open
    decimal ExitCommission   = 0m,   // commission deducted from equity at trade close
    // ── Professional analysis fields ────────────────────────────────────────
    int     HoldingBars      = 0,    // candle bars held from entry fill to exit
    decimal SlippageAmount   = 0m,   // slippage cost in ₹ (qty × rawPrice × basisPoints/10000)
    string? LegsJson         = null, // spread trades: JSON array of per-leg details
    int     EntryBarIndex    = -1);  // internal: allCandles[] index at entry fill (used to compute HoldingBars)
