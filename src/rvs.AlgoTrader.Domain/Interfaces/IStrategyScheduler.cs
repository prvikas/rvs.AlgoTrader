using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Domain.Interfaces;

public interface IStrategyScheduler
{
    bool IsWithinScheduledSession(ScheduleConfig config);
    ScheduleEvaluation EvaluateOnStartup(StrategyInstanceSnapshot instance);
    NodaTime.Duration? TimeUntilNextSession(ScheduleConfig config);
}

public record ScheduleConfig(
    string[] Days,
    NodaTime.LocalTime SessionStart,
    NodaTime.LocalTime SessionStop,
    string Timezone,                        // Always "Asia/Kolkata"
    bool AutoResumeOnRestart,
    string MissedSessionBehavior,           // START_LATE | SKIP | ALERT_ONLY
    bool ForceExitOnSessionEnd
);

public record StrategyInstanceSnapshot(
    Guid Id,
    string Name,
    StrategyStatus PriorStatus,
    bool AutoResumeOnRestart,
    ScheduleConfig? Schedule
);

public record ScheduleEvaluation(
    ScheduleAction Action,
    string Reason
);
