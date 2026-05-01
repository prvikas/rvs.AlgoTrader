using NodaTime;

namespace rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;

/// <summary>Whether the intraday circuit breaker has tripped and to what degree.</summary>
public enum CircuitBreakerStateValue
{
    /// <summary>Daily loss is within normal bounds — no restrictions.</summary>
    Normal,

    /// <summary>Daily loss exceeded SoftStopLossPct — reduce new position size by 50%.</summary>
    SoftStop,

    /// <summary>Daily loss exceeded HardStopLossPct — no new positions until next session.</summary>
    HardStop,
}

/// <summary>
/// Snapshot of the intraday circuit-breaker produced by ICircuitBreakerService.
/// TriggeredAt:     UTC instant when the circuit breaker tripped; null when State = Normal.
/// ResetEligibleAt: Earliest UTC instant at which a reset to Normal is eligible; null when State = Normal.
/// </summary>
public record CircuitBreakerState(
    CircuitBreakerStateValue State,
    decimal                  DailyLossPercent,
    Instant?                 TriggeredAt,
    Instant?                 ResetEligibleAt
);
