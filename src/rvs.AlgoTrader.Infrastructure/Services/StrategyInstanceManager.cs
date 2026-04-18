using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Constants;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Application.Options;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Manages lifecycle of strategy instances: start, pause, stop, resume.
/// Updates status in DB, adjusts candle subscriptions via CandleAggregatorService.
/// Tracks active StrategyRun IDs per instance.
/// </summary>
public class StrategyInstanceManager(
    IStrategyInstanceRepository instanceRepo,
    IStrategyRunRepository runRepo,
    CandleAggregatorService aggregator,
    IInstrumentRepository instrumentRepo,
    IForwardTestEngine forwardTestEngine,
    IAuditService audit,
    IClock clock,
    ILogger<StrategyInstanceManager> logger,
    IOptions<FeaturesOptions> featuresOptions) : IStrategyInstanceManager
{
    // ConcurrentDictionary — safe for concurrent StartAsync/StopAsync calls (#23)
    private readonly ConcurrentDictionary<Guid, Guid> _activeRuns = new(); // instanceId → runId

    public async Task<Guid> StartAsync(Guid instanceId, CancellationToken ct)
    {
        var instance = await instanceRepo.GetByIdAsync(instanceId, ct)
            ?? throw new InvalidOperationException($"Strategy instance {instanceId} not found");

        if (instance.Status == StrategyStatus.Running)
            throw new InvalidOperationException($"Instance '{instance.Name}' is already running");

        // Backtest-only mode: Forward and Live modes require a broker connection
        if (!featuresOptions.Value.BrokerRequired && instance.Mode != StrategyMode.Backtest)
            throw new InvalidOperationException(
                $"Cannot start '{instance.Name}' in {instance.Mode} mode: broker connection is disabled " +
                "(Features:BrokerRequired=false). Only Backtest mode is available.");

        var nowInstant = clock.NowInstant();

        // Create new StrategyRun
        var run = new StrategyRun
        {
            Id = Guid.NewGuid(),
            StrategyInstanceId = instanceId,
            BrokerName = instance.BrokerName,
            Mode = instance.Mode,
            Status = StrategyRunStatus.Running,
            StartedAt = nowInstant
        };
        await runRepo.AddAsync(run, ct);

        instance.Status = StrategyStatus.Running;
        if (instance.RuntimeState != null)
            instance.RuntimeState.CurrentRunId = run.Id;
        instance.UpdatedAt = nowInstant;
        await instanceRepo.UpdateAsync(instance, ct);
        _activeRuns.TryAdd(instanceId, run.Id);

        // For Forward mode: initialise the ForwardTestEngine session so candle ticks are processed
        if (instance.Mode == StrategyMode.Forward)
        {
            var capital = instance.AllocatedCapital > 0
                ? instance.AllocatedCapital
                : TradingDefaults.DefaultCapital;
            var sessionId = await forwardTestEngine.StartSessionAsync(instance, capital, ct);
            logger.LogInformation("[StrategyManager] ForwardTest session {SessionId} started for '{Name}'",
                sessionId, instance.Name);
        }

        // Update candle aggregator subscriptions
        await RefreshSubscriptionsAsync(ct);

        await audit.LogAsync("STRATEGY_STARTED", "System", "StrategyInstance", instanceId.ToString(),
            new { instance.Name, run.Id }, instanceId.ToString(), ct);

        logger.LogInformation("[StrategyManager] Started '{Name}' (Run: {RunId})", instance.Name, run.Id);
        return run.Id;
    }

    public async Task PauseAsync(Guid instanceId, string reason, CancellationToken ct)
    {
        var instance = await instanceRepo.GetByIdAsync(instanceId, ct)
            ?? throw new InvalidOperationException($"Strategy instance {instanceId} not found");

        instance.Status = StrategyStatus.Paused;
        instance.UpdatedAt = clock.NowInstant();
        await instanceRepo.UpdateAsync(instance, ct);

        await audit.LogAsync("STRATEGY_PAUSED", "System", "StrategyInstance", instanceId.ToString(),
            new { instance.Name, reason }, instanceId.ToString(), ct);

        logger.LogInformation("[StrategyManager] Paused '{Name}': {Reason}", instance.Name, reason);
    }

    public async Task StopAsync(Guid instanceId, string reason, CancellationToken ct)
    {
        var instance = await instanceRepo.GetByIdAsync(instanceId, ct)
            ?? throw new InvalidOperationException($"Strategy instance {instanceId} not found");

        var nowInstant = clock.NowInstant();

        if (_activeRuns.TryRemove(instanceId, out var runId))
        {
            var run = await runRepo.GetByIdAsync(runId, ct);
            if (run != null)
            {
                run.Status = StrategyRunStatus.Stopped;
                run.EndedAt = nowInstant;
                await runRepo.UpdateAsync(run, ct);
            }
        }

        // For Forward mode: finalise the session (compute WinRate, FinalPnl, mark Stopped)
        if (instance.Mode == StrategyMode.Forward)
        {
            await forwardTestEngine.StopSessionAsync(instanceId, ct);
        }

        instance.Status = StrategyStatus.Stopped;
        if (instance.RuntimeState != null)
            instance.RuntimeState.CurrentRunId = null;
        instance.UpdatedAt = nowInstant;
        await instanceRepo.UpdateAsync(instance, ct);

        await audit.LogAsync("STRATEGY_STOPPED", "System", "StrategyInstance", instanceId.ToString(),
            new { instance.Name, reason }, instanceId.ToString(), ct);

        logger.LogInformation("[StrategyManager] Stopped '{Name}': {Reason}", instance.Name, reason);
    }

    public async Task ResumeAsync(Guid instanceId, CancellationToken ct)
        => await StartAsync(instanceId, ct);

    private async Task RefreshSubscriptionsAsync(CancellationToken ct)
    {
        var running = await instanceRepo.GetRunningAsync(ct);
        var symbols = new HashSet<string>();
        var brokerName = "MStock"; // primary broker; overridden below if instances specify another

        foreach (var inst in running)
        {
            if (string.IsNullOrWhiteSpace(inst.InternalSymbol))
            {
                logger.LogWarning("[StrategyManager] Instance '{Name}' has no InternalSymbol — skipping subscription", inst.Name);
                continue;
            }

            var instrument = await instrumentRepo.GetBySymbolAsync(inst.InternalSymbol, ct);
            if (instrument == null)
            {
                logger.LogWarning("[StrategyManager] Instrument not found for symbol '{Symbol}' (instance '{Name}')",
                    inst.InternalSymbol, inst.Name);
                continue;
            }

            var instBroker = inst.BrokerName ?? brokerName;
            brokerName = instBroker; // last running instance wins; single-broker model
            var token = instrument.GetBrokerToken(instBroker);
            if (token != null)
                symbols.Add(token);
            else
                logger.LogWarning("[StrategyManager] No broker token for {Broker}:{Symbol} (instance '{Name}')",
                    instBroker, inst.InternalSymbol, inst.Name);
        }

        aggregator.UpdateSubscriptions(symbols, brokerName);
        logger.LogInformation("[StrategyManager] Aggregator subscriptions updated: {Count} symbol(s) via {Broker}",
            symbols.Count, brokerName);
    }
}
