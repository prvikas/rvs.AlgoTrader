namespace rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;

/// <summary>
/// The spread structure type that IStructureSelector chose for a given regime and score.
/// </summary>
public enum StructureType
{
    /// <summary>No structure can be selected (e.g. VelocityPanic regime).</summary>
    None,

    /// <summary>Short straddle (ATM) or short strangle (equidistant OTM calls and puts).</summary>
    ShortStraddleStrangle,

    /// <summary>Four-legged iron condor: short OTM call + put, long further-OTM wing on each side.</summary>
    IronCondor,

    /// <summary>Single-sided credit spread (call or put), used in strongly directional environments.</summary>
    VerticalCreditSpread,

    /// <summary>Calendar spread: sell near-term, buy far-term at same strike.</summary>
    CalendarSpread,
}

/// <summary>
/// Immutable result from IStructureSelector describing which spread structure to use
/// and why, with a priority rank for fallback chaining.
/// Priority: 1 = first choice, 2 = first fallback, etc.
/// </summary>
public record StructureChoice(
    StructureType Type,
    string        Reason,
    int           Priority
);
