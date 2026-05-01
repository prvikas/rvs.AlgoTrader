namespace rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;

/// <summary>
/// Aggregate Greeks for the entire Short Premium Velocity book.
/// Computed by summing individual VelocityPosition Greeks and used by
/// IHedgeEvaluator to determine whether mandatory hedges are required.
///
/// NetDelta:           Net delta of all legs (long + short combined). Target: near zero for delta-neutral.
/// GrossGamma:         Sum of absolute gamma across all short-premium legs (always positive).
/// GammaHedged:        Net gamma after subtracting hedge-leg gamma. Negative = net short gamma.
/// HedgeCoverageRatio: GammaHedged / GrossGamma. 0 = fully unhedged; 1 = fully hedged.
///                     Compared against regime's MinHedgeCoverageRatio.
/// TotalHedgeNetCost:  Cumulative net cost of all hedge legs currently open (negative = net debit).
/// </summary>
public record VelocityPortfolioGreeks(
    decimal NetDelta,
    decimal GrossGamma,
    decimal GammaHedged,
    decimal NetTheta,
    decimal NetVega,
    decimal HedgeCoverageRatio,
    decimal TotalHedgeNetCost
);
