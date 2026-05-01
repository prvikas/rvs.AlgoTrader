using NodaTime;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Domain.ValueObjects;

/// <summary>
/// Immutable snapshot of the Short Premium Velocity regime classification.
/// Produced by VelocityRegimeClassifier and consumed by the SPV engine
/// components (screener, structure selector, DTE optimizer, hedge engine, etc.).
///
/// All downstream decisions are deterministic given the same VelocityRegimeState —
/// equal states always produce equal strategy decisions (required for backtesting
/// reproducibility and auditability).
///
/// Label:           Five-label velocity regime (VelocityLowVolCompression … VelocityPanic).
/// RegimeStability: Stability score 0–100; clamp(0,100,(1−VolOfVol/MaxExpectedVolOfVol)×100).
/// TailRiskScore:   Composite tail-risk 0–100; 0.40×(VIX/40×100)+0.30×(RoC/50×100)+0.30×(1−R²)×100.
///                  Scores ≥ 60 trigger mandatory tail-risk hedges regardless of regime.
/// VolOfVol:        Std-dev of 10 sequential 5-day realised-vol windows (annualised %).
/// IsResultsSeason: True when current month is in Jan–Feb, Apr–May, Jul–Aug, Oct–Nov.
/// ClassifiedAt:    UTC instant at which this state was classified.
/// ConfigVersion:   SHA-256 hex digest of effective ParametersJson; guards stale cached states.
/// </summary>
public record VelocityRegimeState(
    MarketRegime Label,
    decimal      RegimeStability,
    decimal      TailRiskScore,
    decimal      VolOfVol,
    bool         IsResultsSeason,
    Instant      ClassifiedAt,
    string       ConfigVersion
);
