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

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Receives CandleClosedEvent and dispatches strategy evaluation per running instance.
/// Each instance evaluated sequentially within its own worker channel.
///
/// Routing:
///   StrategyMode.Live    → LiveExecutionEngine (real orders)
///   StrategyMode.Forward → ForwardTestEngine (paper trading, no real orders)
///   Other modes          → skipped
///
/// HOLD and SKIPPED signals → signal_journal only, no domain event.
/// BUY/SELL signals → SignalGenerated event + execution engine.
///
/// Multi-timeframe (#94):
///   Before calling EvaluateAsync, higher-TF candles (15m, 60m, 1d) are pre-fetched from
///   ICandleCache and attached to StrategyContext when the instance's primary TF is finer-grained.
///   Strategies access these via context.Candles15Min / Candles1Hour / CandlesDaily — no I/O
///   inside EvaluateAsync.
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
    ILogger<StrategyEvaluationQueue> logger) : IConsumer<CandleClosedEvent>
{
    // Per-instance channels to ensure sequential evaluation
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _semaphores = new();

    // Higher-TF bars to fetch — enough for most indicators (200-bar SMA needs 200 daily bars)
    private const int HigherTfBarCount = 200;

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
            // Only fetch a higher TF when the primary TF is finer-grained.
            var candles15Min = IsFinerThan(instance.Timeframe, Timeframes.FifteenMinute)
                ? await candleCache.GetAsync(instance.InternalSymbol, Timeframes.FifteenMinute, HigherTfBarCount, ct)
                : null;

            var candles1Hour = IsFinerThan(instance.Timeframe, Timeframes.SixtyMinute)
                ? await candleCache.GetAsync(instance.InternalSymbol, Timeframes.SixtyMinute, HigherTfBarCount, ct)
                : null;

            var candlesDaily = IsFinerThan(instance.Timeframe, Timeframes.Daily)
                ? await candleCache.GetAsync(instance.InternalSymbol, Timeframes.Daily, HigherTfBarCount, ct)
                : null;

            var strategy = strategyFactory.Create(instance.StrategyName, instance.ParametersJson);
            var strategyContext = new StrategyContext(
                instance.Id,
                instance.InternalSymbol,
                instance.Timeframe,
                candles,
                instance.ParametersJson ?? "{}",
                correlationId,
                OptionChain:  null,          // populated by IOptionChainService when wired
                Candles15Min: candles15Min,
                Candles1Hour: candles1Hour,
                CandlesDaily: candlesDaily);

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

            // Always journal signal
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

            // Only publish and execute on BUY/SELL
            if (!isActionable) return;

            await bus.Publish(new SignalGenerated(
                instance.Id, instance.StrategyName, instance.InternalSymbol,
                instance.Timeframe, result.Signal.ToString(),
                result.EntryPrice, result.StopLoss, result.TakeProfit,
                result.Reason ?? "", correlationId, clock.NowIst()), ct);

            // Route to the correct execution engine based on mode
            if (instance.Mode == StrategyMode.Live)
            {
                await executionEngine.ExecuteSignalAsync(instance, result, correlationId, ct);
            }
            else if (instance.Mode == StrategyMode.Forward)
            {
                // ForwardTestEngine handles fill simulation and trade persistence
                await forwardTestEngine.ProcessCandleAsync(instance, evt.ClosedCandle, ct);
            }
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
        // Use the Timeframes.All ordering: lower index = finer granularity
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
