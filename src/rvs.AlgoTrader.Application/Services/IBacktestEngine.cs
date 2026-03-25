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
    Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken ct);
}

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
    bool WalkForward = false,
    int WalkForwardInSampleBars = 200,
    int WalkForwardOutOfSampleBars = 50,
    // 0=NextBarOpen (default, no lookahead), 1=NextBarOpenPlusSlippage, 2=SignalBarClose
    FillModel FillModel = FillModel.NextBarOpen,
    // Additional adverse slippage in basis points. E.g. 5 = 0.05%. Default = 5 bps.
    decimal SlippageBasisPoints = 5m,
    // Flat brokerage per order leg in INR (e.g. 20 for Zerodha/Upstox model).
    decimal BrokerageFlatPerSide = 20m);

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
    IReadOnlyList<BacktestTrade> Trades,
    string? DataHash,
    string? Error)
{
    public static BacktestResult Failed(string error) => new(
        false, "", "", "", LocalDate.MinIsoValue, LocalDate.MinIsoValue,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], null, error);

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
            Trades: trades, DataHash: null, Error: null);
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
    string ExitReason);
