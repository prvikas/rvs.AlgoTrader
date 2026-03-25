using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;

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
    IAuditService audit,
    IClock clock,
    ILogger<StrategyInstanceManager> logger) : IStrategyInstanceManager
{
    private readonly Dictionary<Guid, Guid> _activeRuns = new(); // instanceId → runId

    public async Task<Guid> StartAsync(Guid instanceId, CancellationToken ct)
    {
        var instance = await instanceRepo.GetByIdAsync(instanceId, ct)
            ?? throw new InvalidOperationException($"Strategy instance {instanceId} not found");

        if (instance.Status == StrategyStatus.Running)
            throw new InvalidOperationException($"Instance '{instance.Name}' is already running");

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
        instance.CurrentRunId = run.Id;
        instance.UpdatedAt = nowInstant;
        await instanceRepo.UpdateAsync(instance, ct);
        _activeRuns[instanceId] = run.Id;

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

        if (_activeRuns.TryGetValue(instanceId, out var runId))
        {
            var run = await runRepo.GetByIdAsync(runId, ct);
            if (run != null)
            {
                run.Status = StrategyRunStatus.Stopped;
                run.EndedAt = nowInstant;
                await runRepo.UpdateAsync(run, ct);
            }
            _activeRuns.Remove(instanceId);
        }

        instance.Status = StrategyStatus.Stopped;
        instance.CurrentRunId = null;
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
        var brokerName = "Zerodha";

        foreach (var inst in running)
        {
            var instrument = await instrumentRepo.GetBySymbolAsync(inst.InternalSymbol, ct);
            if (instrument != null)
            {
                brokerName = inst.BrokerName ?? brokerName;
                var token = instrument.GetBrokerToken(brokerName);
                if (token != null) symbols.Add(token);
            }
        }

        aggregator.UpdateSubscriptions(symbols, brokerName);
    }
}
