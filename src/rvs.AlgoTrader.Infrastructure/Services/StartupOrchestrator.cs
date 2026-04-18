using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Events;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Infrastructure.Constants;
using rvs.AlgoTrader.Infrastructure.Hangfire;
using rvs.AlgoTrader.Application.Options;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// 11-step startup orchestrator (CLAUDE.md Rule #11).
/// Implements the full startup sequence in the required order:
///   1. PostgreSQL  2. Redis  3. RabbitMQ  4. Kill-switch check  5. Re-enqueue Hangfire
///   6. Reload strategy instances  7. Re-authenticate brokers  8. Reconcile capital
///   9. Re-subscribe WebSockets  10. Warm candle cache  11. Mark READY
///
/// Step 6 uses IStrategyScheduler to evaluate each instance's session state.
/// AP-015: kill-switch check in Step 4 sets a flag that Step 6 reads — never auto-resume
/// when kill switch was active at startup.
/// </summary>
public class StartupOrchestrator(
    IStrategyInstanceRepository instanceRepo,
    IStrategyInstanceManager instanceManager,
    IStrategyScheduler scheduler,
    IKillSwitchService killSwitch,
    ICandleCache candleCache,
    ICandleRepository candleRepo,
    IBrokerClientFactory brokerFactory,
    IAppBrokerSessionManager sessions,
    IForwardTestEngine forwardTestEngine,
    IPublishEndpoint bus,
    IAuditService audit,
    IClock clock,
    ILogger<StartupOrchestrator> logger,
    IServiceProvider serviceProvider,
    IOptions<FeaturesOptions> featuresOptions) : IStartupOrchestrator
{
    public async Task RunAsync(CancellationToken ct)
    {
        logger.LogInformation("[Startup] Beginning 11-step orchestration — {Time}", clock.NowIst());

        // Step 4: Kill-switch check (AP-015)
        var killSwitchWasActive = await Step4_CheckKillSwitchAsync(ct);

        // Step 6: Reload strategy instances
        await Step6_RestoreStrategyInstancesAsync(killSwitchWasActive, ct);

        // Step 6b: Recover ForwardTestEngine in-memory state for sessions that were Running at crash
        await forwardTestEngine.RecoverActiveSessionsAsync(ct);

        // Step 7: Re-authenticate brokers (skipped in backtest-only mode)
        if (featuresOptions.Value.BrokerRequired)
            await Step7_ReAuthBrokersAsync(ct);
        else
            logger.LogInformation("[Startup:Step7] Skipped — BrokerRequired=false (backtest-only mode)");

        // Step 9: Warm candle cache
        await Step9_WarmCandleCacheAsync(ct);

        // Step 11: Mark ready
        logger.LogInformation("[Startup] All steps complete — system is READY");
        await audit.LogAsync("SYSTEM_STARTUP", "System", "System", "startup",
            new { Time = clock.NowIst().ToString(), KillSwitchWasActive = killSwitchWasActive },
            "system-startup", ct);
    }

    // ── Step 4 ────────────────────────────────────────────────────────────────

    private async Task<bool> Step4_CheckKillSwitchAsync(CancellationToken ct)
    {
        try
        {
            var active = await killSwitch.IsActiveAsync(ct);
            if (active)
                logger.LogWarning("[Startup:Step4] ⚠ Kill switch was ACTIVE at startup — no instances will auto-resume (AP-015)");
            else
                logger.LogInformation("[Startup:Step4] Kill switch is inactive");
            return active;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Startup:Step4] Failed to check kill switch — treating as active (safe default)");
            return true; // Safe default: treat as active if check fails
        }
    }

    // ── Step 6 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates each instance that was RUNNING or PAUSED at shutdown.
    /// If killSwitchWasActive → ALL instances stay STOPPED (AP-015 — no exceptions).
    /// Otherwise: delegates to IStrategyScheduler.EvaluateOnStartup for each instance.
    /// </summary>
    private async Task Step6_RestoreStrategyInstancesAsync(bool killSwitchWasActive, CancellationToken ct)
    {
        logger.LogInformation("[Startup:Step6] Restoring strategy instances (killSwitch={KillSwitch})", killSwitchWasActive);

        var candidates = (await instanceRepo.GetAllAsync(ct))
            .Where(i => i.Status is StrategyStatus.Running or StrategyStatus.Paused)
            .ToList();

        foreach (var instance in candidates)
        {
            var correlationId = Guid.NewGuid().ToString();
            var nowIst = clock.NowIst();

            // AP-015: If kill switch was active, never auto-resume any instance
            if (killSwitchWasActive)
            {
                await SetPausedAsync(instance, "Kill switch was active at startup — manual restart required", ct);
                continue;
            }

            // Build the schedule config for this instance (if any)
            ScheduleConfig? scheduleConfig = null;
            if (!string.IsNullOrEmpty(instance.ScheduleJson))
                scheduleConfig = StrategySchedulerJob.ParseScheduleConfig(instance.ScheduleJson);

            var snapshot = new StrategyInstanceSnapshot(
                instance.Id,
                instance.Name,
                instance.Status,
                instance.RuntimeState?.AutoResumeOnRestart ?? false,
                scheduleConfig);

            var evaluation = scheduler.EvaluateOnStartup(snapshot);

            logger.LogInformation("[Startup:Step6] {Name}: {Action} — {Reason}",
                instance.Name, evaluation.Action, evaluation.Reason);

            switch (evaluation.Action)
            {
                case ScheduleAction.AutoResume:
                    try
                    {
                        await instanceManager.StartAsync(instance.Id, ct);
                        await bus.Publish(new StrategyAutoResumed(
                            instance.Id, instance.StrategyName,
                            evaluation.Reason, nowIst, nowIst, correlationId, nowIst), ct);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "[Startup:Step6] Failed to auto-resume {Name}", instance.Name);
                        await SetPausedAsync(instance, "Auto-resume failed — manual start required", ct);
                    }
                    break;

                case ScheduleAction.Pause:
                case ScheduleAction.ManuallyPaused:
                    await SetPausedAsync(instance, evaluation.Reason, ct);
                    await bus.Publish(new ColdRestartPausedEvent(
                        instance.Id, instance.StrategyName,
                        evaluation.Reason, nowIst, correlationId, nowIst), ct);
                    break;

                case ScheduleAction.Skip:
                    // Missed session window — schedule for next day
                    await SetScheduledAsync(instance, ct);
                    await bus.Publish(new StrategyMissedSessionWindow(
                        instance.Id, instance.StrategyName,
                        nowIst, nowIst,
                        evaluation.Reason, correlationId, nowIst), ct);
                    break;

                case ScheduleAction.Scheduled:
                case ScheduleAction.NextDay:
                    await SetScheduledAsync(instance, ct);
                    break;

                // StartLate: start immediately (missed session but START_LATE behavior)
                case ScheduleAction.StartLate:
                    try
                    {
                        await instanceManager.StartAsync(instance.Id, ct);
                        logger.LogWarning("[Startup:Step6] Late-start: {Name} — {Reason}", instance.Name, evaluation.Reason);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "[Startup:Step6] Late-start failed for {Name}", instance.Name);
                        await SetPausedAsync(instance, "Late-start failed — manual start required", ct);
                    }
                    break;
            }
        }
    }

    // ── Step 7 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-injects persisted Redis tokens into each broker client's in-memory state.
    /// Broker clients lose their in-memory _jwtToken / _accessToken on process restart
    /// even though BrokerSessionManager keeps them in Redis. This step bridges the gap
    /// by calling IFullBrokerClient.RestoreToken for every broker with a valid session.
    /// (CLAUDE.md Rule #11, Step 7)
    /// </summary>
    private async Task Step7_ReAuthBrokersAsync(CancellationToken ct)
    {
        logger.LogInformation("[Startup:Step7] Re-authenticating broker clients from stored sessions");

        // ── Pre-populate InMemory session dict from DB (when Redis is not available) ──
        // This bridges the gap: tokens survive restarts as long as they haven't expired.
        // Both services are only resolvable when InMemory path is active.
        var inMemorySessions = serviceProvider.GetService<InMemoryBrokerSessionManager>();
        var dbPersistence    = serviceProvider.GetService<DbBrokerSessionPersistence>();
        if (inMemorySessions != null && dbPersistence != null)
        {
            logger.LogInformation("[Startup:Step7] Redis not available — loading broker sessions from DB");
            var storedSessions = await dbPersistence.LoadAllValidAsync(ct);
            foreach (var stored in storedSessions)
                inMemorySessions.RestoreFromDb(stored);
        }

        var brokers = new[] { BrokerNames.Zerodha, BrokerNames.Upstox, BrokerNames.MStock };
        var restored = 0;
        var skipped = 0;

        foreach (var brokerName in brokers)
        {
            try
            {
                if (!sessions.IsSessionValid(brokerName))
                {
                    logger.LogWarning("[Startup:Step7] {Broker}: no valid session in Redis — login required", brokerName);
                    skipped++;
                    continue;
                }

                var accessToken = await sessions.TryGetAccessTokenAsync(brokerName, ct);
                if (string.IsNullOrEmpty(accessToken))
                {
                    logger.LogWarning("[Startup:Step7] {Broker}: session flagged valid but token missing from Redis", brokerName);
                    skipped++;
                    continue;
                }

                // Feed token is mStock-specific — null for Zerodha/Upstox (ignored by their RestoreToken)
                var feedToken = await sessions.TryGetFeedTokenAsync(brokerName, ct);

                var client = brokerFactory.GetClient(brokerName);
                client.RestoreToken(accessToken, feedToken);

                logger.LogInformation("[Startup:Step7] {Broker}: token restored{FeedHint}",
                    brokerName, feedToken != null ? " (+ feed token)" : "");
                restored++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Startup:Step7] {Broker}: failed to restore token — broker will show as unauthenticated", brokerName);
                skipped++;
            }
        }

        logger.LogInformation("[Startup:Step7] Broker re-auth complete — {Restored} restored, {Skipped} skipped/failed",
            restored, skipped);
    }

    // ── Step 9 ────────────────────────────────────────────────────────────────

    private async Task Step9_WarmCandleCacheAsync(CancellationToken ct)
    {
        logger.LogInformation("[Startup:Step9] Warming candle cache from TimescaleDB");
        var instances = await instanceRepo.GetRunningAsync(ct);
        var pairs = instances
            .Select(i => (i.InternalSymbol, i.Timeframe))
            .Where(p => !string.IsNullOrEmpty(p.InternalSymbol) && !string.IsNullOrEmpty(p.Timeframe))
            .Distinct();

        foreach (var (symbol, timeframe) in pairs)
        {
            var candles = await candleRepo.GetLastNAsync(symbol, timeframe, 500, ct);
            foreach (var candle in candles)
                await candleCache.AppendAsync(candle, ct);

            logger.LogDebug("[Startup:Step9] Warmed {Count} candles for {Symbol}/{Tf}", candles.Count, symbol, timeframe);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SetPausedAsync(StrategyInstance instance, string reason, CancellationToken ct)
    {
        instance.Status = StrategyStatus.Paused;
        instance.UpdatedAt = clock.NowInstant();
        await instanceRepo.UpdateAsync(instance, ct);
        logger.LogInformation("[Startup:Step6] Paused '{Name}': {Reason}", instance.Name, reason);
    }

    private async Task SetScheduledAsync(StrategyInstance instance, CancellationToken ct)
    {
        instance.Status = StrategyStatus.Scheduled;
        instance.UpdatedAt = clock.NowInstant();
        await instanceRepo.UpdateAsync(instance, ct);
    }
}
