using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Backtesting.Engine;

/// <summary>
/// Walk-forward backtesting engine — no lookahead bias.
/// Uses ReadOnlyListSlice for O(1) visible-candle windowing (fixes O(n²) hang).
/// Reports progress via IProgress&lt;BacktestProgress&gt; every 1% of bars.
/// Computes extended stats: Sortino, Monthly/Daily Sharpe, yearly breakdown, drawdown recovery.
/// </summary>
public class BacktestEngine(
    ICandleRepository candleRepo,
    IStrategyFactory strategyFactory,
    ITransactionCostCalculator costCalc,
    ILogger<BacktestEngine> logger) : IBacktestEngine
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    private static readonly CostProfile DefaultCostProfile = new(
        BrokeragePct: 0m,
        SttPct: 0.00025m,
        GstPct: 0.18m,
        SebiChargesPct: 0.000001m,
        StampDutyPct: 0.00003m,
        SlippagePct: 0.0002m,
        BrokerageFlatPerSide: 20m,
        SlippageBasisPoints: 0m);

    public async Task<BacktestResult> RunAsync(
        BacktestRequest request,
        CancellationToken ct,
        IProgress<BacktestProgress>? progress = null)
    {
        logger.LogInformation(
            "[Backtest] Starting {Strategy} on {Symbol}/{Tf} from {From} to {To} | FillModel={Fill} Slippage={Slip}bps Brokerage=\u20b9{Brok}/side",
            request.StrategyName, request.InternalSymbol, request.Timeframe,
            request.FromDate, request.ToDate, request.FillModel,
            request.SlippageBasisPoints, request.BrokerageFlatPerSide);

        var costProfile = DefaultCostProfile with
        {
            BrokerageFlatPerSide = request.BrokerageFlatPerSide,
            BrokeragePct = request.BrokerageFlatPerSide > 0 ? 0m : DefaultCostProfile.BrokeragePct,
            SlippageBasisPoints = request.SlippageBasisPoints,
        };

        var fromInstant = request.FromDate.AtStartOfDayInZone(Ist).ToInstant();
        var toInstant   = request.ToDate.PlusDays(1).AtStartOfDayInZone(Ist).ToInstant();
        var allCandles  = await candleRepo.GetOrAggregateAsync(
            request.InternalSymbol, request.Timeframe, fromInstant, toInstant, ct);

        logger.LogInformation("[Backtest] Loaded {Count} candles for {Symbol}/{Tf}",
            allCandles.Count, request.InternalSymbol, request.Timeframe);

        var warmupBars = request.WarmupBars;
        if (allCandles.Count < warmupBars + 1)
            return BacktestResult.Failed($"Insufficient candle data (need > {warmupBars} bars, got {allCandles.Count})");

        var dataHash = ComputeReproducibilityHash(allCandles, request);
        var strategy = strategyFactory.Create(request.StrategyName, request.ParametersJson);

        var trades    = new List<BacktestTrade>();
        var equity    = request.InitialCapital;
        var peakEquity = equity;
        var maxDrawdown = 0m;
        BacktestTrade? openTrade = null;
        var totalBars  = allCandles.Count;
        var circuitBreakerHit = false;
        string? circuitBreakerReason = null;
        var circuitBreakerFloor = request.CircuitBreakerPct > 0
            ? request.InitialCapital * request.CircuitBreakerPct
            : 0m;

        // Progress reporting: every 1% of bars (minimum 1)
        var jobId        = request.JobId ?? "backtest";
        var progressStep = Math.Max(1, totalBars / 100);

        // ── Chart accumulation ──────────────────────────────────────────────
        // Full buffer: downsampled to ≤ 2000 at completion.
        // Rolling window: last 200 bars sent with each progress event.
        const int RollingWindowSize = 200;
        var chartBuffer = new List<BacktestChartBar>(Math.Min(totalBars, 50_000));
        var rollingWindow = new BacktestChartBar[RollingWindowSize];
        int rollingHead  = 0;   // next write position (circular)
        int rollingFill  = 0;   // how many slots are populated (0..200)

        void AddChartBar(BacktestChartBar bar)
        {
            chartBuffer.Add(bar);
            rollingWindow[rollingHead % RollingWindowSize] = bar;
            rollingHead++;
            if (rollingFill < RollingWindowSize) rollingFill++;
        }

        IReadOnlyList<BacktestChartBar> SnapshotRollingWindow()
        {
            // Return bars in chronological order from the circular buffer
            var result = new BacktestChartBar[rollingFill];
            int startIdx = rollingFill < RollingWindowSize ? 0 : rollingHead % RollingWindowSize;
            for (int k = 0; k < rollingFill; k++)
                result[k] = rollingWindow[(startIdx + k) % RollingWindowSize];
            return result;
        }
        // ───────────────────────────────────────────────────────────────────

        for (int i = warmupBars; i < totalBars; i++)
        {
            // Cancellation checkpoint every 500 bars
            if (i % 500 == 0) ct.ThrowIfCancellationRequested();

            var current = allCandles[i];

            if (openTrade != null)
            {
                // Apply trailing stop / break-even BEFORE checking SL/TP so the
                // updated SL is used in this bar's exit check.
                openTrade = ApplyTrailingStop(openTrade, current, request);

                var closed = TryClosePosition(openTrade, current, request);
                if (closed != null)
                {
                    var entryCosts = costCalc.Calculate(closed.EntryPrice * closed.Quantity, closed.Direction == "BUY", costProfile);
                    var exitCosts  = costCalc.Calculate(closed.ExitPrice  * closed.Quantity, closed.Direction != "BUY", costProfile);
                    closed     = closed with { NetPnl = closed.GrossPnl - entryCosts.Total - exitCosts.Total };
                    equity    += closed.NetPnl;
                    trades.Add(closed);
                    openTrade  = null;

                    if (equity > peakEquity) peakEquity = equity;
                    var drawdown = peakEquity > 0 ? (peakEquity - equity) / peakEquity : 0m;
                    if (drawdown > maxDrawdown) maxDrawdown = drawdown;

                    // ── Circuit breaker: stop early if equity falls below the floor ──
                    if (circuitBreakerFloor > 0 && equity < circuitBreakerFloor)
                    {
                        circuitBreakerHit    = true;
                        circuitBreakerReason = $"Equity ₹{equity:F0} fell below {request.CircuitBreakerPct * 100:F0}% of initial capital ₹{request.InitialCapital:F0}. Loss-making strategy — backtest stopped early.";
                        logger.LogWarning("[Backtest] Circuit breaker triggered at bar {I}: {Reason}", i, circuitBreakerReason);
                        break;
                    }
                }

                // Chart: add bar without indicators (strategy not evaluated on open-trade bars)
                AddChartBar(new BacktestChartBar(
                    TimeMs: current.OpenTime.ToInstant().ToUnixTimeMilliseconds(),
                    Open: current.Open, High: current.High, Low: current.Low, Close: current.Close,
                    Volume: current.Volume,
                    Signal: null, SignalPrice: null, StopLoss: null, TakeProfit: null,
                    Indicators: null));

                // Progress reporting (for open-trade bars with no signal)
                if (progress != null && i % progressStep == 0)
                {
                    var pct = (decimal)(i - warmupBars) / Math.Max(1, totalBars - warmupBars) * 100m;
                    progress.Report(new BacktestProgress(jobId, i, totalBars, Math.Min(99m, pct),
                        trades.Count, equity, SnapshotRollingWindow()));
                }
                continue;
            }

            // *** KEY FIX: zero-copy O(1) slice — no List<T> allocation ***
            var visibleCandles = new ReadOnlyListSlice<ClosedCandle>(allCandles, i + 1);

            var ctx = new StrategyContext(
                Guid.Empty,
                request.InternalSymbol,
                request.Timeframe,
                visibleCandles,
                request.ParametersJson,
                "backtest");

            SignalResult signal;
            try { signal = await strategy.EvaluateAsync(ctx, ct); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Backtest] Strategy evaluation failed at bar {I}", i);
                AddChartBar(new BacktestChartBar(
                    current.OpenTime.ToInstant().ToUnixTimeMilliseconds(),
                    current.Open, current.High, current.Low, current.Close, current.Volume,
                    null, null, null, null, null));
                continue;
            }

            // Chart: bar with indicators and optional signal
            string? chartSig = signal.Signal is SignalType.Buy or SignalType.Sell ? signal.Signal.ToString().ToUpperInvariant() : null;
            AddChartBar(new BacktestChartBar(
                TimeMs: current.OpenTime.ToInstant().ToUnixTimeMilliseconds(),
                Open: current.Open, High: current.High, Low: current.Low, Close: current.Close,
                Volume: current.Volume,
                Signal: chartSig,
                SignalPrice: chartSig != null ? signal.EntryPrice : null,
                StopLoss:    chartSig != null ? signal.StopLoss   : null,
                TakeProfit:  chartSig != null ? signal.TakeProfit : null,
                Indicators: signal.IndicatorValues));

            // Progress reporting every ~1% (includes rolling window snapshot)
            if (progress != null && i % progressStep == 0)
            {
                var pct = (decimal)(i - warmupBars) / Math.Max(1, totalBars - warmupBars) * 100m;
                progress.Report(new BacktestProgress(
                    JobId: jobId,
                    CurrentBar: i,
                    TotalBars: totalBars,
                    ProgressPct: Math.Min(99m, pct),
                    TradesSoFar: trades.Count,
                    CurrentEquity: equity,
                    ChartBatch: SnapshotRollingWindow()));
            }

            if (signal.Signal is not (SignalType.Buy or SignalType.Sell)) continue;

            var positionSize = CalculatePositionSize(equity, signal, request);
            if (positionSize <= 0) continue;

            decimal entryPrice;
            ZonedDateTime entryTime;

            if (request.FillModel == FillModel.SignalBarClose)
            {
                entryPrice = current.Close;
                entryTime  = current.CloseTime;
            }
            else
            {
                if (i + 1 >= totalBars) break;
                var nextBar = allCandles[i + 1];
                entryPrice  = nextBar.Open;
                entryTime   = nextBar.OpenTime;

                if (request.FillModel == FillModel.NextBarOpenPlusSlippage && request.SlippageBasisPoints > 0)
                {
                    var slipFraction = request.SlippageBasisPoints / 10_000m;
                    entryPrice = signal.Signal == SignalType.Buy
                        ? entryPrice * (1m + slipFraction)
                        : entryPrice * (1m - slipFraction);
                }
            }

            var initialSl = signal.StopLoss ?? entryPrice * 0.99m;
            openTrade = new BacktestTrade(
                Id: Guid.NewGuid(),
                Symbol: request.InternalSymbol,
                Direction: signal.Signal.ToString().ToUpperInvariant(),
                Quantity: positionSize,
                EntryPrice: entryPrice,
                ExitPrice: 0,
                StopLoss:        initialSl,
                TakeProfit:      signal.TakeProfit ?? entryPrice * 1.02m,
                EntryTime: entryTime,
                ExitTime: entryTime,
                GrossPnl: 0,
                NetPnl: 0,
                ExitReason: "",
                InitialStopLoss: initialSl,
                BestPrice:       entryPrice,   // will track high (longs) or low (shorts) from entry
                WorstPrice:      entryPrice,   // will track low (longs) or high (shorts) from entry
                TrailActive:     false);
        }

        if (openTrade != null && allCandles.Count > 0)
        {
            var lastCandle    = allCandles[^1];
            var exitPrice     = lastCandle.Close;
            var grossPnl      = openTrade.Direction == "BUY"
                ? (exitPrice - openTrade.EntryPrice) * openTrade.Quantity
                : (openTrade.EntryPrice - exitPrice) * openTrade.Quantity;
            var entryCostsEod = costCalc.Calculate(openTrade.EntryPrice * openTrade.Quantity, openTrade.Direction == "BUY",  costProfile);
            var exitCostsEod  = costCalc.Calculate(exitPrice             * openTrade.Quantity, openTrade.Direction != "BUY", costProfile);
            var netPnlEod     = grossPnl - entryCostsEod.Total - exitCostsEod.Total;
            equity += netPnlEod;
            trades.Add(openTrade with
            {
                ExitPrice  = exitPrice,
                ExitTime   = lastCandle.CloseTime,
                GrossPnl   = grossPnl,
                NetPnl     = netPnlEod,
                ExitReason = "END_OF_DATA"
            });
        }

        // Final progress (100%)
        progress?.Report(new BacktestProgress(jobId, totalBars, totalBars, 100m, trades.Count, equity));

        logger.LogInformation("[Backtest] Finished \u2014 {Trades} trades, equity \u20b9{Equity:F0}",
            trades.Count, equity);

        // Build downsampled chart sample (≤ 2000 bars) for the post-run replay chart
        var chartSample = DownsampleChart(chartBuffer, 2000);

        var result = ComputeMetrics(trades, request, equity, maxDrawdown, dataHash, allCandles, chartSample);

        if (circuitBreakerHit)
            result = result with
            {
                CircuitBreakerHit    = true,
                CircuitBreakerReason = circuitBreakerReason,
                Error = circuitBreakerReason   // surfaces in the frontend failure banner
            };

        return result;
    }

    /// <summary>
    /// Ratchets the trailing stop forward each bar.
    /// Called BEFORE TryClosePosition so the updated SL is used for this bar's exit check.
    ///
    /// Logic (R = |entryPrice − initialStopLoss|):
    ///   1. Update BestPrice (running high for longs, running low for shorts).
    ///   2. BreakEvenAt1R: once gain ≥ 1R, slide SL to entry price.
    ///   3. Full trail: once gain ≥ TrailActivationR, trail SL at (BestPrice − TrailOffsetR × R).
    ///      SL is a ratchet — it can only move in favor, never back.
    /// </summary>
    private static BacktestTrade ApplyTrailingStop(BacktestTrade trade, ClosedCandle candle, BacktestRequest req)
    {
        // Always update WorstPrice (MAE tracking) regardless of trailing stop settings
        var worstPrice = trade.Direction == "BUY"
            ? Math.Min(trade.WorstPrice, candle.Low)
            : Math.Max(trade.WorstPrice, candle.High);

        // Nothing to do for trailing stop if both features are disabled
        if (req.TrailActivationR <= 0 && !req.BreakEvenAt1R)
        {
            return worstPrice == trade.WorstPrice ? trade : trade with { WorstPrice = worstPrice };
        }

        var initialR = Math.Abs(trade.EntryPrice - trade.InitialStopLoss);
        if (initialR == 0) return worstPrice == trade.WorstPrice ? trade : trade with { WorstPrice = worstPrice };

        // Update the running best price (ratchet — only ever improves)
        var bestPrice = trade.Direction == "BUY"
            ? Math.Max(trade.BestPrice, candle.High)
            : Math.Min(trade.BestPrice, candle.Low);

        // How many R has the trade moved in our favour based on best price seen
        var gainR = trade.Direction == "BUY"
            ? (bestPrice - trade.EntryPrice) / initialR
            : (trade.EntryPrice - bestPrice) / initialR;

        var newSl       = trade.StopLoss;
        var trailActive = trade.TrailActive;

        // ── Break-even: slide to entry once 1R gained ─────────────────────
        if (req.BreakEvenAt1R && gainR >= 1m)
        {
            newSl = trade.Direction == "BUY"
                ? Math.Max(newSl, trade.EntryPrice)   // SL can only move up
                : Math.Min(newSl, trade.EntryPrice);  // SL can only move down
            trailActive = true;
        }

        // ── Full trailing stop ─────────────────────────────────────────────
        if (req.TrailActivationR > 0 && gainR >= req.TrailActivationR)
        {
            trailActive = true;
            var trailDistance = initialR * req.TrailOffsetR;
            var trailSl = trade.Direction == "BUY"
                ? bestPrice - trailDistance
                : bestPrice + trailDistance;

            // Ratchet: SL only moves in the winning direction
            newSl = trade.Direction == "BUY"
                ? Math.Max(newSl, trailSl)
                : Math.Min(newSl, trailSl);
        }

        return trade with { BestPrice = bestPrice, StopLoss = newSl, TrailActive = trailActive, WorstPrice = worstPrice };
    }

    private static BacktestTrade? TryClosePosition(BacktestTrade trade, ClosedCandle candle, BacktestRequest request)
    {
        string? exitReason = null;
        decimal exitPrice  = 0;

        if (trade.Direction == "BUY")
        {
            // Gap-fill: candle opened below SL — fill at open, not at SL
            if (candle.Open <= trade.StopLoss)
            {
                exitPrice  = candle.Open;
                exitReason = trade.TrailActive ? "TRAIL_STOP" : "STOP_LOSS";
            }
            else if (candle.Low <= trade.StopLoss && candle.High >= trade.TakeProfit)
            {
                // Both SL and TP touched: use candle midpoint heuristic to decide which hit first.
                // If mid > TP, assume TP was hit from below early in the bar; otherwise SL.
                var mid = (candle.High + candle.Low) / 2m;
                if (mid >= trade.TakeProfit)
                { exitPrice = trade.TakeProfit; exitReason = "TAKE_PROFIT"; }
                else
                { exitPrice = trade.StopLoss; exitReason = trade.TrailActive ? "TRAIL_STOP" : "STOP_LOSS"; }
            }
            else if (candle.Low <= trade.StopLoss)
            {
                exitPrice  = trade.StopLoss;
                exitReason = trade.TrailActive ? "TRAIL_STOP" : "STOP_LOSS";
            }
            else if (candle.High >= trade.TakeProfit)
            { exitPrice = trade.TakeProfit; exitReason = "TAKE_PROFIT"; }
        }
        else // SELL / short
        {
            // Gap-fill: candle opened above SL — fill at open
            if (candle.Open >= trade.StopLoss)
            {
                exitPrice  = candle.Open;
                exitReason = trade.TrailActive ? "TRAIL_STOP" : "STOP_LOSS";
            }
            else if (candle.High >= trade.StopLoss && candle.Low <= trade.TakeProfit)
            {
                var mid = (candle.High + candle.Low) / 2m;
                if (mid <= trade.TakeProfit)
                { exitPrice = trade.TakeProfit; exitReason = "TAKE_PROFIT"; }
                else
                { exitPrice = trade.StopLoss; exitReason = trade.TrailActive ? "TRAIL_STOP" : "STOP_LOSS"; }
            }
            else if (candle.High >= trade.StopLoss)
            {
                exitPrice  = trade.StopLoss;
                exitReason = trade.TrailActive ? "TRAIL_STOP" : "STOP_LOSS";
            }
            else if (candle.Low <= trade.TakeProfit)
            { exitPrice = trade.TakeProfit; exitReason = "TAKE_PROFIT"; }
        }

        if (exitReason == null) return null;

        var grossPnl = trade.Direction == "BUY"
            ? (exitPrice - trade.EntryPrice) * trade.Quantity
            : (trade.EntryPrice - exitPrice) * trade.Quantity;

        return trade with
        {
            ExitPrice  = exitPrice,
            ExitTime   = candle.CloseTime,
            GrossPnl   = grossPnl,
            ExitReason = exitReason
        };
    }

    private static int CalculatePositionSize(decimal equity, SignalResult signal, BacktestRequest request)
    {
        if (equity <= 0) return 0;
        if (signal.EntryPrice == null || signal.StopLoss == null) return 0;
        var entryPrice   = signal.EntryPrice.Value;
        if (entryPrice <= 0) return 0;

        var riskAmount   = equity * request.RiskPerTradePercent / 100m;
        var stopDistance = Math.Abs(entryPrice - signal.StopLoss.Value);
        if (stopDistance == 0) return 0;

        var sizeByRisk = (int)(riskAmount / stopDistance);

        // Cap position to 25% of equity to prevent over-leverage on tight stops
        var maxByCapital = (int)(equity * 0.25m / entryPrice);
        return Math.Min(sizeByRisk, maxByCapital);
    }

    private static BacktestResult ComputeMetrics(
        List<BacktestTrade> trades, BacktestRequest request,
        decimal finalEquity, decimal maxDrawdown, string dataHash,
        IReadOnlyList<ClosedCandle> allCandles,
        IReadOnlyList<BacktestChartBar> chartSample)
    {
        if (trades.Count == 0)
            return BacktestResult.Failed("No trades generated");

        var winTrades  = trades.Where(t => t.NetPnl > 0).ToList();
        var lossTrades = trades.Where(t => t.NetPnl <= 0).ToList();
        var totalPnl     = trades.Sum(t => t.NetPnl);
        var grossProfit  = winTrades.Sum(t => t.NetPnl);
        var grossLoss    = Math.Abs(lossTrades.Sum(t => t.NetPnl));
        var profitFactor = grossLoss == 0 ? decimal.MaxValue : grossProfit / grossLoss;
        var winRate      = (decimal)winTrades.Count / trades.Count;
        var avgWin       = winTrades.Count  > 0 ? winTrades.Average(t => t.NetPnl)              : 0m;
        var avgLoss      = lossTrades.Count > 0 ? Math.Abs(lossTrades.Average(t => t.NetPnl))   : 0m;
        var lossRate     = 1m - winRate;
        var expectancy   = winRate * avgWin - lossRate * avgLoss;
        var maxLots      = trades.Count > 0 ? trades.Max(t => t.Quantity) : 0;

        var maxConsecLosses = 0;
        var curConsecLosses = 0;
        foreach (var t in trades)
        {
            if (t.NetPnl <= 0) { curConsecLosses++; if (curConsecLosses > maxConsecLosses) maxConsecLosses = curConsecLosses; }
            else curConsecLosses = 0;
        }

        var returns   = trades.Select(t => (double)(t.NetPnl / request.InitialCapital)).ToArray();
        var avgReturn = returns.Average();
        var stdDev    = Math.Sqrt(returns.Select(r => Math.Pow(r - avgReturn, 2)).Average());
        // Per-trade Sharpe: no √252 annualization (that factor applies to daily/weekly series only)
        var sharpe    = stdDev == 0 ? 0 : (decimal)(avgReturn / stdDev);

        // Sortino: downside deviation (negative returns only)
        var negReturns  = returns.Where(r => r < 0).ToArray();
        var downDev     = negReturns.Length > 0
            ? Math.Sqrt(negReturns.Select(r => r * r).Average())
            : stdDev;
        var sortino     = downDev == 0 ? 0m : (decimal)(avgReturn / downDev);

        var totalReturn = (finalEquity - request.InitialCapital) / request.InitialCapital;
        var calmar      = maxDrawdown == 0 ? 0 : totalReturn / maxDrawdown;

        // Daily Sharpe (group trades by exit date, compute daily P&L)
        var dailySharpe  = ComputeGroupedSharpe(trades, t => t.ExitTime.ToInstant().InZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"]).Date.ToString());
        // Monthly Sharpe (group by year-month)
        var monthlySharpe = ComputeGroupedSharpe(trades, t =>
        {
            var d = t.ExitTime.ToInstant().InZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"]).Date;
            return $"{d.Year:D4}-{d.Month:D2}";
        });

        // Monthly breakdown
        var monthlyGroups = trades
            .GroupBy(t =>
            {
                var d = t.ExitTime.ToInstant().InZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"]).Date;
                return (d.Year, d.Month);
            })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g =>
            {
                var mWins = g.Count(t => t.NetPnl > 0);
                return new BacktestMonthlyBreakdown(
                    Year: g.Key.Year,
                    Month: g.Key.Month,
                    Pnl: g.Sum(t => t.NetPnl),
                    Trades: g.Count(),
                    WinRate: g.Count() > 0 ? (decimal)mWins / g.Count() : 0m);
            })
            .ToList();

        // Monthly win rate: % of months with positive P&L
        var monthlyWinRate = monthlyGroups.Count > 0
            ? (decimal)monthlyGroups.Count(m => m.Pnl > 0) / monthlyGroups.Count
            : 0m;

        // Yearly breakdown
        var yearlyGroups = trades
            .GroupBy(t => t.ExitTime.ToInstant().InZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"]).Year)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var yWins = g.Count(t => t.NetPnl > 0);
                var yPnl  = g.Sum(t => t.NetPnl);
                return new BacktestYearlyBreakdown(
                    Year: g.Key,
                    Pnl: yPnl,
                    Return: request.InitialCapital > 0 ? yPnl / request.InitialCapital : 0m,
                    Trades: g.Count(),
                    WinRate: g.Count() > 0 ? (decimal)yWins / g.Count() : 0m);
            })
            .ToList();

        // Drawdown recovery bars: number of bars from trough of max drawdown to next peak
        var ddRecoveryBars = ComputeDrawdownRecovery(trades, request.InitialCapital);

        // ── Advanced risk analytics (#89) ───────────────────────────────────────
        var sortedReturns = returns.OrderBy(r => r).ToArray();
        var returnCount   = sortedReturns.Length;

        // VaR95: 5th-percentile of per-trade returns (the worst 5%)
        var var95Idx = Math.Max(0, (int)Math.Floor(returnCount * 0.05) - 1);
        var var95    = returnCount > 0 ? (decimal)sortedReturns[var95Idx] : 0m;

        // CVaR95 (expected shortfall): average of returns below VaR95
        var tailCount = Math.Max(1, (int)Math.Floor(returnCount * 0.05));
        var cvar95    = returnCount > 0 ? (decimal)sortedReturns.Take(tailCount).Average() : 0m;

        // Omega ratio: sum of positive returns / |sum of negative returns| (threshold = 0)
        var positiveSum = returns.Where(r => r > 0).Sum();
        var negativeSum = Math.Abs(returns.Where(r => r < 0).Sum());
        var omega       = negativeSum < 1e-12 ? 0m : (decimal)(positiveSum / negativeSum);

        // Skewness: third standardized moment
        var m3       = returnCount > 0 ? returns.Select(r => Math.Pow(r - avgReturn, 3)).Average() : 0;
        var skewness = stdDev < 1e-12 ? 0m : (decimal)(m3 / Math.Pow(stdDev, 3));

        // Excess kurtosis: fourth standardized moment minus 3
        var m4       = returnCount > 0 ? returns.Select(r => Math.Pow(r - avgReturn, 4)).Average() : 0;
        var kurtosis = stdDev < 1e-12 ? 0m : (decimal)(m4 / Math.Pow(stdDev, 4)) - 3m;

        // Deployment readiness rating
        var (deployRating, deployRationale) = ComputeDeploymentRating(
            sharpe, maxDrawdown, winRate, trades.Count, sortino);

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
            AvgWin: avgWin,
            AvgLoss: avgLoss,
            MaxConsecutiveLosses: maxConsecLosses,
            ExpectancyPerTrade: expectancy,
            SortinoRatio: sortino,
            DailySharpe: dailySharpe,
            MonthlySharpe: monthlySharpe,
            MonthlyWinRate: monthlyWinRate,
            DrawdownRecoveryBars: ddRecoveryBars,
            MaxLots: maxLots,
            MonthlyBreakdown: monthlyGroups,
            YearlyBreakdown: yearlyGroups,
            Trades: trades,
            DataHash: dataHash,
            Error: null,
            ChartSample: chartSample,
            VaR95: var95,
            CVaR95: cvar95,
            OmegaRatio: omega,
            Skewness: skewness,
            Kurtosis: kurtosis,
            DeploymentRating: deployRating,
            DeploymentRationale: deployRationale);
    }

    private static (string Rating, string Rationale) ComputeDeploymentRating(
        decimal sharpe, decimal maxDrawdown, decimal winRate, int tradeCount, decimal sortino)
    {
        var issues = new List<string>();
        var passes = new List<string>();

        if (tradeCount < 30)
            issues.Add($"< 30 trades (only {tradeCount}; low statistical confidence)");
        else
            passes.Add($"{tradeCount} trades");

        if (maxDrawdown > 0.30m)
            issues.Add($"Max drawdown {maxDrawdown:P1} exceeds 30%");
        else
            passes.Add($"Max drawdown {maxDrawdown:P1}");

        if (sharpe < 0.5m)
            issues.Add($"Sharpe {sharpe:F2} < 0.5");
        else
            passes.Add($"Sharpe {sharpe:F2}");

        if (winRate < 0.35m)
            issues.Add($"Win rate {winRate:P1} < 35%");
        else
            passes.Add($"Win rate {winRate:P1}");

        string rating;
        if (issues.Count == 0 && sharpe >= 1.0m && maxDrawdown <= 0.20m)
            rating = "Green";
        else if (issues.Count <= 1 && sharpe >= 0.5m && maxDrawdown <= 0.30m)
            rating = "Amber";
        else
            rating = "Red";

        var rationale = issues.Count > 0
            ? $"Issues: {string.Join("; ", issues)}. Passes: {string.Join("; ", passes)}."
            : $"All checks passed: {string.Join("; ", passes)}.";

        return (rating, rationale);
    }

    private static decimal ComputeGroupedSharpe(List<BacktestTrade> trades, Func<BacktestTrade, string> keySelector)
    {
        var groups = trades.GroupBy(keySelector)
            .Select(g => (double)g.Sum(t => t.NetPnl))
            .ToArray();
        if (groups.Length < 2) return 0m;
        var avg    = groups.Average();
        var stdDev = Math.Sqrt(groups.Select(r => Math.Pow(r - avg, 2)).Average());
        if (stdDev == 0) return 0m;
        return (decimal)(avg / stdDev * Math.Sqrt(252));
    }

    private static int ComputeDrawdownRecovery(List<BacktestTrade> trades, decimal initialCapital)
    {
        if (trades.Count == 0) return 0;
        var equity = initialCapital;
        var peak   = equity;
        var maxDd  = 0m;
        var troughIdx = 0;

        for (int i = 0; i < trades.Count; i++)
        {
            equity += trades[i].NetPnl;
            if (equity > peak)
            {
                peak = equity;
            }
            var dd = peak > 0 ? (peak - equity) / peak : 0m;
            if (dd > maxDd) { maxDd = dd; troughIdx = i; }
        }

        if (maxDd == 0) return 0;

        // Count trades from trough back to new high
        var troughEquity = initialCapital;
        for (int i = 0; i <= troughIdx; i++) troughEquity += trades[i].NetPnl;

        var peak2 = troughEquity;
        for (int i = troughIdx + 1; i < trades.Count; i++)
        {
            peak2 += trades[i].NetPnl;
            if (peak2 >= peak) return i - troughIdx;
        }
        return -1; // Not yet recovered within the backtest period
    }

    /// <summary>
    /// Downsamples the full chart buffer to at most <paramref name="maxBars"/> bars.
    /// Signal bars (BUY/SELL) are always kept; the remaining slots are filled with a
    /// uniform sample from the non-signal bars so the chart retains visual shape.
    /// </summary>
    private static IReadOnlyList<BacktestChartBar> DownsampleChart(List<BacktestChartBar> buffer, int maxBars)
    {
        if (buffer.Count <= maxBars) return buffer;

        var signalBars     = buffer.Where(b => b.Signal != null).ToList();
        var nonSignalBars  = buffer.Where(b => b.Signal == null).ToList();
        var slotsForNonSig = Math.Max(0, maxBars - signalBars.Count);

        List<BacktestChartBar> sampled;
        if (nonSignalBars.Count <= slotsForNonSig)
        {
            sampled = nonSignalBars;
        }
        else
        {
            var step = (double)nonSignalBars.Count / slotsForNonSig;
            sampled = Enumerable.Range(0, slotsForNonSig)
                .Select(k => nonSignalBars[(int)(k * step)])
                .ToList();
        }

        // Merge + sort chronologically
        var merged = signalBars.Concat(sampled)
            .OrderBy(b => b.TimeMs)
            .ToList();
        return merged;
    }

    private static string ComputeReproducibilityHash(IReadOnlyList<ClosedCandle> candles, BacktestRequest request)
    {
        // Stream directly into SHA256 — avoids allocating a large string for 200k+ candle runs.
        using var sha = SHA256.Create();
        using var cs  = new System.Security.Cryptography.CryptoStream(
            System.IO.Stream.Null, sha, System.Security.Cryptography.CryptoStreamMode.Write);

        void Write(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            cs.Write(bytes, 0, bytes.Length);
        }

        Write($"strategy={request.StrategyName};params={request.ParametersJson};");
        Write($"symbol={request.InternalSymbol};tf={request.Timeframe};");
        Write($"from={request.FromDate};to={request.ToDate};capital={request.InitialCapital};");

        // Write an 8-byte little-endian long for each OHLCV field to avoid string overhead.
        Span<byte> buf = stackalloc byte[8];
        foreach (var c in candles)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(buf,
                c.OpenTime.ToInstant().ToUnixTimeMilliseconds());
            cs.Write(buf);
            Write($"{c.Open},{c.High},{c.Low},{c.Close},{c.Volume};");
        }

        cs.FlushFinalBlock();
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    /// <summary>
    /// Zero-copy view over a list segment. Replaces Take(n).ToList() in the inner loop.
    /// Reduces allocation from O(n²) to O(1) per bar — critical for 200k+ candle backtests.
    /// </summary>
    private sealed class ReadOnlyListSlice<T>(IReadOnlyList<T> source, int length) : IReadOnlyList<T>
    {
        public int Count => length;
        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)length) throw new ArgumentOutOfRangeException(nameof(index));
                return source[index];
            }
        }
        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < length; i++) yield return source[i];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

// BacktestRequest, BacktestResult, BacktestTrade, BacktestProgress, BacktestMonthlyBreakdown,
// BacktestYearlyBreakdown defined in rvs.AlgoTrader.Application.Services.IBacktestEngine
