using NodaTime;

namespace rvs.AlgoTrader.Domain.Events;

public record SignalGenerated(
    Guid StrategyInstanceId, string StrategyName, string InternalSymbol, string Timeframe,
    string Signal, decimal? EntryPrice, decimal? StopLoss, decimal? TakeProfit,
    string Reason, string CorrelationId, ZonedDateTime OccurredAt);

public record StrategyAutoResumed(
    Guid StrategyInstanceId, string StrategyName, string ResumeReason,
    ZonedDateTime SessionStart, ZonedDateTime SessionStop,
    string CorrelationId, ZonedDateTime OccurredAt);

public record StrategyMissedSessionWindow(
    Guid StrategyInstanceId, string StrategyName,
    ZonedDateTime MissedSessionStart, ZonedDateTime MissedSessionStop,
    string MissedReason, string CorrelationId, ZonedDateTime OccurredAt);

public record ColdRestartPausedEvent(
    Guid StrategyInstanceId, string StrategyName, string PauseReason,
    ZonedDateTime ShutdownAt, string CorrelationId, ZonedDateTime OccurredAt);
