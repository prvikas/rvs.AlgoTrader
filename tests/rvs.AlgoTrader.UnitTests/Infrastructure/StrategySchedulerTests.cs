using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Infrastructure.Clock;
using rvs.AlgoTrader.Infrastructure.Services;
using Xunit;
using DomainClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.UnitTests.Infrastructure;

/// <summary>
/// Unit tests for StrategyScheduler.
///
/// Design principles:
/// - SimulatedClock controls time — never system clock (AP-001).
/// - IMarketCalendarService is mocked to isolate from the static holiday set.
/// - Tests cover all 8+ rows of the EvaluateOnStartup decision matrix.
/// - Monday 2024-06-03 at various times is used as the reference date:
///     09:19 IST = before session  |  09:30 IST = within session  |  15:30 IST = after session
/// </summary>
public class StrategySchedulerTests
{
    // ── Constants ──────────────────────────────────────────────────────────────

    // Reference date: Monday 2024-06-03 — a normal trading day (not a holiday)
    private const int Year  = 2024;
    private const int Month = 6;
    private const int Day   = 3;   // Monday

    // Standard 5-day schedule matching most instances
    private static readonly string[] WeekdayDays = ["MON", "TUE", "WED", "THU", "FRI"];

    private static readonly LocalTime SessionStart = new(9, 20, 0);
    private static readonly LocalTime SessionStop  = new(15, 10, 0);

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Builds the default schedule: Mon–Fri, 09:20–15:10 IST, SKIP missed sessions.</summary>
    private static ScheduleConfig DefaultSchedule(
        bool AutoResumeOnRestart         = true,
        string missedBehavior   = "SKIP",
        bool forceExit          = true,
        LocalTime? start        = null,
        LocalTime? stop         = null,
        string[]? days          = null) =>
        new ScheduleConfig(
            days ?? WeekdayDays,
            start ?? SessionStart,
            stop  ?? SessionStop,
            "Asia/Kolkata",
            AutoResumeOnRestart,
            missedBehavior,
            forceExit);

    /// <summary>Builds a snapshot of a RUNNING instance with a standard schedule.</summary>
    private static StrategyInstanceSnapshot RunningInstance(
        ScheduleConfig? schedule  = null,
        bool AutoResumeOnRestart           = true) =>
        new StrategyInstanceSnapshot(
            Guid.NewGuid(), "TestStrategy",
            StrategyStatus.Running,
            AutoResumeOnRestart,
            schedule ?? DefaultSchedule(AutoResumeOnRestart: AutoResumeOnRestart));

    /// <summary>Builds a mock IMarketCalendarService that reports every day as a trading day.</summary>
    private static IMarketCalendarService TradingDayCalendar()
    {
        var mock = new Mock<IMarketCalendarService>();
        mock.Setup(c => c.IsTradingDay(It.IsAny<LocalDate>())).Returns(true);
        return mock.Object;
    }

    /// <summary>Builds a mock IMarketCalendarService that reports every day as a holiday/non-trading day.</summary>
    private static IMarketCalendarService HolidayCalendar()
    {
        var mock = new Mock<IMarketCalendarService>();
        mock.Setup(c => c.IsTradingDay(It.IsAny<LocalDate>())).Returns(false);
        return mock.Object;
    }

    private static StrategyScheduler BuildScheduler(DomainClock clock, IMarketCalendarService? calendar = null) =>
        new StrategyScheduler(
            calendar ?? TradingDayCalendar(),
            clock,
            NullLogger<StrategyScheduler>.Instance);

    // ── EvaluateOnStartup — PAUSED prior status ────────────────────────────────

    [Fact]
    public void EvaluateOnStartup_WhenPriorStatusIsPaused_ReturnsManuallyPaused()
    {
        // Arrange: doesn't matter what time it is — Paused → ManuallyPaused unconditionally
        var clock = SimulatedClock.FromIst(Year, Month, Day, 10, 0);
        var scheduler = BuildScheduler(clock);

        var instance = new StrategyInstanceSnapshot(
            Guid.NewGuid(), "TestStrategy",
            StrategyStatus.Paused,          // ← was manually paused
            AutoResumeOnRestart: true,      // even with AutoResumeOnRestart=true, manually paused is sticky
            DefaultSchedule(AutoResumeOnRestart: true));

        // Act
        var result = scheduler.EvaluateOnStartup(instance);

        // Assert
        result.Action.Should().Be(ScheduleAction.ManuallyPaused);
        result.Reason.Should().NotBeNullOrEmpty();
    }

    // ── EvaluateOnStartup — non-RUNNING, non-PAUSED prior states ──────────────

    [Theory]
    [InlineData(StrategyStatus.Scheduled)]
    [InlineData(StrategyStatus.Draft)]
    [InlineData(StrategyStatus.Stopped)]
    public void EvaluateOnStartup_WhenPriorStatusIsNonRunning_ReturnsScheduled(StrategyStatus status)
    {
        var clock = SimulatedClock.FromIst(Year, Month, Day, 10, 0);
        var scheduler = BuildScheduler(clock);

        var instance = new StrategyInstanceSnapshot(
            Guid.NewGuid(), "TestStrategy",
            status,
            AutoResumeOnRestart: true,
            DefaultSchedule());

        var result = scheduler.EvaluateOnStartup(instance);

        result.Action.Should().Be(ScheduleAction.Scheduled);
    }

    // ── EvaluateOnStartup — RUNNING + within session ───────────────────────────

    [Fact]
    public void EvaluateOnStartup_WhenWithinSessionAndAutoResumeTrue_ReturnsAutoResume()
    {
        // Arrange: 09:30 IST — within session (09:20–15:10), Monday, trading day
        var clock = SimulatedClock.FromIst(Year, Month, Day, 9, 30);
        var scheduler = BuildScheduler(clock);
        var instance = RunningInstance(AutoResumeOnRestart: true);

        // Act
        var result = scheduler.EvaluateOnStartup(instance);

        // Assert
        result.Action.Should().Be(ScheduleAction.AutoResume,
            "instance was RUNNING, within session, auto_resume_on_restart=true → must auto-resume");
        result.Reason.Should().Contain("auto_resume_on_restart=true");
    }

    [Fact]
    public void EvaluateOnStartup_WhenWithinSessionAndAutoResumeFalse_ReturnsPause()
    {
        // Arrange: 10:00 IST — within session, but auto_resume=false (safer default)
        var clock = SimulatedClock.FromIst(Year, Month, Day, 10, 0);
        var scheduler = BuildScheduler(clock);
        var instance = RunningInstance(AutoResumeOnRestart: false);

        // Act
        var result = scheduler.EvaluateOnStartup(instance);

        // Assert
        result.Action.Should().Be(ScheduleAction.Pause,
            "instance was RUNNING, within session, but auto_resume_on_restart=false → must pause and require manual start");
        result.Reason.Should().Contain("auto_resume_on_restart=false");
    }

    // ── EvaluateOnStartup — RUNNING + before session start ────────────────────

    [Fact]
    public void EvaluateOnStartup_WhenBeforeSessionStart_ReturnsScheduled()
    {
        // Arrange: 08:00 IST — before session start of 09:20
        var clock = SimulatedClock.FromIst(Year, Month, Day, 8, 0);
        var scheduler = BuildScheduler(clock);
        var instance = RunningInstance(AutoResumeOnRestart: true);

        // Act
        var result = scheduler.EvaluateOnStartup(instance);

        // Assert
        result.Action.Should().Be(ScheduleAction.Scheduled,
            "before session start → scheduler will start the instance at session open");
        result.Reason.Should().Contain("Before session start");
    }

    [Fact]
    public void EvaluateOnStartup_WhenExactlyAtSessionStart_ReturnsAutoResume()
    {
        // Arrange: 09:20:00 IST — exactly at session start (start ≤ now < stop → within session)
        var clock = SimulatedClock.FromIst(Year, Month, Day, 9, 20);
        var scheduler = BuildScheduler(clock);
        var instance = RunningInstance(AutoResumeOnRestart: true);

        var result = scheduler.EvaluateOnStartup(instance);

        // The condition is currentTime >= sessionStart && currentTime < sessionStop
        result.Action.Should().Be(ScheduleAction.AutoResume);
    }

    // ── EvaluateOnStartup — RUNNING + after session end ───────────────────────

    [Fact]
    public void EvaluateOnStartup_WhenAfterSessionEnd_ReturnsScheduled()
    {
        // Arrange: 15:30 IST — after session stop of 15:10
        var clock = SimulatedClock.FromIst(Year, Month, Day, 15, 30);
        var scheduler = BuildScheduler(clock);
        var instance = RunningInstance(AutoResumeOnRestart: true);

        // Act
        var result = scheduler.EvaluateOnStartup(instance);

        // Assert
        result.Action.Should().Be(ScheduleAction.Scheduled,
            "session ended → schedule for next eligible trading session");
        result.Reason.Should().Contain("Session ended");
    }

    // ── EvaluateOnStartup — RUNNING + today is a market holiday ───────────────

    [Fact]
    public void EvaluateOnStartup_WhenTodayIsMarketHoliday_ReturnsScheduled()
    {
        // Arrange: normal weekday time but the calendar says it's a holiday
        var clock = SimulatedClock.FromIst(Year, Month, Day, 10, 0);
        var scheduler = BuildScheduler(clock, calendar: HolidayCalendar());
        var instance = RunningInstance(AutoResumeOnRestart: true);

        // Act
        var result = scheduler.EvaluateOnStartup(instance);

        // Assert
        result.Action.Should().Be(ScheduleAction.Scheduled,
            "market holiday → schedule for next trading day");
        result.Reason.Should().Contain("holiday");
    }

    // ── EvaluateOnStartup — RUNNING + today is a weekend (not in schedule Days)

    [Fact]
    public void EvaluateOnStartup_WhenTodayIsWeekend_ReturnsScheduled()
    {
        // Arrange: Sunday 2024-06-02 (day before our reference Monday)
        var clock = SimulatedClock.FromIst(2024, 6, 2, 10, 0); // Sunday
        var scheduler = BuildScheduler(clock); // calendar still says "trading day" — but day-of-week filter fires first
        var instance = RunningInstance(AutoResumeOnRestart: true);

        // Act
        var result = scheduler.EvaluateOnStartup(instance);

        // Assert: Sunday is not in ["MON"–"FRI"], so IsScheduledDay returns false
        result.Action.Should().Be(ScheduleAction.Scheduled,
            "weekend not in configured days → schedule for next weekday");
    }

    // ── EvaluateOnStartup — RUNNING + no schedule configured ──────────────────

    [Fact]
    public void EvaluateOnStartup_WhenNoScheduleConfigured_ReturnsPause()
    {
        // Arrange: no schedule_json → can't determine session window
        var clock = SimulatedClock.FromIst(Year, Month, Day, 10, 0);
        var scheduler = BuildScheduler(clock);

        var instance = new StrategyInstanceSnapshot(
            Guid.NewGuid(), "TestStrategy",
            StrategyStatus.Running,
            AutoResumeOnRestart: true,
            Schedule: null);           // ← no schedule

        // Act
        var result = scheduler.EvaluateOnStartup(instance);

        // Assert: safe default — pause and require manual start
        result.Action.Should().Be(ScheduleAction.Pause,
            "no schedule → cannot determine session state → safe default is Pause");
        result.Reason.Should().Contain("No schedule configured");
    }

    // ── IsWithinScheduledSession ───────────────────────────────────────────────

    [Fact]
    public void IsWithinScheduledSession_WhenTimeIsInsideWindow_ReturnsTrue()
    {
        // Arrange: 10:30 IST Monday — well within 09:20–15:10
        var clock = SimulatedClock.FromIst(Year, Month, Day, 10, 30);
        var scheduler = BuildScheduler(clock);

        var result = scheduler.IsWithinScheduledSession(DefaultSchedule());

        result.Should().BeTrue();
    }

    [Fact]
    public void IsWithinScheduledSession_WhenTimeIsBeforeSessionStart_ReturnsFalse()
    {
        // Arrange: 08:45 IST — before 09:20 session start
        var clock = SimulatedClock.FromIst(Year, Month, Day, 8, 45);
        var scheduler = BuildScheduler(clock);

        var result = scheduler.IsWithinScheduledSession(DefaultSchedule());

        result.Should().BeFalse();
    }

    [Fact]
    public void IsWithinScheduledSession_WhenTimeIsAfterSessionStop_ReturnsFalse()
    {
        // Arrange: 15:15 IST — after 15:10 session stop
        var clock = SimulatedClock.FromIst(Year, Month, Day, 15, 15);
        var scheduler = BuildScheduler(clock);

        var result = scheduler.IsWithinScheduledSession(DefaultSchedule());

        result.Should().BeFalse();
    }

    [Fact]
    public void IsWithinScheduledSession_WhenWeekend_ReturnsFalse()
    {
        // Arrange: Saturday 2024-06-01, 11:00 IST — SAT not in MON–FRI days
        var clock = SimulatedClock.FromIst(2024, 6, 1, 11, 0); // Saturday
        var scheduler = BuildScheduler(clock);

        var result = scheduler.IsWithinScheduledSession(DefaultSchedule());

        result.Should().BeFalse();
    }

    [Fact]
    public void IsWithinScheduledSession_WhenMarketHoliday_ReturnsFalse()
    {
        // Arrange: Monday 11:00 IST but calendar says it's a holiday
        var clock = SimulatedClock.FromIst(Year, Month, Day, 11, 0);
        var scheduler = BuildScheduler(clock, calendar: HolidayCalendar());

        var result = scheduler.IsWithinScheduledSession(DefaultSchedule());

        result.Should().BeFalse();
    }

    [Fact]
    public void IsWithinScheduledSession_WhenExactlyAtSessionStop_ReturnsFalse()
    {
        // Arrange: exactly 15:10 IST — the condition is currentTime < sessionStop (exclusive end)
        var clock = SimulatedClock.FromIst(Year, Month, Day, 15, 10);
        var scheduler = BuildScheduler(clock);

        var result = scheduler.IsWithinScheduledSession(DefaultSchedule());

        result.Should().BeFalse("session stop is exclusive — at exactly 15:10 the session has ended");
    }

    [Fact]
    public void IsWithinScheduledSession_WhenDayNotInConfiguredDays_ReturnsFalse()
    {
        // Arrange: Monday 10:00 IST but schedule only runs WED–FRI
        var clock = SimulatedClock.FromIst(Year, Month, Day, 10, 0);
        var scheduler = BuildScheduler(clock);
        var wedFriSchedule = DefaultSchedule(days: ["WED", "THU", "FRI"]);

        var result = scheduler.IsWithinScheduledSession(wedFriSchedule);

        result.Should().BeFalse("Monday is not in the configured days list");
    }

    // ── TimeUntilNextSession ───────────────────────────────────────────────────

    [Fact]
    public void TimeUntilNextSession_WhenBeforeSessionToday_ReturnsDurationToTodayStart()
    {
        // Arrange: 08:00 IST Monday — session starts at 09:20, so 80 minutes until next session
        var clock = SimulatedClock.FromIst(Year, Month, Day, 8, 0);
        var scheduler = BuildScheduler(clock);

        var result = scheduler.TimeUntilNextSession(DefaultSchedule());

        result.Should().NotBeNull();
        // Duration should be approximately 80 minutes (session starts at 09:20, currently 08:00)
        result!.Value.TotalMinutes.Should().BeApproximately(80, 1.0,
            "80 minutes from 08:00 to 09:20");
    }

    [Fact]
    public void TimeUntilNextSession_WhenAfterSessionTodayAndTomorrowIsWeekend_SkipsToMonday()
    {
        // Arrange: Friday 15:30 IST — session ended; next session is Monday
        // Friday 2024-06-07, after session
        var clock = SimulatedClock.FromIst(2024, 6, 7, 15, 30); // Friday
        var scheduler = BuildScheduler(clock);

        var result = scheduler.TimeUntilNextSession(DefaultSchedule());

        // Next session is Monday 2024-06-10 at 09:20 IST
        // Duration from Fri 15:30 to Mon 09:20: 2 days + ~17h 50min
        result.Should().NotBeNull("should find Monday as the next eligible session");
        result!.Value.TotalDays.Should().BeGreaterThan(2.0,
            "from Friday afternoon to Monday morning is more than 2 days");
    }

    [Fact]
    public void TimeUntilNextSession_WhenNoTradingDaysFound_ReturnsNull()
    {
        // Arrange: every day is a holiday → should return null after scanning 14 days
        var clock = SimulatedClock.FromIst(Year, Month, Day, 10, 0);
        var scheduler = BuildScheduler(clock, calendar: HolidayCalendar());

        var result = scheduler.TimeUntilNextSession(DefaultSchedule());

        result.Should().BeNull("no trading days in the next 14 days → returns null");
    }

    [Fact]
    public void TimeUntilNextSession_WhenCurrentlyWithinSession_ReturnsPositiveDuration()
    {
        // Arrange: 10:00 IST Monday — currently within session
        // Expect: duration to NEXT session (tomorrow or next eligible day)
        var clock = SimulatedClock.FromIst(Year, Month, Day, 10, 0);
        var scheduler = BuildScheduler(clock);

        var result = scheduler.TimeUntilNextSession(DefaultSchedule());

        // The loop starts from daysAhead=0, but 09:20 today has already passed → should find tomorrow
        result.Should().NotBeNull();
        result!.Value.TotalHours.Should().BeGreaterThan(0,
            "next session start is always in the future when called within session");
    }

    // ── Edge cases ─────────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateOnStartup_WhenRunningAndAutoResumeTrue_ButDayNotInSchedule_ReturnsScheduled()
    {
        // Arrange: Wednesday but schedule is MON/TUE/THU/FRI only (WED excluded)
        var clock = SimulatedClock.FromIst(2024, 6, 5, 10, 0); // Wednesday
        var scheduler = BuildScheduler(clock);
        var instance = RunningInstance(
            schedule: DefaultSchedule(AutoResumeOnRestart: true, days: ["MON", "TUE", "THU", "FRI"]),
            AutoResumeOnRestart: true);

        var result = scheduler.EvaluateOnStartup(instance);

        result.Action.Should().Be(ScheduleAction.Scheduled,
            "Wednesday is not in the schedule's days list → treat as outside session → Scheduled");
    }

    [Fact]
    public void EvaluateOnStartup_AutoResume_IsOverriddenByCallerKillSwitchLogic_NotByScheduler()
    {
        // This test documents the AP-015 contract:
        // The scheduler itself does NOT check kill switch — that is done by StartupOrchestrator Step 4/6.
        // Within the scheduler, if kill switch is active, StartupOrchestrator pre-filters and calls
        // SetPausedAsync without calling EvaluateOnStartup at all.
        // So a normal AutoResume result is correct here — the caller applies the kill switch check.

        var clock = SimulatedClock.FromIst(Year, Month, Day, 10, 0);
        var scheduler = BuildScheduler(clock);
        var instance = RunningInstance(AutoResumeOnRestart: true);

        var result = scheduler.EvaluateOnStartup(instance);

        // Scheduler does not know about kill switch — returns AutoResume if criteria met
        result.Action.Should().Be(ScheduleAction.AutoResume,
            "AP-015: kill switch check is StartupOrchestrator's responsibility, not StrategyScheduler's");
    }
}
