using System.Collections.Concurrent;
using MassTransit;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Events;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Receives CandleClosedEvent and dispatches strategy evaluation per running instance.
/// Each instance evaluated sequentially within its own worker channel.
/// HOLD and SKIPPED signals → signal_journal only, no domain event.
/// BUY/SELL signals → SignalGenerated event + LiveExecutionEngine.
/// </summary>
public class StrategyEvaluationQueue(
    IStrategyInstanceRepository instanceRepo,
    ICandleCache candleCache,
    IStrategyFactory strategyFactory,
    ILiveExecutionEngine executionEngine,
    ISignalJournalRepository signalJournal,
    IPublishEndpoint bus,
    IClock clock,
    ILogger<StrategyEvaluationQueue> logger) : IConsumer<CandleClosedEvent>
{
    // Per-instance channels to ensure sequential evaluation
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _semaphores = new();

    public async Task Consume(ConsumeContext<CandleClosedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        // Find all running instances watching this symbol + timeframe
        var instances = await instanceRepo.GetRunningAsync(ct);
        var matching = instances.Where(i =>
            i.InternalSymbol == evt.InternalSymbol &&
            i.Timeframe == evt.Timeframe &&
            i.Mode == StrategyMode.Live).ToList();

        var tasks = matching.Select(instance => EvaluateInstanceAsync(instance, evt, ct));
        await Task.WhenAll(tasks);
    }

    private async Task EvaluateInstanceAsync(StrategyInstance instance, CandleClosedEvent evt, CancellationToken ct)
    {
        var sem = _semaphores.GetOrAdd(instance.Id, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            // Get candle history from cache (already ClosedCandle value objects)
            var candles = await candleCache.GetAsync(instance.InternalSymbol, instance.Timeframe, 500, ct);
            if (candles.Count < 2)
            {
                logger.LogDebug("[EvalQueue] Insufficient candle history for {Instance}", instance.Name);
                return;
            }

            var correlationId = evt.CorrelationId;

            var strategy = strategyFactory.Create(instance.StrategyName, instance.ParametersJson);
            var strategyContext = new StrategyContext(
                instance.Id,
                instance.InternalSymbol,
                instance.Timeframe,
                candles,
                instance.ParametersJson ?? "{}",
                correlationId);

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
            await signalJournal.AppendAsync(new SignalJournalEntry(
                0L,
                instance.Id,
                instance.InternalSymbol,
                clock.NowInstant(),
                instance.Timeframe,
                result.Signal,
                result.EntryPrice,
                result.StopLoss,
                result.TakeProfit,
                result.Reason,
                result.DiagnosticsJson,
                result.Signal is "BUY" or "SELL",
                result.SkippedReason), ct);

            // Only publish and execute on BUY/SELL
            if (result.Signal is not ("BUY" or "SELL")) return;

            await bus.Publish(new SignalGenerated(
                instance.Id, instance.StrategyName, instance.InternalSymbol,
                instance.Timeframe, result.Signal,
                result.EntryPrice, result.StopLoss, result.TakeProfit,
                result.Reason ?? "", correlationId, clock.NowIst()), ct);

            await executionEngine.ExecuteSignalAsync(instance, result, correlationId, ct);
        }
        finally
        {
            sem.Release();
        }
    }
}
