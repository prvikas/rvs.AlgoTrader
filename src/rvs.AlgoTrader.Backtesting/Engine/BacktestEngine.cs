using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.MarketData;
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
    ILogger<BacktestEngine> logger,
    // FIB-3: Optional — populates SymbolIvRank in StrategyContext per bar using pre-fetched history.
    // Null when IV history data is unavailable (strategies degrade gracefully: FibOptionSpread skips IVP filter).
    IOptionIvRankService? ivRankService = null,
    // FIB-4: Optional — populates HasUpcomingEvent in StrategyContext per bar using pre-fetched event list.
    // Null when event calendar is not populated (strategies degrade gracefully: FibOptionSpread skips event filter).
    IEventCalendarService? eventCalendar = null,
    // ALL-SPREADS-1: Optional — enables spread simulation using Black-Scholes pricing.
    // When null, spread signals are logged as warnings and skipped (IC-1 fallback).
    // Registered as Singleton; safe to inject into a Scoped BacktestEngine.
    IBlackScholesEngine? bsEngine = null) : IBacktestEngine
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

        IStrategy strategy;
        try
        {
            strategy = strategyFactory.Create(request.StrategyName, request.ParametersJson);
        }
        catch (ArgumentException ex)
        {
            return BacktestResult.Failed($"Invalid strategy parameter '{ex.ParamName}': {ex.Message}");
        }
        var warmupBars = Math.Max(request.WarmupBars, strategy.MinWarmupBars);
        if (allCandles.Count < warmupBars + 1)
            return BacktestResult.Failed($"Insufficient candle data (need > {warmupBars} bars, got {allCandles.Count})");

        var dataHash = ComputeReproducibilityHash(allCandles, request);

        var trades    = new List<BacktestTrade>();
        var equity    = request.InitialCapital;
        var peakEquity = equity;
        var maxDrawdown = 0m;
        BacktestTrade?   openTrade  = null;
        OpenSpreadSim?   openSpread = null;
        var totalBars  = allCandles.Count;
        var skippedSignals = 0;
        var circuitBreakerHit = false;
        string? circuitBreakerReason = null;
        var circuitBreakerFloor = request.CircuitBreakerPct > 0
            ? request.InitialCapital * request.CircuitBreakerPct
            : 0m;
        // Strategy-driven exit: set to true when the strategy returns ExitLong/ExitShort.
        // The actual close is deferred to the OPEN of the next candle (matching PineScript next-bar fill).
        bool pendingStrategyExit = false;
        string  pendingExitReason = string.Empty;

        // Progress reporting: every 1% of bars (minimum 1)
        var jobId        = request.JobId ?? "backtest";
        var progressStep = Math.Max(1, totalBars / 100);

        // ── FIB-3: Pre-fetch IV history for rolling per-bar IV rank ────────────
        // Loaded once before the loop; sorted ascending by date for binary-search lookups.
        // Empty list when IOptionIvRankService is not available or has no data.
        IReadOnlyList<(NodaTime.LocalDate Date, decimal AtmIv)> ivHistory =
            Array.Empty<(NodaTime.LocalDate, decimal)>();
        if (ivRankService != null)
        {
            try
            {
                ivHistory = await ivRankService.GetHistoryRangeAsync(
                    request.InternalSymbol, request.FromDate, request.ToDate, ct);
                logger.LogInformation("[Backtest] FIB-3: loaded {Count} IV history records for {Symbol}",
                    ivHistory.Count, request.InternalSymbol);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Backtest] FIB-3: failed to load IV history — SymbolIvRank will be null for all bars");
            }
        }

        // ── FIB-4: Pre-fetch market events for per-bar HasUpcomingEvent ────────
        // Group into a HashSet of dates that have High-impact events for fast O(1) per-bar lookups.
        // ExclusionDays window is applied per bar (check up to N future dates).
        IReadOnlyList<MarketEventDto> prefetchedEvents =
            Array.Empty<MarketEventDto>();
        if (eventCalendar != null)
        {
            try
            {
                // Add a 14-day forward buffer so bars near toDate can still check the window.
                var eventsTo = request.ToDate.PlusDays(14);
                prefetchedEvents = await eventCalendar.GetRangeAsync(
                    request.FromDate, eventsTo, ct: ct);
                logger.LogInformation("[Backtest] FIB-4: loaded {Count} market events for {From}–{To}",
                    prefetchedEvents.Count, request.FromDate, eventsTo);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Backtest] FIB-4: failed to load event calendar — HasUpcomingEvent will be false for all bars");
            }
        }

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

            // ── Strategy-driven exit: execute deferred close at this bar's open ──────────
            // Set by the exit-signal evaluation block below (previous bar returned ExitLong/ExitShort).
            // PineScript semantics: signal fires on bar close, fill executes at next bar's open.
            if (pendingStrategyExit && openTrade != null)
            {
                var seExitPrice = current.Open;
                var seGross     = openTrade.Direction == "BUY"
                    ? (seExitPrice - openTrade.EntryPrice) * openTrade.Quantity
                    : (openTrade.EntryPrice - seExitPrice) * openTrade.Quantity;
                var seCosts     = costCalc.Calculate(seExitPrice * openTrade.Quantity, openTrade.Direction != "BUY", costProfile);
                trades.Add(openTrade with
                {
                    ExitPrice      = seExitPrice,
                    ExitTime       = current.OpenTime,
                    GrossPnl       = seGross,
                    NetPnl         = seGross - openTrade.EntryCommission - seCosts.Total,
                    ExitReason     = $"STRATEGY_EXIT:{pendingExitReason}",
                    ExitCommission = seCosts.Total,
                    HoldingBars    = Math.Max(0, i - openTrade.EntryBarIndex),
                });
                equity    += seGross - seCosts.Total;
                openTrade          = null;
                pendingStrategyExit = false;

                if (equity > peakEquity) peakEquity = equity;
                var seDd = peakEquity > 0 ? (peakEquity - equity) / peakEquity : 0m;
                if (seDd > maxDrawdown) maxDrawdown = seDd;

                if (equity <= 0)
                {
                    circuitBreakerHit    = true;
                    circuitBreakerReason = $"Equity ₹{equity:F2} — account bankrupt (strategy exit). Backtest stopped.";
                    logger.LogWarning("[Backtest] Bankruptcy after strategy exit at bar {I}", i);
                    break;
                }
                if (circuitBreakerFloor > 0 && equity < circuitBreakerFloor)
                {
                    circuitBreakerHit    = true;
                    circuitBreakerReason = $"Equity ₹{equity:F0} fell below circuit breaker floor after strategy exit.";
                    logger.LogWarning("[Backtest] Circuit breaker hit after strategy exit at bar {I}", i);
                    break;
                }
                // Fall through — evaluate for new entry on this bar (position is now flat).
            }

            // ── ALL-SPREADS-1: monitor open spread position ───────────────────
            if (openSpread != null)
            {
                var (spreadClosed, closeReason, spreadPnl, exitValue) =
                    TryCloseSpreadSim(openSpread, current, bsEngine!);

                if (spreadClosed)
                {
                    decimal exitComm   = request.BrokerageFlatPerSide * openSpread.Legs.Count;
                    decimal netSpreadPnl = spreadPnl - exitComm;
                    equity += netSpreadPnl; // EntryCommission already deducted at entry

                    var spreadTrade = new BacktestTrade(
                        Id:              Guid.NewGuid(),
                        Symbol:          request.InternalSymbol,
                        Direction:       openSpread.NetCredit >= 0 ? "CREDIT-SPREAD" : "DEBIT-SPREAD",
                        Quantity:        openSpread.Legs.Count,
                        EntryPrice:      Math.Abs(openSpread.NetCredit),
                        ExitPrice:       exitValue,
                        StopLoss:        openSpread.UnderlyingStop ?? 0m,
                        TakeProfit:      Math.Abs(openSpread.NetCredit) * openSpread.ProfitTargetPct,
                        EntryTime:       openSpread.EntryTime,
                        ExitTime:        current.OpenTime,
                        GrossPnl:        spreadPnl,
                        NetPnl:          netSpreadPnl,
                        ExitReason:      closeReason,
                        EntryCommission: openSpread.EntryCommission,
                        ExitCommission:  exitComm,
                        HoldingBars:     Math.Max(0, i - openSpread.EntryBarIndex),
                        LegsJson:        BuildLegsJson(openSpread, request.BrokerageFlatPerSide));
                    trades.Add(spreadTrade);
                    openSpread = null;

                    if (equity > peakEquity) peakEquity = equity;
                    var spreadDd = peakEquity > 0 ? (peakEquity - equity) / peakEquity : 0m;
                    if (spreadDd > maxDrawdown) maxDrawdown = spreadDd;

                    if (equity <= 0)
                    {
                        circuitBreakerHit    = true;
                        circuitBreakerReason = $"Equity ₹{equity:F2} — account bankrupt (spread). Backtest stopped.";
                        logger.LogWarning("[Backtest] Bankruptcy after spread close at bar {I}", i);
                        break;
                    }
                    if (circuitBreakerFloor > 0 && equity < circuitBreakerFloor)
                    {
                        circuitBreakerHit    = true;
                        circuitBreakerReason = $"Equity ₹{equity:F0} fell below circuit breaker floor after spread close.";
                        logger.LogWarning("[Backtest] Circuit breaker hit after spread close at bar {I}", i);
                        break;
                    }
                }

                AddChartBar(new BacktestChartBar(
                    current.OpenTime.ToInstant().ToUnixTimeMilliseconds(),
                    current.Open, current.High, current.Low, current.Close, current.Volume,
                    null, null, null, null, null));

                if (progress != null && i % progressStep == 0)
                {
                    var pct2 = (decimal)(i - warmupBars) / Math.Max(1, totalBars - warmupBars) * 100m;
                    progress.Report(new BacktestProgress(jobId, i, totalBars, Math.Min(99m, pct2),
                        trades.Count, equity, SnapshotRollingWindow()));
                }
                continue;
            }

            if (openTrade != null)
            {
                // Apply trailing stop / break-even BEFORE checking SL/TP so the
                // updated SL is used in this bar's exit check.
                openTrade = ApplyTrailingStop(openTrade, current, request);

                var closed = TryClosePosition(openTrade, current, request);
                if (closed != null)
                {
                    // EntryCommission was already deducted from equity at trade open.
                    // Only deduct exit commission here; use stored EntryCommission for NetPnl.
                    var exitCosts = costCalc.Calculate(closed.ExitPrice * closed.Quantity, closed.Direction != "BUY", costProfile);
                    closed = closed with
                    {
                        ExitCommission = exitCosts.Total,
                        NetPnl         = closed.GrossPnl - closed.EntryCommission - exitCosts.Total,
                        HoldingBars    = Math.Max(0, i - closed.EntryBarIndex),
                    };
                    equity += closed.GrossPnl - exitCosts.Total; // EntryCommission already deducted at open
                    trades.Add(closed);
                    openTrade  = null;

                    if (equity > peakEquity) peakEquity = equity;
                    var drawdown = peakEquity > 0 ? (peakEquity - equity) / peakEquity : 0m;
                    if (drawdown > maxDrawdown) maxDrawdown = drawdown;

                    // ── Bankruptcy guard: stop if equity reaches zero or below ────
                    if (equity <= 0)
                    {
                        circuitBreakerHit    = true;
                        circuitBreakerReason = $"Equity ₹{equity:F2} — account bankrupt. Backtest stopped.";
                        logger.LogWarning("[Backtest] Bankruptcy at bar {I}: {Reason}", i, circuitBreakerReason);
                        break;
                    }

                    // ── Circuit breaker: stop early if equity falls below the floor ──
                    if (circuitBreakerFloor > 0 && equity < circuitBreakerFloor)
                    {
                        circuitBreakerHit    = true;
                        circuitBreakerReason = $"Equity ₹{equity:F0} fell below {request.CircuitBreakerPct * 100:F0}% of initial capital ₹{request.InitialCapital:F0}. Loss-making strategy — backtest stopped early.";
                        logger.LogWarning("[Backtest] Circuit breaker triggered at bar {I}: {Reason}", i, circuitBreakerReason);
                        break;
                    }
                }

                // ── Strategy-driven exit evaluation ───────────────────────────────────
                // Re-evaluate the strategy with CurrentPosition set so that strategies using
                // indicator-based exits (e.g. SMA crossover) can return ExitLong / ExitShort.
                // Backward-compatible: existing strategies return Buy/Sell/Hold and are unaffected.
                // If an exit signal fires, set pendingStrategyExit = true; fill executes at the
                // OPEN of the next candle (consistent with the PineScript next-bar fill model).
                if (openTrade != null) // guard: may have been closed by SL/TP above
                {
                    var exitVisibleCandles = new ReadOnlyListSlice<ClosedCandle>(allCandles, i + 1);
                    var exitCtx = new StrategyContext(
                        Guid.Empty, request.InternalSymbol, request.Timeframe,
                        exitVisibleCandles, request.ParametersJson, "backtest",
                        CurrentPosition: openTrade.Direction == "BUY" ? "LONG" : "SHORT");
                    try
                    {
                        var exitSignal = await strategy.EvaluateAsync(exitCtx, ct);
                        if ((openTrade.Direction == "BUY"  && exitSignal.Signal == SignalType.ExitLong) ||
                            (openTrade.Direction == "SELL" && exitSignal.Signal == SignalType.ExitShort))
                        {
                            pendingStrategyExit = true;
                            pendingExitReason   = exitSignal.Reason ?? "strategy condition";
                            logger.LogDebug("[Backtest] Strategy exit queued at bar {I}: {Reason}", i, pendingExitReason);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "[Backtest] Strategy exit evaluation failed at bar {I} — continuing with SL/TP only", i);
                    }
                }

                // Chart: add bar (strategy indicators available when exit check ran)
                AddChartBar(new BacktestChartBar(
                    TimeMs: current.OpenTime.ToInstant().ToUnixTimeMilliseconds(),
                    Open: current.Open, High: current.High, Low: current.Low, Close: current.Close,
                    Volume: current.Volume,
                    Signal: pendingStrategyExit ? "EXIT" : null, SignalPrice: null,
                    StopLoss: null, TakeProfit: null, Indicators: null));

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

            // FIB-3: Compute rolling IvRankSnapshot for this bar from the pre-fetched IV history.
            // Uses up to 252 most recent records on or before the current bar's date.
            var barDate = current.OpenTime.Date;
            var barIvRank = ComputeIvRankAsOf(barDate, ivHistory);

            // FIB-4: Determine HasUpcomingEvent for this bar using the pre-fetched event list.
            // FibOptionSpreadConfig.ExclusionDays is not known here (strategy config may vary),
            // so we use a conservative 7-day window — strategies with smaller windows will not
            // get false positives, just a slightly broader pre-filter that is re-checked internally.
            const int BacktestEventWindow = 7;
            var hasUpcomingEvent = prefetchedEvents.Any(e =>
                e.Impact == "High" &&
                e.EventDate >= barDate &&
                e.EventDate <= barDate.PlusDays(BacktestEventWindow));

            // BT-OPT-1: Build synthetic OptionChainSnapshot from historical IV so option strategies
            // can evaluate their IV filters and chain-based conditions in backtest mode.
            // Only built when IBlackScholesEngine is registered (spread simulation enabled).
            //
            // Multi-strike OI ladder (5 strikes each side of ATM):
            //   • OI decays as 200,000 × 0.65^n from ATM outward (realistic pyramid)
            //   • OI skew driven by prior bar direction: bullish → CE OI > PE OI → PCR ≈ 0.54
            //     bearish → PE OI > CE OI → PCR ≈ 2.0 (matches real NIFTY/BANKNIFTY patterns)
            //   • OiChange sign mirrors bar direction for PutCallRatioChangeOI
            //   • Put IV skew: +0.5%/strike for OTM puts (negative-gamma / vol-skew realism)
            //   • Call IV discount: -0.3%/strike for OTM calls
            //   • MaxPain and CeMaxOiStrike/PeMaxOiStrike now reflect OI walls correctly
            //   • Far chain IV = 85% of near IV (normal term structure)
            OptionChainSnapshot? syntheticNearChain = null;
            OptionChainSnapshot? syntheticFarChain  = null;
            if (bsEngine != null)
            {
                var (_, _, si)  = ExtractSpreadConfig(request.ParametersJson);
                decimal atmIv   = barIvRank?.CurrentIv ?? 15m;
                decimal atmK    = Math.Round(current.Close / si) * si;
                var nearExpiry  = NearestWeeklyExpiry(barDate);
                var farExpiry   = NearestMonthlyExpiry(barDate);

                bool prevBarBullish = i > 0 && allCandles[i - 1].Close > allCandles[i - 1].Open;
                var nearLegs = BuildSyntheticLegs(atmK, si, atmIv, prevBarBullish, nStrikes: 5);
                syntheticNearChain = new OptionChainSnapshot(
                    request.InternalSymbol, current.OpenTime.ToInstant(),
                    current.Close, nearExpiry, nearLegs);

                // CalendarSpread CS-2: far chain must have lower IV to produce a positive slope.
                // 85% of near IV approximates the normal term structure seen in NIFTY/BANKNIFTY.
                decimal farIv  = Math.Max(5m, atmIv * 0.85m);
                var farLegs = BuildSyntheticLegs(atmK, si, farIv, prevBarBullish, nStrikes: 5);
                syntheticFarChain = new OptionChainSnapshot(
                    request.InternalSymbol, current.OpenTime.ToInstant(),
                    current.Close, farExpiry, farLegs);
            }

            var ctx = new StrategyContext(
                Guid.Empty,
                request.InternalSymbol,
                request.Timeframe,
                visibleCandles,
                request.ParametersJson,
                "backtest",
                OptionChain:     syntheticNearChain,
                SymbolIvRank:    barIvRank,
                HasUpcomingEvent: hasUpcomingEvent,
                NearExpiryChain: syntheticNearChain,
                FarExpiryChain:  syntheticFarChain);

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

            // ExitLong/ExitShort with no open position = ignore (already handled above when position was open).
            if (signal.Signal is not (SignalType.Buy or SignalType.Sell)) continue;

            // ALL-SPREADS-1: Simulate spread entry via Black-Scholes pricing.
            // Falls back to skip+warning when IBlackScholesEngine is unavailable (e.g. unit tests
            // that construct BacktestEngine without DI).
            if (signal.Spread != null)
            {
                if (bsEngine == null)
                {
                    skippedSignals++;
                    logger.LogWarning("[Backtest] {JobId} — spread signal ({SpreadType}): " +
                        "IBlackScholesEngine not available, spread simulation disabled.",
                        jobId, signal.Spread.SpreadType);
                    continue;
                }

                var entryIvPct = barIvRank?.CurrentIv ?? 15m; // fallback 15% when no IV history
                openSpread = EnterSpreadSim(signal.Spread, current, barDate, entryIvPct, request, bsEngine);
                if (openSpread != null)
                {
                    openSpread = openSpread with { EntryBarIndex = i }; // for HoldingBars at close
                    equity -= openSpread.EntryCommission;
                    logger.LogDebug("[Backtest] Spread entered {Type} bar={Bar} credit={Credit:F2} legs={Legs}",
                        signal.Spread.SpreadType, i, openSpread.NetCredit, openSpread.Legs.Count);
                }
                continue;
            }

            var positionSize = CalculatePositionSize(equity, signal, request);
            if (positionSize <= 0)
            {
                skippedSignals++;
                // Distinguish capital floor (equity too low to afford 1 share) from stop-too-tight
                var maxByCapital = signal.EntryPrice > 0 ? (int)(equity * 0.25m / signal.EntryPrice.Value) : 0;
                if (maxByCapital <= 0)
                    logger.LogDebug("[Backtest] Signal skipped: capital floor reached (equity={Equity} entry={Entry})",
                        equity, signal.EntryPrice);
                else
                    logger.LogDebug("[Backtest] Signal skipped (size=0) bar={Bar} signal={Signal} entry={Entry} sl={SL} equity={Equity}",
                        i, signal.Signal, signal.EntryPrice, signal.StopLoss, equity);
                continue;
            }

            decimal entryPrice;
            ZonedDateTime entryTime;
            decimal slipAmount    = 0m;
            int     entryBarIndex = i;  // bar index at fill

            if (request.FillModel == FillModel.SignalBarClose)
            {
                entryPrice = current.Close;
                entryTime  = current.CloseTime;
                // entryBarIndex stays at i (signal bar = fill bar)
            }
            else
            {
                if (i + 1 >= totalBars) break;
                var nextBar = allCandles[i + 1];
                entryPrice    = nextBar.Open;
                entryTime     = nextBar.OpenTime;
                entryBarIndex = i + 1; // fills on next bar's open

                if (request.FillModel == FillModel.NextBarOpenPlusSlippage && request.SlippageBasisPoints > 0)
                {
                    var slipFraction = request.SlippageBasisPoints / 10_000m;
                    var rawEntry     = entryPrice;
                    entryPrice = signal.Signal == SignalType.Buy
                        ? entryPrice * (1m + slipFraction)
                        : entryPrice * (1m - slipFraction);
                    slipAmount = Math.Abs(entryPrice - rawEntry) * positionSize; // ₹ cost of slippage
                }
            }

            var initialSl = signal.StopLoss ?? entryPrice * 0.99m;

            // Deduct entry commission immediately so equity is accurate for subsequent sizing
            var entryCostsOnOpen = costCalc.Calculate(entryPrice * positionSize, signal.Signal == SignalType.Buy, costProfile);
            equity -= entryCostsOnOpen.Total;

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
                TrailActive:     false,
                EntryCommission: entryCostsOnOpen.Total,
                SlippageAmount:  slipAmount,
                EntryBarIndex:   entryBarIndex);
        }

        if (openTrade != null && allCandles.Count > 0)
        {
            var lastCandle    = allCandles[^1];
            var exitPrice     = lastCandle.Close;
            var grossPnl      = openTrade.Direction == "BUY"
                ? (exitPrice - openTrade.EntryPrice) * openTrade.Quantity
                : (openTrade.EntryPrice - exitPrice) * openTrade.Quantity;
            var exitCostsEod = costCalc.Calculate(exitPrice * openTrade.Quantity, openTrade.Direction != "BUY", costProfile);
            var netPnlEod    = grossPnl - openTrade.EntryCommission - exitCostsEod.Total;
            equity += grossPnl - exitCostsEod.Total; // EntryCommission already deducted at open
            trades.Add(openTrade with
            {
                ExitPrice      = exitPrice,
                ExitTime       = lastCandle.CloseTime,
                GrossPnl       = grossPnl,
                NetPnl         = netPnlEod,
                ExitReason     = "END_OF_DATA",
                ExitCommission = exitCostsEod.Total,
                HoldingBars    = Math.Max(0, totalBars - 1 - openTrade.EntryBarIndex),
            });
        }

        // Close any open spread position at end of data
        if (openSpread != null && bsEngine != null && allCandles.Count > 0)
        {
            var lastBar = allCandles[^1];
            decimal eodValue   = PriceSpreadSim(openSpread, lastBar.Close, lastBar.OpenTime.Date, bsEngine);
            decimal eodPnl     = openSpread.NetCredit - eodValue; // unified formula — see TryCloseSpreadSim
            decimal exitComm   = request.BrokerageFlatPerSide * openSpread.Legs.Count;
            decimal netEodPnl  = eodPnl - exitComm;
            equity += netEodPnl;
            trades.Add(new BacktestTrade(
                Id:              Guid.NewGuid(),
                Symbol:          request.InternalSymbol,
                Direction:       openSpread.NetCredit >= 0 ? "CREDIT-SPREAD" : "DEBIT-SPREAD",
                Quantity:        openSpread.Legs.Count,
                EntryPrice:      Math.Abs(openSpread.NetCredit),
                ExitPrice:       eodValue,
                StopLoss:        openSpread.UnderlyingStop ?? 0m,
                TakeProfit:      Math.Abs(openSpread.NetCredit) * openSpread.ProfitTargetPct,
                EntryTime:       openSpread.EntryTime,
                ExitTime:        lastBar.CloseTime,
                GrossPnl:        eodPnl,
                NetPnl:          netEodPnl,
                ExitReason:      "END_OF_DATA",
                EntryCommission: openSpread.EntryCommission,
                ExitCommission:  exitComm,
                HoldingBars:     Math.Max(0, totalBars - 1 - openSpread.EntryBarIndex),
                LegsJson:        BuildLegsJson(openSpread, request.BrokerageFlatPerSide)));
        }

        // Final progress (100%)
        progress?.Report(new BacktestProgress(jobId, totalBars, totalBars, 100m, trades.Count, equity));

        logger.LogInformation("[Backtest] Finished \u2014 {Trades} trades, equity \u20b9{Equity:F0}",
            trades.Count, equity);

        // Build downsampled chart sample (≤ 2000 bars) for the post-run replay chart
        var chartSample = DownsampleChart(chartBuffer, 2000);

        if (skippedSignals > 0)
            logger.LogInformation("[Backtest] {Skipped} signal(s) dropped (size=0) — check equity, stop distance, or null SL/entry.",
                skippedSignals);

        var result = ComputeMetrics(trades, request, equity, maxDrawdown, dataHash, allCandles, chartSample);
        result = result with { SkippedSignalCount = skippedSignals };

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
    /// FIB-3: Compute rolling IV rank/percentile for a specific bar date from pre-fetched IV history.
    /// History is ordered by date descending; takes up to 252 entries on or before barDate.
    /// Returns null if fewer than 30 data points are available (warmup requirement).
    /// </summary>
    private static Domain.ValueObjects.IvRankSnapshot? ComputeIvRankAsOf(
        NodaTime.LocalDate barDate,
        IReadOnlyList<(NodaTime.LocalDate Date, decimal AtmIv)> ivHistory)
    {
        const int MinDataPoints = 30;
        const int LookbackDays  = 252;

        if (ivHistory.Count == 0) return null;

        // Take up to LookbackDays entries that are on or before barDate (history is desc-sorted)
        var window = new List<decimal>(LookbackDays);
        foreach (var (date, iv) in ivHistory)
        {
            if (date > barDate) continue;
            window.Add(iv);
            if (window.Count == LookbackDays) break;
        }

        if (window.Count < MinDataPoints) return null;

        decimal currentIv    = window[0]; // most recent (history is desc-sorted)
        decimal high52        = window.Max();
        decimal low52         = window.Min();
        decimal range         = high52 - low52;
        decimal ivRank        = range == 0 ? 50m : Math.Round((currentIv - low52) / range * 100m, 1);
        int     below         = window.Count(v => v < currentIv);
        decimal ivPercentile  = Math.Round((decimal)below / window.Count * 100m, 1);

        return new IvRankSnapshot(
            UnderlyingSymbol: "backtest",
            CurrentIv:        currentIv,
            IvRank:           ivRank,
            IvPercentile:     ivPercentile,
            WeekHigh52:       high52,
            WeekLow52:        low52,
            Regime:           IvRankSnapshot.ClassifyRegime(ivRank),
            DataPointsUsed:   window.Count);
    }

    // ── ALL-SPREADS-1: Spread simulation helpers ─────────────────────────────

    /// <summary>
    /// Internal state for one simulated multi-leg spread position in the backtest.
    /// Created at entry; mutated per bar by TryCloseSpreadSim.
    /// </summary>
    private sealed record OpenSpreadSim(
        SpreadSignalResult         Signal,
        LocalDate                  ExpiryDate,
        // LegExpiry added per-leg so PriceSpreadSim can use the correct TTE for each leg
        // (CalendarSpread has two different expiries — near weekly and far monthly).
        List<(SpreadLeg Leg, decimal Strike, decimal EntryLegPrice, LocalDate LegExpiry)> Legs,
        decimal                    NetCredit,          // positive = credit received; negative = debit paid
        decimal                    MaxLossMultiple,    // from strategy config (SS-1)
        decimal                    ProfitTargetPct,    // fraction of credit to capture (e.g. 0.50)
        decimal?                   UnderlyingStop,     // FIB-2: fib786 stop level; null = none
        ZonedDateTime              EntryTime,
        decimal                    EntrySpot,
        double                     EntryIvFraction,    // e.g. 0.15 = 15%
        decimal                    EntryCommission,
        int                        EntryBarIndex = 0   // allCandles[] index at entry bar
    );

    /// <summary>
    /// Resolve strikes, price each leg via Black-Scholes, record net premium, return the open position.
    /// Returns null if B-S pricing produces invalid results (zero/negative premium on all legs).
    /// </summary>
    private static OpenSpreadSim? EnterSpreadSim(
        SpreadSignalResult signal,
        ClosedCandle       current,
        LocalDate          barDate,
        decimal            atmIvPct,
        BacktestRequest    request,
        IBlackScholesEngine bs)
    {
        var (maxLossMultiple, profitTargetPct, strikeInterval) = ExtractSpreadConfig(request.ParametersJson);

        // Each leg may use near or far expiry depending on NearestWeekly
        var nearExpiry = NearestWeeklyExpiry(barDate);
        var farExpiry  = NearestMonthlyExpiry(barDate);

        double ivFrac      = Math.Max(0.05, (double)(atmIvPct / 100m));
        const double RFR   = 0.065; // RBI repo rate
        decimal entryComm  = request.BrokerageFlatPerSide * signal.Legs.Count;

        var resolvedLegs = new List<(SpreadLeg Leg, decimal Strike, decimal EntryLegPrice, LocalDate LegExpiry)>();
        decimal netCredit = 0m;

        foreach (var leg in signal.Legs)
        {
            var legExpiry = leg.NearestWeekly ? nearExpiry : farExpiry;
            double tte    = Math.Max(0.001, (legExpiry - barDate).Days / 365.0);

            decimal strike = ResolveStrike(leg, current.Close, ivFrac, tte, RFR, strikeInterval, bs);
            if (strike <= 0) continue;

            var greeks = bs.Compute(current.Close, strike, tte, ivFrac, RFR,
                leg.OptionType == OptionType.Call);
            decimal legPremium = Math.Max(0m, greeks.TheoreticalPrice);

            // Credit = premium received (sell); Debit = premium paid (buy)
            netCredit += leg.Direction == OrderDirection.Sell ? legPremium : -legPremium;
            resolvedLegs.Add((leg, strike, legPremium, legExpiry));
        }

        if (resolvedLegs.Count == 0) return null;

        // Determine expiry for the position: use the near expiry
        // (for CalendarSpread, near expiry is when the short near leg expires)
        return new OpenSpreadSim(
            Signal:          signal,
            ExpiryDate:      nearExpiry,
            Legs:            resolvedLegs,
            NetCredit:       netCredit,
            MaxLossMultiple: maxLossMultiple,
            ProfitTargetPct: profitTargetPct,
            UnderlyingStop:  signal.UnderlyingStopLevel,
            EntryTime:       current.OpenTime,
            EntrySpot:       current.Close,
            EntryIvFraction: ivFrac,
            EntryCommission: entryComm);
    }

    /// <summary>
    /// Re-price the spread on the current bar and check exit conditions.
    /// Returns (closed, closeReason, grossPnl, currentSpreadValue).
    /// </summary>
    private static (bool Closed, string Reason, decimal GrossPnl, decimal CurrentValue)
        TryCloseSpreadSim(OpenSpreadSim pos, ClosedCandle current, IBlackScholesEngine bs)
    {
        var barDate = current.OpenTime.Date;
        decimal spot = current.Close;

        // Expiry: price all legs at T→0 (intrinsic only).
        // P&L = NetCredit − costToClose for all spread types (credit and debit).
        if (barDate >= pos.ExpiryDate)
        {
            decimal expiryValue = PriceSpreadSim(pos, spot, pos.ExpiryDate, bs);
            decimal pnl = pos.NetCredit - expiryValue;
            return (true, "EXPIRY", pnl, expiryValue);
        }

        // FIB-2 / SS-1 underlying stop: force-close when spot breaches the fib786 level
        if (pos.UnderlyingStop.HasValue)
        {
            bool isUptrend  = pos.Signal.SpreadType.Contains("put", StringComparison.OrdinalIgnoreCase);
            bool breached   = isUptrend
                ? spot < pos.UnderlyingStop.Value   // put spread: stop below swing support
                : spot > pos.UnderlyingStop.Value;  // call spread: stop above swing resistance
            if (breached)
            {
                decimal val = PriceSpreadSim(pos, spot, barDate, bs);
                decimal pnl = pos.NetCredit - val;
                return (true, "UNDERLYING_STOP", pnl, val);
            }
        }

        decimal currentValue = PriceSpreadSim(pos, spot, barDate, bs);
        // Unified P&L formula: NetCredit − cost-to-close.
        // Proof: credit spread: receive C, pay C' to close → PnL = C − C'.
        //        debit spread:  pay D, receive V when closing → V − D = −D − (−V) = NetCredit − costToClose.
        decimal runningPnl   = pos.NetCredit - currentValue;

        // SS-1: MaxLossMultiple — close when loss exceeds MaxLossMultiple × premium
        decimal maxPremium = Math.Abs(pos.NetCredit);
        if (maxPremium > 0 && runningPnl <= -(maxPremium * pos.MaxLossMultiple))
            return (true, "MAX_LOSS_MULTIPLE", runningPnl, currentValue);

        // Profit target: for credit spreads, capture ProfitTargetPct of entry credit
        if (maxPremium > 0 && runningPnl >= maxPremium * pos.ProfitTargetPct)
            return (true, "PROFIT_TARGET", runningPnl, currentValue);

        return (false, "", 0m, currentValue);
    }

    /// <summary>
    /// Builds a realistic multi-strike synthetic option chain for backtest simulation.
    /// Generates ATM + nStrikes OTM legs on each side with OI pyramid and directional skew.
    ///
    /// OI structure:
    ///   • Base OI at ATM = 200,000 lots; decays by factor 0.65 per strike outward
    ///   • Bullish prev bar  → CE OI inflated ×1.20, PE OI compressed ×0.65 → PCR ≈ 0.54
    ///   • Bearish prev bar  → CE OI compressed ×0.70, PE OI inflated ×1.40  → PCR ≈ 2.0
    ///   • OiChange sign: bullish → CE adds OI (call writing), PE removes OI; vice versa
    /// IV structure:
    ///   • Put IV skew: +0.5% per OTM strike (realistic negative-gamma skew)
    ///   • Call IV discount: -0.3% per OTM strike (lower demand for OTM calls in India)
    ///   • ATM delta ≈ ±0.50; OTM legs scale by 0.5^(n+1) approximation
    /// </summary>
    internal static List<OptionLeg> BuildSyntheticLegs(
        decimal atmStrike, decimal strikeInterval, decimal atmIv,
        bool prevBarBullish, int nStrikes = 5)
    {
        // OI skew multipliers based on prior bar direction
        decimal ceSkim = prevBarBullish ? 1.20m : 0.70m;
        decimal peSkim = prevBarBullish ? 0.65m : 1.40m;

        // OiChange direction: on bullish bar, call writers add CE OI (+), put writers close PE OI (-)
        int ceChangeSign = prevBarBullish ? +1 : -1;
        int peChangeSign = prevBarBullish ? -1 : +1;

        var legs = new List<OptionLeg>(capacity: (nStrikes + 1) * 2);

        for (int n = 0; n <= nStrikes; n++)
        {
            // OI pyramid: decays 35% per strike outward from ATM
            long baseOi     = (long)(200_000 * Math.Pow(0.65, n));
            long ceOi       = (long)(baseOi * (double)ceSkim);
            long peOi       = (long)(baseOi * (double)peSkim);
            long ceOiChange = (long)(baseOi * 0.08) * ceChangeSign;
            long peOiChange = (long)(baseOi * 0.08) * peChangeSign;
            long ceVol      = Math.Max(1_000L, baseOi / 20);
            long peVol      = Math.Max(1_000L, baseOi / 20);

            // IV skew: puts are more expensive OTM, calls cheaper OTM
            decimal ceIv    = Math.Max(5m, atmIv - 0.3m * n);
            decimal peIv    = Math.Max(5m, atmIv + 0.5m * n);

            // Delta approximation: ATM ≈ ±0.50, halves every strike
            decimal ceDelta =  (decimal)(0.50 * Math.Pow(0.55, n));
            decimal peDelta = -(decimal)(0.50 * Math.Pow(0.55, n));

            decimal ceStrike = atmStrike + strikeInterval * n;
            decimal peStrike = atmStrike - strikeInterval * n;

            if (n == 0)
            {
                // ATM: single strike, one CE + one PE
                legs.Add(new OptionLeg(atmStrike, "CE", 0m, ceOi, ceOiChange, ceVol, ceIv, 0m, 0m, ceDelta));
                legs.Add(new OptionLeg(atmStrike, "PE", 0m, peOi, peOiChange, peVol, peIv, 0m, 0m, peDelta));
            }
            else
            {
                // OTM CE above ATM
                legs.Add(new OptionLeg(ceStrike, "CE", 0m, ceOi, ceOiChange, ceVol, ceIv, 0m, 0m, ceDelta));
                // OTM PE below ATM
                legs.Add(new OptionLeg(peStrike, "PE", 0m, peOi, peOiChange, peVol, peIv, 0m, 0m, peDelta));
            }
        }

        return legs;
    }

    /// <summary>
    /// Serialises per-leg entry details for storage in BacktestTrade.LegsJson.
    /// Each element: { strike, type ("CE"/"PE"), direction ("BUY"/"SELL"), premium, expiry, brokerage }.
    /// </summary>
    private static string BuildLegsJson(OpenSpreadSim pos, decimal brokeragePerLeg)
    {
        var legs = pos.Legs.Select(l => new
        {
            strike    = l.Strike,
            type      = l.Leg.OptionType == OptionType.Call ? "CE" : "PE",
            direction = l.Leg.Direction  == OrderDirection.Sell ? "SELL" : "BUY",
            premium   = Math.Round(l.EntryLegPrice, 2),
            expiry    = l.LegExpiry.ToString(),
            brokerage = brokeragePerLeg,
        });
        return System.Text.Json.JsonSerializer.Serialize(legs);
    }

    /// <summary>
    /// Price the entire spread using Black-Scholes (sum of all leg prices with direction sign).
    /// Each leg uses its own LegExpiry for TTE so CalendarSpread near/far legs are priced correctly.
    /// Returns net cost-to-close: positive = net payment, negative = net receipt.
    /// P&amp;L = NetCredit − PriceSpreadSim(…) for all spread types (credit and debit).
    /// </summary>
    private static decimal PriceSpreadSim(
        OpenSpreadSim pos, decimal spot, LocalDate barDate, IBlackScholesEngine bs)
    {
        const double RFR = 0.065;
        decimal spreadValue = 0m;
        foreach (var (leg, strike, _, legExpiry) in pos.Legs)
        {
            // Use each leg's own expiry — critical for CalendarSpread (near ≠ far expiry).
            double tte = Math.Max(0.0, (legExpiry - barDate).Days / 365.0);
            var g = bs.Compute(spot, strike, tte, pos.EntryIvFraction, RFR,
                leg.OptionType == OptionType.Call);
            decimal legVal = Math.Max(0m, g.TheoreticalPrice);
            // To close: pay to buy back short legs, receive for selling long legs
            spreadValue += leg.Direction == OrderDirection.Sell ? legVal : -legVal;
        }
        return spreadValue;
    }

    /// <summary>
    /// Resolve the concrete strike for a spread leg using the selection mode.
    /// For ByDelta: iterative B-S search across ±30% of spot in strikeInterval steps.
    /// For ATM/OtmByStrike: arithmetic from ATM.
    /// </summary>
    internal static decimal ResolveStrike(
        SpreadLeg leg, decimal spot, double iv, double tte, double r,
        decimal strikeInterval, IBlackScholesEngine bs)
    {
        decimal atm = Math.Round(spot / strikeInterval) * strikeInterval;

        return leg.SelectionMode switch
        {
            StrikeSelectionMode.Atm => atm,

            StrikeSelectionMode.OtmByStrike => ComputeOtmStrike(leg, atm, strikeInterval),

            StrikeSelectionMode.ByDelta when leg.TargetDelta.HasValue =>
                FindDeltaStrike(leg, spot, iv, tte, r, strikeInterval, bs),

            _ => atm
        };
    }

    internal static decimal ComputeOtmStrike(SpreadLeg leg, decimal atm, decimal si)
    {
        int n   = leg.OtmStrikes ?? 1;
        bool isCallOtm = leg.OptionType == OptionType.Call;

        if (leg.FromStrike.HasValue)
            return leg.FromStrike.Value + (isCallOtm ? 1 : -1) * n * si;

        return atm + (isCallOtm ? 1 : -1) * n * si;
    }

    internal static decimal FindDeltaStrike(
        SpreadLeg leg, decimal spot, double iv, double tte, double r,
        decimal si, IBlackScholesEngine bs)
    {
        bool isCall          = leg.OptionType == OptionType.Call;
        decimal targetAbs    = Math.Abs(leg.TargetDelta!.Value);
        decimal bestStrike   = Math.Round(spot / si) * si;
        decimal bestDiff     = decimal.MaxValue;

        // Search ±30% of spot; search direction: calls go OTM above, puts OTM below
        for (decimal k = spot * 0.70m; k <= spot * 1.30m; k += si)
        {
            decimal rounded = Math.Round(k / si) * si;
            var g    = bs.Compute(spot, rounded, tte, iv, r, isCall);
            var diff = Math.Abs(Math.Abs(g.Delta) - targetAbs);
            if (diff < bestDiff) { bestDiff = diff; bestStrike = rounded; }
        }
        return bestStrike;
    }

    /// <summary>
    /// Compute nearest weekly F&amp;O expiry (Thursday) that is at least 1 day after 'from'.
    /// NSE weekly options for NIFTY/BANKNIFTY expire on Thursday.
    /// </summary>
    internal static LocalDate NearestWeeklyExpiry(LocalDate from)
    {
        var d = from.PlusDays(1);
        while (d.DayOfWeek != IsoDayOfWeek.Thursday)
            d = d.PlusDays(1);
        return d;
    }

    /// <summary>
    /// Compute nearest monthly F&amp;O expiry (last Thursday of the month following 'from').
    /// Used for CalendarSpread far-leg expiry.
    /// </summary>
    internal static LocalDate NearestMonthlyExpiry(LocalDate from)
    {
        // Last Thursday of the month after 'from'
        var nextMonth = from.PlusMonths(1);
        var firstOfFollowing = new LocalDate(nextMonth.Year, nextMonth.Month, 1).PlusMonths(1);
        var lastDay = firstOfFollowing.PlusDays(-1);
        while (lastDay.DayOfWeek != IsoDayOfWeek.Thursday)
            lastDay = lastDay.PlusDays(-1);
        return lastDay;
    }

    /// <summary>
    /// Extract spread simulation config from the strategy's ParametersJson.
    /// Reads MaxLossMultiple, ProfitTargetPct (0–100 → 0–1), StrikeInterval.
    /// For GenericRules strategies also reads nested optionsConfig.stopLossPct /
    /// optionsConfig.profitTargetPct when top-level keys are absent.
    /// Uses safe defaults when keys are absent or JSON is malformed.
    /// </summary>
    internal static (decimal MaxLossMultiple, decimal ProfitTargetPct, decimal StrikeInterval)
        ExtractSpreadConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (2.0m, 0.50m, 50m);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            decimal maxLoss = root.TryGetProperty("MaxLossMultiple", out var ml)
                ? ml.GetDecimal() : 2.0m;

            // ProfitTargetPct stored as fraction (0.50) in ShortStraddle, as pct (50) in CalendarSpread
            decimal profitRaw = root.TryGetProperty("ProfitTargetPct", out var pt)
                ? pt.GetDecimal()
                : root.TryGetProperty("VegaProfitTargetPct", out var vp)
                    ? vp.GetDecimal() : -1m;

            decimal si = root.TryGetProperty("StrikeInterval", out var sv)
                ? sv.GetDecimal() : 50m;

            // GenericRules: read from nested optionsConfig when top-level keys not found
            if (root.TryGetProperty("optionsConfig", out var oc))
            {
                if (maxLoss == 2.0m &&
                    oc.TryGetProperty("stopLossPct", out var slp) && slp.TryGetDecimal(out var slpVal) && slpVal > 0)
                    maxLoss = 100m / slpVal;   // e.g. 50% stop → 2.0× max loss

                if (profitRaw < 0 &&
                    oc.TryGetProperty("profitTargetPct", out var ptp) && ptp.TryGetDecimal(out var ptpVal) && ptpVal > 0)
                    profitRaw = ptpVal;        // stored as pct (0–100)
            }

            if (profitRaw < 0) profitRaw = 0.50m;

            // Normalise: values > 1 are treated as percentage
            decimal profit = profitRaw > 1m ? profitRaw / 100m : profitRaw;
            return (Math.Max(1m, maxLoss), Math.Clamp(profit, 0.05m, 1m), Math.Max(1m, si));
        }
        catch { return (2.0m, 0.50m, 50m); }
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

        // ExitTime: use candle.OpenTime as an intrabar fill estimate.
        // Using CloseTime would inflate all time-of-day analytics for SL/TP exits by one bar.
        // For gap-fill exits the fill IS at the open, so OpenTime is also the right choice.
        // END_OF_DATA force-closes use CloseTime separately (force-closed at bar close).
        return trade with
        {
            ExitPrice  = exitPrice,
            ExitTime   = candle.OpenTime,
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
        if (maxByCapital <= 0)
        {
            // Equity too low to afford even 1 share at 25% cap — stop trading, not sizeByRisk issue
            return 0;
        }
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

        // Sortino: downside deviation (negative returns only)
        var negReturns  = returns.Where(r => r < 0).ToArray();
        var downDev     = negReturns.Length > 0
            ? Math.Sqrt(negReturns.Select(r => r * r).Average())
            : stdDev;
        var sortino     = downDev == 0 ? 0m : (decimal)(avgReturn / downDev);

        var totalReturn = (finalEquity - request.InitialCapital) / request.InitialCapital;
        var calmar      = maxDrawdown == 0 ? 0 : totalReturn / maxDrawdown;

        // Daily Sharpe: annualise by √252 (252 trading days per year)
        var dailySharpe  = ComputeGroupedSharpe(trades,
            t => t.ExitTime.ToInstant().InZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"]).Date.ToString(),
            annualisationFactor: 252);
        // Monthly Sharpe: annualise by √12 (12 months per year, not √252)
        var monthlySharpe = ComputeGroupedSharpe(trades, t =>
        {
            var d = t.ExitTime.ToInstant().InZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"]).Date;
            return $"{d.Year:D4}-{d.Month:D2}";
        }, annualisationFactor: 12);

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

        // Deployment readiness rating: use dailySharpe (annualised daily P&L series × √252)
        // because per-trade sharpe is un-annualised and not comparable to industry thresholds.
        var (deployRating, deployRationale) = ComputeDeploymentRating(
            dailySharpe, maxDrawdown, winRate, trades.Count, sortino);

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
            SharpeRatio: dailySharpe,
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

    private static decimal ComputeGroupedSharpe(
        List<BacktestTrade> trades, Func<BacktestTrade, string> keySelector,
        double annualisationFactor = 252)
    {
        var groups = trades.GroupBy(keySelector)
            .Select(g => (double)g.Sum(t => t.NetPnl))
            .ToArray();
        if (groups.Length < 2) return 0m;
        var avg    = groups.Average();
        var stdDev = Math.Sqrt(groups.Select(r => Math.Pow(r - avg, 2)).Average());
        if (stdDev == 0) return 0m;
        return (decimal)(avg / stdDev * Math.Sqrt(annualisationFactor));
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
        try
        {
        // Stream directly into SHA256 — zero large-string allocations even for 200k+ candle runs.
        using var sha = SHA256.Create();
        using var cs  = new System.Security.Cryptography.CryptoStream(
            System.IO.Stream.Null, sha, System.Security.Cryptography.CryptoStreamMode.Write);

        void WriteString(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            cs.Write(bytes, 0, bytes.Length);
        }

        WriteString($"strategy={request.StrategyName};params={request.ParametersJson};");
        WriteString($"symbol={request.InternalSymbol};tf={request.Timeframe};");
        WriteString($"from={request.FromDate};to={request.ToDate};capital={request.InitialCapital};");

        // Write all OHLCV fields as binary (no per-candle string allocations).
        // Each decimal is serialised as its 4 raw int components (16 bytes, stackalloc).
        var buf8  = new byte[8];
        var buf16 = new byte[16];

        static void WriteDecimalTo(byte[] dest, decimal d, System.Security.Cryptography.CryptoStream stream)
        {
            var bits = decimal.GetBits(d);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(dest,       bits[0]);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(dest.AsSpan(4),  bits[1]);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(dest.AsSpan(8),  bits[2]);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(dest.AsSpan(12), bits[3]);
            stream.Write(dest, 0, 16);
        }

        foreach (var c in candles)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(buf8,
                c.OpenTime.ToInstant().ToUnixTimeMilliseconds());
            cs.Write(buf8, 0, 8);
            WriteDecimalTo(buf16, c.Open,   cs);
            WriteDecimalTo(buf16, c.High,   cs);
            WriteDecimalTo(buf16, c.Low,    cs);
            WriteDecimalTo(buf16, c.Close,  cs);
            WriteDecimalTo(buf16, c.Volume, cs);
        }

        cs.FlushFinalBlock();
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        }
        catch
        {
            // If any decimal serialisation throws (e.g. corrupt candle data), return a sentinel
            // rather than crashing the whole backtest. Hash mismatch will be visible in results.
            return "hash-error";
        }
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
