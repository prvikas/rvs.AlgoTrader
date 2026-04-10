using System.Collections.Concurrent;
using MassTransit;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Constants;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Events;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Receives CandleClosedEvent and dispatches strategy evaluation per running instance.
/// Each instance evaluated sequentially within its own worker channel.
///
/// Routing:
///   StrategyMode.Live    → LiveExecutionEngine (real orders via 8-gate sequence)
///   StrategyMode.Forward → ForwardTestEngine (paper trading — always called; owns its own
///                          signal evaluation, position state, and trade persistence)
///   Other modes          → skipped
///
/// Option chain pre-fetch (SEQ-1 / Rule #18):
///   IOptionChainService is called BEFORE building StrategyContext so strategies can read
///   OptionChain / NearExpiryChain / FarExpiryChain without any I/O inside EvaluateAsync.
///   Option strategies that require a chain return Skip when it is unavailable (degraded mode).
///
/// Forward mode routing (SEQ-2):
///   ForwardTestEngine.ProcessCandleAsync is always called for Forward instances, regardless
///   of whether the queue's own evaluation produced a BUY/SELL signal. ForwardTestEngine
///   fetches its own option chains and manages spread/single-leg position state independently.
///   The queue's evaluation for Forward mode is used only for signal journaling.
///
/// Multi-timeframe (#94):
///   Higher-TF candles (15m, 60m, 1d) are pre-fetched and attached to StrategyContext.
/// </summary>
public class StrategyEvaluationQueue(
    IStrategyInstanceRepository instanceRepo,
    ICandleCache candleCache,
    IStrategyFactory strategyFactory,
    ILiveExecutionEngine executionEngine,
    IForwardTestEngine forwardTestEngine,
    ISignalJournalRepository signalJournal,
    IPublishEndpoint bus,
    IClock clock,
    IMarketBreadthService breadthService,
    IOptionIvRankService ivRankService,
    IEventCalendarService eventCalendarService,
    // SEQ-1: Pre-fetches option chain snapshots before StrategyContext is built (Rule #18).
    // Registered as Scoped — each candle event gets a fresh service with its own in-process cache.
    IOptionChainService optionChainService,
    ILogger<StrategyEvaluationQueue> logger) : IConsumer<CandleClosedEvent>
{
    // Per-instance channels to ensure sequential evaluation
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _semaphores = new();

    // Higher-TF bars to fetch — enough for most indicators (200-bar SMA needs 200 daily bars)
    private const int HigherTfBarCount = 200;

    // Strategy types that require an option chain snapshot for EvaluateAsync.
    // Checked against StrategyInstance.StrategyType (not display Name).
    private static readonly HashSet<string> OptionStrategyTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "IronCondor", "ShortStraddleStrangle", "CalendarSpread",
        "FibOptionSpread", "IntradayPcrOptions", "VerticalSpread"
    };

    public async Task Consume(ConsumeContext<CandleClosedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        // Find all running instances watching this symbol + timeframe (Live + ForwardTest)
        var instances = await instanceRepo.GetRunningAsync(ct);
        var matching = instances.Where(i =>
            i.InternalSymbol == evt.InternalSymbol &&
            i.Timeframe == evt.Timeframe &&
            i.Mode is StrategyMode.Live or StrategyMode.Forward).ToList();

        var tasks = matching.Select(instance => EvaluateInstanceAsync(instance, evt, ct));
        await Task.WhenAll(tasks);
    }

    private async Task EvaluateInstanceAsync(StrategyInstance instance, CandleClosedEvent evt, CancellationToken ct)
    {
        var sem = _semaphores.GetOrAdd(instance.Id, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            // Get primary candle history from cache
            var candles = await candleCache.GetAsync(
                instance.InternalSymbol, instance.Timeframe, TradingDefaults.CandleCacheSize, ct);
            if (candles.Count < 2)
            {
                logger.LogDebug("[EvalQueue] Insufficient candle history for {Instance}", instance.Name);
                return;
            }

            var correlationId = evt.CorrelationId;

            // ── Pre-fetch higher-TF candles for multi-timeframe strategies (#94) ──
            var candles15Min = IsFinerThan(instance.Timeframe, Timeframes.FifteenMinute)
                ? await candleCache.GetAsync(instance.InternalSymbol, Timeframes.FifteenMinute, HigherTfBarCount, ct)
                : null;

            var candles1Hour = IsFinerThan(instance.Timeframe, Timeframes.SixtyMinute)
                ? await candleCache.GetAsync(instance.InternalSymbol, Timeframes.SixtyMinute, HigherTfBarCount, ct)
                : null;

            var candlesDaily = IsFinerThan(instance.Timeframe, Timeframes.Daily)
                ? await candleCache.GetAsync(instance.InternalSymbol, Timeframes.Daily, HigherTfBarCount, ct)
                : null;

            // ── Pre-fetch market-context for strategy filters ─────────────────
            var todayIst = clock.NowInstant()
                .InZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"])
                .Date;

            decimal? breadthPct200Sma = null;
            try
            {
                var breadthDto = await breadthService.GetLatestAsync(ct);
                breadthPct200Sma = breadthDto?.Pct200Sma;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "[EvalQueue] BreadthService unavailable for {Instance}", instance.Name);
            }

            IvRankSnapshot? symbolIvRank = null;
            try
            {
                symbolIvRank = await ivRankService.GetAsync(instance.InternalSymbol, ct);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "[EvalQueue] IvRankService unavailable for {Instance}", instance.Name);
            }

            var hasUpcomingEvent = false;
            try
            {
                hasUpcomingEvent = await eventCalendarService.HasHighImpactEventAsync(
                    todayIst, windowDays: 3, symbol: instance.InternalSymbol, ct: ct);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "[EvalQueue] EventCalendarService unavailable for {Instance}", instance.Name);
            }

            // ── SEQ-1: Pre-fetch option chain(s) for option strategies (Rule #18) ─
            // Fetched here so EvaluateAsync has zero I/O; strategies read context fields directly.
            OptionChainSnapshot? optionChain     = null;
            OptionChainSnapshot? nearExpiryChain = null;
            OptionChainSnapshot? farExpiryChain  = null;

            if (OptionStrategyTypes.Contains(instance.StrategyType))
            {
                try
                {
                    var nearExpiry = optionChainService.GetNearestWeeklyExpiry(instance.InternalSymbol);
                    nearExpiryChain = await optionChainService.GetSnapshotAsync(
                        instance.InternalSymbol, nearExpiry, ct);
                    optionChain = nearExpiryChain; // backward compat — strategies that use context.OptionChain

                    // CalendarSpread also needs the far (monthly) expiry chain for CS-1/CS-2
                    if (string.Equals(instance.StrategyType, "CalendarSpread", StringComparison.OrdinalIgnoreCase))
                    {
                        var farExpiry = optionChainService.GetNearestMonthlyExpiry(instance.InternalSymbol);
                        farExpiryChain = await optionChainService.GetSnapshotAsync(
                            instance.InternalSymbol, farExpiry, ct);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "[EvalQueue] OptionChainService unavailable for {Instance} — " +
                        "option strategies will return Skip", instance.Name);
                }
            }

            var strategy = strategyFactory.Create(instance.StrategyType, instance.ParametersJson);
            var strategyContext = new StrategyContext(
                instance.Id,
                instance.InternalSymbol,
                instance.Timeframe,
                candles,
                instance.ParametersJson ?? "{}",
                correlationId,
                OptionChain:       optionChain,
                Candles15Min:      candles15Min,
                Candles1Hour:      candles1Hour,
                CandlesDaily:      candlesDaily,
                BreadthPct200Sma:  breadthPct200Sma,
                SymbolIvRank:      symbolIvRank,
                HasUpcomingEvent:  hasUpcomingEvent,
                NearExpiryChain:   nearExpiryChain,
                FarExpiryChain:    farExpiryChain);

            SignalResult result;
            try
            {
                result = await strategy.EvaluateAsync(strategyContext, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[EvalQueue] Strategy evaluation failed for {Instance}", instance.Name);
                return;
            }

            // Always journal signal (for both Live and Forward modes)
            var isActionable = result.Signal is SignalType.Buy or SignalType.Sell;
            await signalJournal.AppendAsync(new SignalJournalEntry(
                0L,
                instance.Id,
                instance.InternalSymbol,
                clock.NowInstant(),
                instance.Timeframe,
                result.Signal.ToString(),
                result.EntryPrice,
                result.StopLoss,
                result.TakeProfit,
                result.Reason,
                result.DiagnosticsJson,
                isActionable,
                result.SkippedReason), ct);

            // ── SEQ-2: Forward mode — ForwardTestEngine owns position state and execution ─
            // Always call ProcessCandleAsync regardless of whether this evaluation produced
            // a BUY/SELL. ForwardTestEngine re-evaluates internally (with its own option chains),
            // manages open spread/single-leg state, and persists trades.
            // The queue's evaluation above serves only for signal journaling in Forward mode.
            if (instance.Mode == StrategyMode.Forward)
            {
                try
                {
                    await forwardTestEngine.ProcessCandleAsync(instance, evt.ClosedCandle, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[EvalQueue] ForwardTestEngine.ProcessCandleAsync failed for {Instance}",
                        instance.Name);
                }
                return;
            }

            // ── Live mode: only route on actionable signals ────────────────────
            if (!isActionable) return;

            await bus.Publish(new SignalGenerated(
                instance.Id, instance.StrategyType, instance.InternalSymbol,
                instance.Timeframe, result.Signal.ToString(),
                result.EntryPrice, result.StopLoss, result.TakeProfit,
                result.Reason ?? "", correlationId, clock.NowIst()), ct);

            await executionEngine.ExecuteSignalAsync(instance, result, correlationId, ct);
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Returns true when <paramref name="instanceTf"/> is finer-grained than <paramref name="higherTf"/>,
    /// meaning the higher-TF candles provide genuinely additional context.
    /// </summary>
    private static bool IsFinerThan(string instanceTf, string higherTf)
    {
        var all = Timeframes.All;
        var instanceIdx = IndexOf(all, instanceTf);
        var higherIdx   = IndexOf(all, higherTf);
        return instanceIdx >= 0 && higherIdx >= 0 && instanceIdx < higherIdx;
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == value) return i;
        return -1;
    }
}
