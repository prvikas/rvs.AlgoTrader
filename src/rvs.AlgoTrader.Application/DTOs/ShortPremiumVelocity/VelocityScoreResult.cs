namespace rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;

/// <summary>
/// Composite scoring output from IVelocityIndicator.
/// Drives structure selection, aggression multiplier, and hedge coverage requirement.
///
/// VelocityScore:       Weighted composite (0–100) of IV mean-reversion, skew, term-structure,
///                      realized-vol spread, and breadth. Higher = more favourable for premium selling.
/// OpportunityDensity:  Measures how many independent premium-selling signals are aligned (0–100).
///                      High OD justifies maximum aggression.
/// AggressionMultiplier: Applied to base position size; range [AggressionMultiplierMin, Max].
/// HedgeCoverageRatio:  Required GammaHedged/GrossGamma for this bar; driven by regime MinHedgeCoverageRatio.
/// StructureHint:       Optional human-readable hint for the structure selector.
/// </summary>
public record VelocityScoreResult(
    decimal VelocityScore,
    decimal OpportunityDensity,
    decimal AggressionMultiplier,
    decimal HedgeCoverageRatio,
    string  StructureHint
);
