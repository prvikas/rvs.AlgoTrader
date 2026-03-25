namespace rvs.AlgoTrader.Application.DTOs.Strategy;

public record StrategyInstanceDto(
    Guid Id, string Name, string StrategyType, string InternalSymbol, string Timeframe,
    string Status, string Mode, string? BrokerName, decimal AllocatedCapital,
    bool AutoResumeOnRestart, string? ScheduleJson, string? ParametersJson,
    string? FailureBehaviorJson, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    // Today's realised P&L for this instance (net of brokerage + taxes).
    decimal TodayRealizedPnl = 0m,
    // Current mark-to-market unrealised P&L on open positions.
    decimal TodayUnrealizedPnl = 0m,
    // Number of currently open positions held by this instance.
    int OpenPositionCount = 0);

public record CreateStrategyInstanceDto(
    string Name, string StrategyType, Guid WatchlistId, string Mode,
    string? BrokerName, PriceActionBreakoutConfigDto ConfigJson,
    FailureBehaviorConfigDto? FailureBehaviorJson, Guid? RiskProfileId,
    ScheduleConfigDto? ScheduleJson);

public record UpdateStrategyInstanceDto(
    string? Name, string? BrokerName,
    PriceActionBreakoutConfigDto? ConfigJson,
    FailureBehaviorConfigDto? FailureBehaviorJson,
    Guid? RiskProfileId, ScheduleConfigDto? ScheduleJson);

public record PriceActionBreakoutConfigDto(
    string Timeframe, int SwingHighLookback, int VolumeSmaLookback,
    decimal VolumeMultiplier, bool UseEmaFilter, int EmaLength,
    decimal MinBodyRangePct, decimal SlBufferPct, decimal RiskRewardRatio,
    decimal TrailingSLActivationPct, decimal TrailingTPStep, int EvaluationTimeoutMs);

public record ScheduleConfigDto(
    string[] Days, TimeOnly SessionStart, TimeOnly SessionStop,
    string Timezone, bool AutoResumeOnRestart,
    string MissedSessionBehavior, bool ForceExitOnSessionEnd);

public record FailureBehaviorConfigDto(
    string OnBrokerCircuitOpen, string OnStreamDisconnect, string OnDataStale,
    int DataStalenessThresholdMinutes, string OnRiskLimitBreached,
    string OnEvaluationTimeout, KillSwitchBehaviorDto KillSwitch);

public record KillSwitchBehaviorDto(bool SquareOff);
