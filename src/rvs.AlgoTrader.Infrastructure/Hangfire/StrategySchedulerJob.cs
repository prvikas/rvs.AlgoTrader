using Hangfire;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Hangfire;

/// <summary>
/// Checks strategy schedule_json and starts/stops instances at their configured session times.
/// Runs every minute during market hours via Hangfire recurring job.
/// </summary>
public class StrategySchedulerJob(
    IStrategyInstanceRepository instanceRepo,
    IStrategyInstanceManager instanceManager,
    IClock clock,
    ILogger<StrategySchedulerJob> logger)
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var now = clock.NowIst();
        var nowLocal = now.LocalDateTime;

        // Only run during market hours Mon-Fri 9:00-15:35 IST
        if (nowLocal.DayOfWeek is IsoDayOfWeek.Saturday or IsoDayOfWeek.Sunday) return;
        if (nowLocal.TimeOfDay < new LocalTime(9, 0) || nowLocal.TimeOfDay > new LocalTime(15, 35)) return;

        var instances = await instanceRepo.GetAllAsync(ct);
        var scheduledInstances = instances.Where(i =>
            !string.IsNullOrEmpty(i.ScheduleJson) &&
            i.Status is StrategyStatus.Scheduled or StrategyStatus.Running or StrategyStatus.Paused);

        foreach (var instance in scheduledInstances)
        {
            try
            {
                var schedule = System.Text.Json.JsonSerializer.Deserialize<ScheduleConfig>(instance.ScheduleJson!);
                if (schedule == null) continue;

                var sessionStart = new LocalTime(schedule.StartHour, schedule.StartMinute);
                var sessionStop = new LocalTime(schedule.StopHour, schedule.StopMinute);
                var currentTime = nowLocal.TimeOfDay;

                // Start at session_start if not already running
                if (currentTime >= sessionStart && currentTime < sessionStop &&
                    instance.Status != StrategyStatus.Running)
                {
                    logger.LogInformation("[Scheduler] Starting {Name} at scheduled session start", instance.Name);
                    await instanceManager.StartAsync(instance.Id, ct);
                }
                // Stop at session_stop
                else if (currentTime >= sessionStop && instance.Status == StrategyStatus.Running)
                {
                    logger.LogInformation("[Scheduler] Stopping {Name} at scheduled session end", instance.Name);
                    await instanceManager.StopAsync(instance.Id, "SESSION_END", ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Scheduler] Error processing schedule for {Name}", instance.Name);
            }
        }
    }

    private class ScheduleConfig
    {
        public int StartHour { get; set; } = 9;
        public int StartMinute { get; set; } = 15;
        public int StopHour { get; set; } = 15;
        public int StopMinute { get; set; } = 25;
    }
}
