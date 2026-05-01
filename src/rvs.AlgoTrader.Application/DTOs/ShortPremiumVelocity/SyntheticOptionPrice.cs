namespace rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;

/// <summary>
/// Black-Scholes theoretical price with skew and slippage overlays,
/// returned by ISyntheticOptionsPricer for backtesting and pre-trade estimates.
///
/// TheoreticalPrice:  BS theoretical mid-price before any slippage adjustment.
/// SimulatedFillPrice: Expected fill price after applying the configured slippage fraction.
///                    For short legs: TheoreticalPrice × (1 − SlippageFraction).
///                    Adjusted upward in the afternoon session (AfternoonSlippageMultiplier).
/// SlippageApplied:   Absolute slippage applied: |TheoreticalPrice − SimulatedFillPrice|.
/// </summary>
public record SyntheticOptionPrice(
    decimal TheoreticalPrice,
    decimal Delta,
    decimal Gamma,
    decimal Theta,
    decimal Vega,
    decimal SimulatedFillPrice,
    decimal SlippageApplied
);
