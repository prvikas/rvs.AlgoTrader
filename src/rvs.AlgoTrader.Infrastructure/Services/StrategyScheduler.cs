using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Determines whether a strategy instance should start, stay paused, skip, or resume
/// based on its schedule_json configuration, the current IST time, and the market calendar.
///
/// All scheduling logic operates in IST (Asia/Kolkata). No user-local timezone is used here.
/// Uses IClock exclusively — never accesses system clock directly (AP-001).
///
/// Decision matrix for EvaluateOnStartup:
/// ┌──────────────────────────────────────────────────────────────────────────────────┐
/// │ Prior Status │ Kill Switch │ Within Session │ auto_resume │ Action              │
/// ├──────────────────────────────────────────────────────────────────────────────────┤
/// │ RUNNING      │ YES (any)   │ any            │ any         │ PAUSE (AP-015)       │
/// │ RUNNING      │ no          │ YES            │ true        │ AUTO_RESUME          │
/// │ RUNNING      │ no          │ YES            │ false       │ PAUSE                │
/// │ RUNNING      │ no          │ AFTER start    │ SKIP        │ SKIP + MissedSession │
/// │ RUNNING      │ no          │ AFTER start    │ START_LATE  │ START_LATE           │
/// │ RUNNING      │ no          │ BEFORE start   │ any         │ SCHEDULED            │
/// │ RUNNING      │ no          │ after stop     │ any         │ NEXT_DAY             │
/// │ PAUSED       │ any         │ any            │ any         │ MANUALLY_PAUSED      │
/// │ SCHEDULED    │ any         │ any            │ any         │ SCHEDULED            │
/// └──────────────────────────────────────────────────────────────────────────────────┘
/// </summary>
public class StrategyScheduler(
    IMarketCalendarService calendar,
    IClock clock,
    ILogger<StrategyScheduler> logger) : IStrategyScheduler
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    // ── IsWithinScheduledSession ──────────────────────────────────────────────

    /// <summary>
    /// Returns true if the current IST time falls within the configured session window
    /// AND today is a valid trading day (not a weekend or market holiday).
    /// Called on every Hangfire tick during market hours.
    /// </summary>
    public bool IsWithinScheduledSession(ScheduleConfig config)
    {
        var istNow = clock.NowIst();
        return IsWithinSessionAt(config, istNow);
    }

    // ── EvaluateOnStartup ─────────────────────────────────────────────────────

    /// <summary>
    /// Determines the correct action for a strategy instance on cold restart.
    /// AP-015: kill_switch_was_active flag is read by caller — if active, all instances stay STOPPED.
    /// This method assumes kill switch is NOT active (caller must pre-filter).
    /// </summary>
    public ScheduleEvaluation EvaluateOnStartup(StrategyInstanceSnapshot instance)
    {
        logger.LogDebug("[StrategyScheduler] EvaluateOnStartup for {Name} (PriorStatus={Status})",
            instance.Name, instance.PriorStatus);

        // Manually paused instances NEVER auto-resume (regardless of any other condition)
        if (instance.PriorStatus == StrategyStatus.Paused)
            return new ScheduleEvaluation(ScheduleAction.ManuallyPaused,
                "Instance was manually paused before shutdown — requires manual start");

        // Non-running states restore as-is
        if (instance.PriorStatus is StrategyStatus.Scheduled or StrategyStatus.Draft
            or StrategyStatus.Stopped)
            return new ScheduleEvaluation(ScheduleAction.Scheduled,
                $"Restored to {instance.PriorStatus} state");

        // From here: prior status was RUNNING
        // No schedule configured → can't determine session window → pause (safe)
        if (instance.Schedule == null)
            return new ScheduleEvaluation(ScheduleAction.Pause,
                "No schedule configured — cannot determine session state; requires manual start");

        var config = instance.Schedule;
        var istNow = clock.NowIst();
        var todayIst = istNow.LocalDateTime;

        // Is today a scheduled trading day?
        if (!IsScheduledDay(config, todayIst))
        {
            return new ScheduleEvaluation(ScheduleAction.Scheduled,
                $"Today ({todayIst.DayOfWeek}) is not a scheduled trading day — waiting for next session");
        }

        // Is today a market holiday?
        if (!calendar.IsTradingDay(todayIst.Date))
        {
            return new ScheduleEvaluation(ScheduleAction.Scheduled,
                $"Today is a market holiday — waiting for next trading day");
        }

        var sessionStart = config.SessionStart;
        var sessionStop  = config.SessionStop;
        var currentTime  = todayIst.TimeOfDay;

        // Before session start → schedule for session open
        if (currentTime < sessionStart)
            return new ScheduleEvaluation(ScheduleAction.Scheduled,
                $"Before session start ({sessionStart}) — will start at session open");

        // Within session window (start ≤ now < stop)
        if (currentTime >= sessionStart && currentTime < sessionStop)
        {
            if (instance.AutoResumeOnRestart)
                return new ScheduleEvaluation(ScheduleAction.AutoResume,
                    $"Within scheduled session ({sessionStart}–{sessionStop} IST), auto_resume_on_restart=true");

            // auto_resume = false → pause and require manual start
            return new ScheduleEvaluation(ScheduleAction.Pause,
                $"Within scheduled session but auto_resume_on_restart=false — requires manual start");
        }

        // After session start but before stop — means we're past session_start but may have missed it
        // Actually handled above (within session). Reaching here means currentTime >= sessionStop.

        // After session stop → schedule for next eligible session
        return new ScheduleEvaluation(ScheduleAction.Scheduled,
            $"Session ended at {sessionStop} IST — scheduled for next eligible trading session");
    }

    // ── TimeUntilNextSession ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the duration until the next session start.
    /// Returns null if the schedule is invalid or no upcoming session can be found.
    /// Looks up to 14 days ahead (covers weekends + holidays).
    /// </summary>
    public Duration? TimeUntilNextSession(ScheduleConfig config)
    {
        var istNow = clock.NowIst();

        // Check today first, then up to 14 days ahead
        for (int daysAhead = 0; daysAhead <= 14; daysAhead++)
        {
            var candidateDate = istNow.LocalDateTime.Date.PlusDays(daysAhead);

            if (!IsScheduledDay(config, candidateDate.AtMidnight()))
                continue;

            if (!calendar.IsTradingDay(candidateDate))
                continue;

            var sessionStart = candidateDate.At(config.SessionStart);
            var sessionStartZoned = sessionStart.InZoneLeniently(Ist);
            var sessionStartInstant = sessionStartZoned.ToInstant();

            var nowInstant = clock.NowInstant();

            // Only valid if session start is in the future
            if (sessionStartInstant > nowInstant)
                return sessionStartInstant - nowInstant;
        }

        return null; // No session found in the next 14 days
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private bool IsWithinSessionAt(ScheduleConfig config, ZonedDateTime istNow)
    {
        var local = istNow.LocalDateTime;

        if (!IsScheduledDay(config, local))
            return false;

        if (!calendar.IsTradingDay(local.Date))
            return false;

        var currentTime = local.TimeOfDay;
        return currentTime >= config.SessionStart && currentTime < config.SessionStop;
    }

    private static bool IsScheduledDay(ScheduleConfig config, LocalDateTime localDt)
    {
        if (config.Days == null || config.Days.Length == 0)
            return false;

        var dayAbbrev = localDt.DayOfWeek switch
        {
            IsoDayOfWeek.Monday    => "MON",
            IsoDayOfWeek.Tuesday   => "TUE",
            IsoDayOfWeek.Wednesday => "WED",
            IsoDayOfWeek.Thursday  => "THU",
            IsoDayOfWeek.Friday    => "FRI",
            IsoDayOfWeek.Saturday  => "SAT",
            IsoDayOfWeek.Sunday    => "SUN",
            _ => ""
        };

        return config.Days.Contains(dayAbbrev, StringComparer.OrdinalIgnoreCase);
    }
}
