using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Backtesting.Engine;

public interface IBacktestEngine
{
    Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken ct);
}

/// <summary>
/// Walk-forward backtesting engine.
/// Processes historical candle data sequentially — no lookahead bias.
/// Each strategy call receives only candles up to and including the current bar.
/// Computes all performance metrics: Sharpe, Calmar, MaxDrawdown, WinRate, PF.
/// SHA-256 reproducibility hash covers input data + parameters.
/// </summary>
public class BacktestEngine(
    ICandleRepository candleRepo,
    IStrategyFactory strategyFactory,
    ITransactionCostCalculator costCalc,
    ILogger<BacktestEngine> logger) : IBacktestEngine
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    // Default cost profile for Indian equity intraday
    private static readonly CostProfile DefaultCostProfile = new(
        BrokeragePct: 0.0003m,
        SttPct: 0.00025m,
        GstPct: 0.18m,
        SebiChargesPct: 0.000001m,
        StampDutyPct: 0.00003m,
        SlippagePct: 0.0002m);


    public async Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken ct)
    {
        logger.LogInformation("[Backtest] Starting {Strategy} on {Symbol}/{Tf} from {From} to {To}",
            request.StrategyName, request.InternalSymbol, request.Timeframe,
            request.FromDate, request.ToDate);

        // Load all candles for the period
        var fromInstant = request.FromDate.AtStartOfDayInZone(Ist).ToInstant();
        var toInstant = request.ToDate.PlusDays(1).AtStartOfDayInZone(Ist).ToInstant();
        var allCandles = await candleRepo.GetAsync(
            request.InternalSymbol, request.Timeframe, fromInstant, toInstant, ct);

        if (allCandles.Count < 50)
            return BacktestResult.Failed("Insufficient candle data (< 50 bars)");

        // Compute reproducibility hash
        var dataHash = ComputeReproducibilityHash(allCandles, request);

        var strategy = strategyFactory.Create(request.StrategyName, request.ParametersJson);
        var trades = new List<BacktestTrade>();
        var equity = request.InitialCapital;
        var peakEquity = equity;
        var maxDrawdown = 0m;
        BacktestTrade? openTrade = null;

        // Warm-up period before first evaluation
        var warmupBars = 50;

        for (int i = warmupBars; i < allCandles.Count; i++)
        {
            // Provide only candles up to and including current bar (no lookahead)
            var visibleCandles = allCandles.Take(i + 1).ToList();
            var current = visibleCandles[^1];

            // Check if open trade should be closed
            if (openTrade != null)
            {
                var closed = TryClosePosition(openTrade, current, request);
                if (closed != null)
                {
                    var costs = costCalc.Calculate(
                        closed.ExitPrice * closed.Quantity, closed.Direction == "BUY", DefaultCostProfile);
                    closed = closed with { NetPnl = closed.GrossPnl - costs.Total };
                    equity += closed.NetPnl;
                    trades.Add(closed);
                    openTrade = null;

                    if (equity > peakEquity) peakEquity = equity;
                    var drawdown = (peakEquity - equity) / peakEquity;
                    if (drawdown > maxDrawdown) maxDrawdown = drawdown;
                }
                continue; // Only one position at a time
            }

            // Evaluate strategy
            var ctx = new StrategyContext(
                Guid.Empty,
                request.InternalSymbol,
                request.Timeframe,
                visibleCandles,
                request.ParametersJson,
                "backtest");

            SignalResult signal;
            try
            {
                signal = await strategy.EvaluateAsync(ctx, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Backtest] Strategy evaluation failed at bar {I}", i);
                continue;
            }

            if (signal.Signal is not ("BUY" or "SELL")) continue;

            // Position sizing: risk-based
            var positionSize = CalculatePositionSize(equity, signal, request);
            if (positionSize <= 0) continue;

            // Entry on next open (avoid lookahead — simulate fill at open of next bar)
            if (i + 1 >= allCandles.Count) break;
            var nextBar = allCandles[i + 1];
            var entryPrice = nextBar.Open;

            openTrade = new BacktestTrade(
                Id: Guid.NewGuid(),
                Symbol: request.InternalSymbol,
                Direction: signal.Signal,
                Quantity: positionSize,
                EntryPrice: entryPrice,
                ExitPrice: 0,
                StopLoss: signal.StopLoss ?? entryPrice * 0.99m,
                TakeProfit: signal.TakeProfit ?? entryPrice * 1.02m,
                EntryTime: nextBar.OpenTime,
                ExitTime: nextBar.OpenTime,
                GrossPnl: 0,
                NetPnl: 0,
                ExitReason: "");
        }

        // Force-close any open trade at end of data
        if (openTrade != null && allCandles.Count > 0)
        {
            var lastCandle = allCandles[^1];
            var exitPrice = lastCandle.Close;
            var grossPnl = openTrade.Direction == "BUY"
                ? (exitPrice - openTrade.EntryPrice) * openTrade.Quantity
                : (openTrade.EntryPrice - exitPrice) * openTrade.Quantity;
            var costs = costCalc.Calculate(exitPrice * openTrade.Quantity, false, DefaultCostProfile);
            trades.Add(openTrade with
            {
                ExitPrice = exitPrice,
                ExitTime = lastCandle.CloseTime,
                GrossPnl = grossPnl,
                NetPnl = grossPnl - costs.Total,
                ExitReason = "END_OF_DATA"
            });
        }

        return ComputeMetrics(trades, request, equity, maxDrawdown, dataHash);
    }

    private static BacktestTrade? TryClosePosition(BacktestTrade trade, ClosedCandle candle, BacktestRequest request)
    {
        string? exitReason = null;
        decimal exitPrice = 0;

        if (trade.Direction == "BUY")
        {
            if (candle.Low <= trade.StopLoss)
            { exitPrice = trade.StopLoss; exitReason = "STOP_LOSS"; }
            else if (candle.High >= trade.TakeProfit)
            { exitPrice = trade.TakeProfit; exitReason = "TAKE_PROFIT"; }
        }
        else
        {
            if (candle.High >= trade.StopLoss)
            { exitPrice = trade.StopLoss; exitReason = "STOP_LOSS"; }
            else if (candle.Low <= trade.TakeProfit)
            { exitPrice = trade.TakeProfit; exitReason = "TAKE_PROFIT"; }
        }

        if (exitReason == null) return null;

        var grossPnl = trade.Direction == "BUY"
            ? (exitPrice - trade.EntryPrice) * trade.Quantity
            : (trade.EntryPrice - exitPrice) * trade.Quantity;

        return trade with
        {
            ExitPrice = exitPrice,
            ExitTime = candle.CloseTime,
            GrossPnl = grossPnl,
            ExitReason = exitReason
        };
    }

    private static int CalculatePositionSize(decimal equity, SignalResult signal, BacktestRequest request)
    {
        if (signal.EntryPrice == null || signal.StopLoss == null) return 0;
        var riskAmount = equity * request.RiskPerTradePercent / 100m;
        var stopDistance = Math.Abs(signal.EntryPrice.Value - signal.StopLoss.Value);
        if (stopDistance == 0) return 0;
        return (int)(riskAmount / stopDistance);
    }

    private static BacktestResult ComputeMetrics(
        List<BacktestTrade> trades, BacktestRequest request,
        decimal finalEquity, decimal maxDrawdown, string dataHash)
    {
        if (trades.Count == 0)
            return BacktestResult.Failed("No trades generated");

        var winTrades = trades.Where(t => t.NetPnl > 0).ToList();
        var lossTrades = trades.Where(t => t.NetPnl <= 0).ToList();
        var totalPnl = trades.Sum(t => t.NetPnl);
        var grossProfit = winTrades.Sum(t => t.NetPnl);
        var grossLoss = Math.Abs(lossTrades.Sum(t => t.NetPnl));
        var profitFactor = grossLoss == 0 ? decimal.MaxValue : grossProfit / grossLoss;
        var winRate = (decimal)winTrades.Count / trades.Count;

        var returns = trades.Select(t => (double)(t.NetPnl / request.InitialCapital)).ToArray();
        var avgReturn = returns.Average();
        var stdDev = Math.Sqrt(returns.Select(r => Math.Pow(r - avgReturn, 2)).Average());
        var sharpe = stdDev == 0 ? 0 : (decimal)(avgReturn / stdDev * Math.Sqrt(252));

        var totalReturn = (finalEquity - request.InitialCapital) / request.InitialCapital;
        var calmar = maxDrawdown == 0 ? 0 : totalReturn / maxDrawdown;

        return new BacktestResult(
            Success: true,
            StrategyName: request.StrategyName,
            Symbol: request.InternalSymbol,
            Timeframe: request.Timeframe,
            FromDate: request.FromDate,
            ToDate: request.ToDate,
            InitialCapital: request.InitialCapital,
            FinalEquity: finalEquity,
            TotalPnl: totalPnl,
            TotalReturn: totalReturn,
            MaxDrawdown: maxDrawdown,
            SharpeRatio: sharpe,
            CalmarRatio: calmar,
            ProfitFactor: profitFactor,
            WinRate: winRate,
            TotalTrades: trades.Count,
            WinCount: winTrades.Count,
            LossCount: lossTrades.Count,
            AvgWin: winTrades.Count > 0 ? winTrades.Average(t => t.NetPnl) : 0,
            AvgLoss: lossTrades.Count > 0 ? Math.Abs(lossTrades.Average(t => t.NetPnl)) : 0,
            Trades: trades,
            DataHash: dataHash,
            Error: null);
    }

    private static string ComputeReproducibilityHash(IReadOnlyList<ClosedCandle> candles, BacktestRequest request)
    {
        var sb = new StringBuilder();
        sb.Append($"strategy={request.StrategyName};params={request.ParametersJson};");
        sb.Append($"symbol={request.InternalSymbol};tf={request.Timeframe};");
        sb.Append($"from={request.FromDate};to={request.ToDate};capital={request.InitialCapital};");
        foreach (var c in candles)
            sb.Append($"{c.OpenTime.ToInstant().ToUnixTimeMilliseconds()},{c.Open},{c.High},{c.Low},{c.Close},{c.Volume};");

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }
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
    int WalkForwardOutOfSampleBars = 50);

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
    IReadOnlyList<BacktestTrade> Trades,
    string? DataHash,
    string? Error)
{
    public static BacktestResult Failed(string error) => new(
        false, "", "", "", LocalDate.MinIsoValue, LocalDate.MinIsoValue,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], null, error);

    /// <summary>Compute BacktestResult metrics directly from a trade list (for unit tests).</summary>
    public static BacktestResult FromTrades(IReadOnlyList<BacktestTrade> trades, decimal initialCapital)
    {
        if (trades.Count == 0) return Failed("No trades");

        var wins = trades.Where(t => t.NetPnl > 0).ToList();
        var losses = trades.Where(t => t.NetPnl <= 0).ToList();
        var totalPnl = trades.Sum(t => t.NetPnl);
        var grossProfit = wins.Sum(t => t.NetPnl);
        var grossLoss = Math.Abs(losses.Sum(t => t.NetPnl));
        var profitFactor = grossLoss == 0 ? decimal.MaxValue : grossProfit / grossLoss;
        var winRate = (decimal)wins.Count / trades.Count;

        var returns = trades.Select(t => (double)(t.NetPnl / initialCapital)).ToArray();
        var avgReturn = returns.Average();
        var stdDev = Math.Sqrt(returns.Select(r => Math.Pow(r - avgReturn, 2)).Average());
        var sharpe = stdDev == 0 ? 0m : (decimal)(avgReturn / stdDev * Math.Sqrt(252));

        // Compute max drawdown from equity curve
        var equity = initialCapital;
        var peak = equity;
        var maxDd = 0m;
        foreach (var t in trades)
        {
            equity += t.NetPnl;
            if (equity > peak) peak = equity;
            var dd = peak > 0 ? (peak - equity) / peak : 0m;
            if (dd > maxDd) maxDd = dd;
        }

        return new BacktestResult(
            Success: true,
            StrategyName: "Test",
            Symbol: "TEST",
            Timeframe: "5m",
            FromDate: LocalDate.MinIsoValue,
            ToDate: LocalDate.MinIsoValue,
            InitialCapital: initialCapital,
            FinalEquity: initialCapital + totalPnl,
            TotalPnl: totalPnl,
            TotalReturn: totalPnl / initialCapital,
            MaxDrawdown: maxDd,
            SharpeRatio: sharpe,
            CalmarRatio: maxDd == 0 ? 0m : (totalPnl / initialCapital) / maxDd,
            ProfitFactor: profitFactor,
            WinRate: winRate,
            TotalTrades: trades.Count,
            WinCount: wins.Count,
            LossCount: losses.Count,
            AvgWin: wins.Count > 0 ? wins.Average(t => t.NetPnl) : 0m,
            AvgLoss: losses.Count > 0 ? Math.Abs(losses.Average(t => t.NetPnl)) : 0m,
            Trades: trades,
            DataHash: null,
            Error: null);
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
