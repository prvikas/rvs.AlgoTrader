namespace rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;

/// <summary>
/// DTE (days-to-expiry) selection result from IDteOptimizer.
/// IsEligible = false means no DTE bucket passed all hard gates for the current regime.
///
/// Dte:          Chosen days-to-expiry. 0 when IsEligible = false.
/// IsEligible:   True when an eligible DTE bucket was found and a position may be opened.
/// BlockingGate: Name of the blocking gate when IsEligible = false; null otherwise.
/// </summary>
public record DteSelection(
    int     Dte,
    bool    IsEligible,
    string? BlockingGate
);

public static class DteSelectionExtensions
{
    /// <summary>Sentinel value returned when no DTE bucket is eligible (VelocityPanic or no valid bucket).</summary>
    public static DteSelection None =>
        new(0, false, "Regime=Panic or no eligible bucket");
}
