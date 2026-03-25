using Hangfire;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using System.Text.Json;

namespace rvs.AlgoTrader.Infrastructure.Hangfire;

/// <summary>
/// Checks strategy schedule_json and starts/stops instances at their configured session times.
/// Runs every minute during market hours via Hangfire recurring job.
///
/// Uses IStrategyScheduler.IsWithinScheduledSession (IClock-based, never system clock).
/// schedule_json format: { "days":["MON","TUE",...], "session_start":"09:20", "session_stop":"15:10",
///                         "timezone":"Asia/Kolkata", "auto_resume_on_restart": true,
///                         "missed_session_behavior":"SKIP", "force_exit_on_session_end":true }
/// </summary>
public class StrategySchedulerJob(
    IStrategyInstanceRepository instanceRepo,
    IStrategyInstanceManager instanceManager,
    IStrategyScheduler scheduler,
    IClock clock,
    ILogger<StrategySchedulerJob> logger)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var nowIst = clock.NowIst();
        var local = nowIst.LocalDateTime;

        // Only run during market hours Mon-Fri 9:00-15:35 IST
        if (local.DayOfWeek is IsoDayOfWeek.Saturday or IsoDayOfWeek.Sunday) return;
        if (local.TimeOfDay < new LocalTime(9, 0) || local.TimeOfDay > new LocalTime(15, 35)) return;

        var instances = await instanceRepo.GetAllAsync(ct);

        foreach (var instance in instances.Where(i => !string.IsNullOrEmpty(i.ScheduleJson)
                 && i.Status is StrategyStatus.Scheduled or StrategyStatus.Running or StrategyStatus.Paused))
        {
            try
            {
                var config = ParseScheduleConfig(instance.ScheduleJson!);
                if (config == null) continue;

                var withinSession = scheduler.IsWithinScheduledSession(config);

                if (withinSession && instance.Status == StrategyStatus.Scheduled)
                {
                    logger.LogInformation("[Scheduler] Starting {Name} at session open", instance.Name);
                    await instanceManager.StartAsync(instance.Id, ct);
                }
                else if (!withinSession && instance.Status == StrategyStatus.Running && config.ForceExitOnSessionEnd)
                {
                    logger.LogInformation("[Scheduler] Stopping {Name} at session end (force_exit=true)", instance.Name);
                    await instanceManager.StopAsync(instance.Id, "SESSION_END", ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Scheduler] Error processing schedule for instance {Id}", instance.Id);
            }
        }
    }

    /// <summary>
    /// Parses the schedule_json blob into a ScheduleConfig domain record.
    /// Made public static so StartupOrchestrator can reuse it without duplicating logic.
    /// </summary>
    public static ScheduleConfig? ParseScheduleConfig(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string[] days = root.TryGetProperty("days", out var daysEl)
                ? daysEl.EnumerateArray().Select(d => d.GetString() ?? "").ToArray()
                : ["MON", "TUE", "WED", "THU", "FRI"];

            var startStr    = root.TryGetProperty("session_start", out var ss) ? ss.GetString() ?? "09:15" : "09:15";
            var stopStr     = root.TryGetProperty("session_stop",  out var se) ? se.GetString() ?? "15:25" : "15:25";
            var timezone    = root.TryGetProperty("timezone",      out var tz) ? tz.GetString() ?? "Asia/Kolkata" : "Asia/Kolkata";
            var autoResume  = root.TryGetProperty("auto_resume_on_restart",    out var ar) && ar.GetBoolean();
            var missedBehav = root.TryGetProperty("missed_session_behavior",    out var mb) ? mb.GetString() ?? "SKIP" : "SKIP";
            var forceExit   = root.TryGetProperty("force_exit_on_session_end", out var fe) && fe.GetBoolean();

            var start = ParseLocalTime(startStr);
            var stop  = ParseLocalTime(stopStr);
            if (start == null || stop == null) return null;

            return new ScheduleConfig(days, start.Value, stop.Value, timezone, autoResume, missedBehav, forceExit);
        }
        catch { return null; }
    }

    private static LocalTime? ParseLocalTime(string hhmm)
    {
        var parts = hhmm.Split(':');
        if (parts.Length == 2
            && int.TryParse(parts[0], out var h)
            && int.TryParse(parts[1], out var m))
            return new LocalTime(h, m);
        return null;
    }
}
