using Hangfire;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Hangfire;

/// <summary>
/// Runs every 30 seconds during market hours (scheduled in HangfireJobSetup).
/// Evaluates drawdown thresholds against running strategy instances.
/// Fires Telegram alerts via INotificationService when thresholds are breached.
///
/// P&amp;L approach:
///   Unrealized P&amp;L  — read from open Position.UnrealizedPnl (pre-computed on each tick by LiveExecutionEngine)
///   Realized P&amp;L    — read from Position.RealizedPnl on positions closed today (ClosedAt >= today IST)
///   Combined today P&amp;L = sum(closed positions today RealizedPnl) + sum(open positions UnrealizedPnl)
///   Positions are linked to instances via Position.StrategyRunId → IStrategyRunRepository
///
/// Alert deduplication (CLAUDE.md Monitoring rule):
///   Key: "Alert:LastFired:drawdown:{instanceId}" in app_config (DB-backed + Redis cache)
///   Suppresses duplicate alerts within the same trading day.
/// </summary>
public class MonitoringAlertJob(
    IStrategyInstanceRepository instanceRepo,
    IPositionRepository positionRepo,
    IStrategyRunRepository runRepo,
    IAppConfigService configService,
    INotificationService notifications,
    IClock clock,
    ILogger<MonitoringAlertJob> logger)
{
    private const string MaxDailyDrawdownPctKey = "Monitoring:MaxDailyDrawdownPct";
    private const string AlertsEnabledKey = "Monitoring:AlertsEnabled";
    private const decimal DefaultMaxDailyDrawdownPct = 3.0m; // 3% daily drawdown default

    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var istNow = clock.NowIst();
        logger.LogDebug("[MonitoringAlert] Evaluating alert rules at {Time} IST", istNow.ToString("HH:mm:ss", null));

        try
        {
            await EvaluateDrawdownAsync(istNow, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[MonitoringAlert] Unhandled error during alert evaluation");
        }
    }

    private async Task EvaluateDrawdownAsync(ZonedDateTime istNow, CancellationToken ct)
    {
        // Short-circuit if alerts are globally disabled
        var alertsEnabled = await configService.GetAsync<bool>(AlertsEnabledKey, ct);
        if (alertsEnabled == false)
        {
            logger.LogDebug("[MonitoringAlert] Alerts disabled (Monitoring:AlertsEnabled=false) — skipping evaluation");
            return;
        }

        // Load configured max daily drawdown threshold from app_config (DB-backed + Redis, 60s TTL)
        var maxDrawdownPct = await configService.GetAsync<decimal?>(MaxDailyDrawdownPctKey, ct)
                             ?? DefaultMaxDailyDrawdownPct;

        var runningInstances = await instanceRepo.GetRunningAsync(ct);
        if (runningInstances.Count == 0) return;

        // Load all open positions once (cheaper than per-instance queries)
        var allOpenPositions = await positionRepo.GetOpenAsync(ct);

        var todayIst = istNow.Date; // LocalDate in IST

        foreach (var instance in runningInstances)
        {
            if (instance.AllocatedCapital <= 0) continue;

            // Get all strategy runs for this instance to link positions
            var runs = await runRepo.GetByInstanceAsync(instance.Id, ct);
            var runIds = runs.Select(r => r.Id).ToHashSet();

            // Open positions belonging to this instance
            var instanceOpenPositions = allOpenPositions
                .Where(p => p.StrategyRunId.HasValue && runIds.Contains(p.StrategyRunId!.Value))
                .ToList();

            // Unrealized P&L: pre-computed on Position by LiveExecutionEngine on each price tick
            var unrealizedPnl = instanceOpenPositions.Sum(p => p.UnrealizedPnl);

            // Realized P&L: positions closed today (IST calendar day) belonging to this instance's runs.
            var closedToday = await positionRepo.GetClosedTodayAsync(runIds, todayIst, ct);
            var realizedPnlToday = closedToday.Sum(p => p.RealizedPnl);
            var totalPnl = realizedPnlToday + unrealizedPnl;
            var openPositionCount = instanceOpenPositions.Count;

            var drawdownPct = totalPnl < 0
                ? Math.Abs(totalPnl) / instance.AllocatedCapital * 100m
                : 0m;

            logger.LogDebug(
                "[MonitoringAlert] Instance={Instance} OpenPos={Cnt} UnrealizedPnl={Unreal:F2} Drawdown={Dd:F2}%",
                instance.Name, openPositionCount, unrealizedPnl, drawdownPct);

            if (drawdownPct >= maxDrawdownPct)
            {
                await FireDrawdownAlertAsync(instance.Id, instance.Name, instance.InternalSymbol,
                    instance.AllocatedCapital, totalPnl, drawdownPct, maxDrawdownPct,
                    openPositionCount, istNow, ct);
            }
        }
    }

    private async Task FireDrawdownAlertAsync(
        Guid instanceId, string instanceName, string symbol,
        decimal allocatedCapital, decimal totalPnl,
        decimal drawdownPct, decimal thresholdPct,
        int openPositionCount, ZonedDateTime istNow, CancellationToken ct)
    {
        var alertKey = $"Alert:LastFired:drawdown:{instanceId}";
        var todayStr = istNow.Date.ToString();

        // Deduplication: suppress if already alerted today for this instance
        var lastAlertDate = await configService.GetAsync<string>(alertKey, ct);
        if (lastAlertDate == todayStr)
        {
            logger.LogDebug("[MonitoringAlert] Suppressing duplicate drawdown alert for {Instance} (already fired today)",
                instanceName);
            return;
        }

        logger.LogWarning(
            "[MonitoringAlert] DRAWDOWN BREACH Instance={Instance} Drawdown={Dd:F2}% Threshold={Th:F2}% TodayPnl=₹{Pnl:N2}",
            instanceName, drawdownPct, thresholdPct, totalPnl);

        var pnlSign = totalPnl >= 0 ? "+" : "";
        var message =
            $"🚨 *DRAWDOWN ALERT — {instanceName}*\n\n" +
            $"📊 Symbol: `{symbol}`\n" +
            $"💰 Today P&L: `{pnlSign}₹{totalPnl:N2}`\n" +
            $"📉 Drawdown: `{drawdownPct:F2}%` (limit: {thresholdPct:F2}%)\n" +
            $"🏦 Allocated Capital: `₹{allocatedCapital:N0}`\n" +
            $"📌 Open Positions: `{openPositionCount}`\n" +
            $"🕐 Time: `{istNow:HH:mm} IST`\n\n" +
            $"⚠️ Review and consider pausing this strategy.";

        await notifications.SendAsync("Telegram", "CRITICAL", message, ct);

        // Store dedup key — one alert per instance per trading day
        await configService.SetAsync(
            alertKey,
            todayStr,
            actor: "MonitoringAlertJob",
            correlationId: Guid.NewGuid().ToString(),
            ct);
    }
}
