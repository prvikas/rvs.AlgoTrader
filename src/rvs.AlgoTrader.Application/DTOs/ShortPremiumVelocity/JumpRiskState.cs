using NodaTime;

namespace rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;

/// <summary>
/// Real-time jump-risk state maintained by IJumpRiskMonitor.
/// Updated on every broker WebSocket tick; compared against a rolling
/// std-dev band to detect intraday vol spikes.
///
/// CurrentVolRatio: (current 30-min realised vol) / rolling mean.
///                  Values > JumpRiskSpikeStdDevMultiplier × std-dev above mean trigger a soft stop.
/// IsSoftStop:      True when a spike threshold crossing is active and new positions are suppressed.
/// TriggeredAt:     UTC instant the soft stop was last triggered; null when IsSoftStop = false.
/// </summary>
public record JumpRiskState(
    decimal  CurrentVolRatio,
    decimal  RollingMean,
    decimal  RollingStdDev,
    bool     IsSoftStop,
    Instant? TriggeredAt
);
