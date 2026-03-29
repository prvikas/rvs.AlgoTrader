namespace rvs.AlgoTrader.Domain.ValueObjects;

/// <summary>
/// Black-Scholes Greeks for a single option leg.
/// All greeks use standard market conventions:
///   Delta:  -1 to +1 (call: 0→1, put: -1→0)
///   Gamma:  per ₹1 move in underlying
///   Theta:  daily decay in ₹ (always negative for long options)
///   Vega:   ₹ change per 1% change in IV
///   Rho:    ₹ change per 1% change in risk-free rate
/// </summary>
public record GreeksSnapshot(
    decimal Delta,
    decimal Gamma,
    decimal Theta,
    decimal Vega,
    decimal Rho,
    decimal TheoreticalPrice
);
