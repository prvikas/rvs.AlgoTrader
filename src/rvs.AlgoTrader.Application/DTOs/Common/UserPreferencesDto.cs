namespace rvs.AlgoTrader.Application.DTOs.Common;

public record UserPreferencesDto(
    Guid UserId, string Timezone,
    bool NotifyOnOrderFill, bool NotifyOnSLHit, bool NotifyOnTpHit,
    bool NotifyOnKillSwitch, bool NotifyOnStreamReconnect, bool NotifyOnTokenExpiry,
    bool NotifyOnDataQuality, bool NotifyOnColdRestartPause, bool NotifyOnMonitoringBreach,
    bool NotifyOnStrategyAutoResumed, bool NotifyOnStrategyMissedSession,
    bool SendEodReport, string[] NotificationChannels);

public record UpdateUserPreferencesDto(
    string? Timezone,
    bool? NotifyOnOrderFill, bool? NotifyOnSLHit, bool? NotifyOnTpHit,
    bool? NotifyOnKillSwitch, bool? NotifyOnStreamReconnect, bool? NotifyOnTokenExpiry,
    bool? NotifyOnDataQuality, bool? NotifyOnColdRestartPause, bool? NotifyOnMonitoringBreach,
    bool? NotifyOnStrategyAutoResumed, bool? NotifyOnStrategyMissedSession,
    bool? SendEodReport, string[]? NotificationChannels);
