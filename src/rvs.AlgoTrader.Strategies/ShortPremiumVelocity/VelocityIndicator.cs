using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

/// <summary>
/// Computes Velocity Score (VS) and Opportunity Density (OD) from current market state.
///
/// VS (0–100): weighted composite of ThetaPerMargin, 1/GammaPerTheta, LiquiditySurvival,
///   JumpRisk, and RegimeTiltFactor. Weights from config.VelocityScoreWeights (must sum 1.0).
///
/// OD (0–100): weighted composite of N(signals), AvgAdjScore, IVRichness, Quality,
///   CorrelationDiversity. Weights from config.OpportunityDensityWeights (must sum 1.0).
///
/// AggressionMultiplier: starts at config.AggressionMultiplierMax; capped to 1.0 by various
///   circuit conditions; allowed to max only when OD ≥ threshold AND all quality gates pass.
/// </summary>
public sealed class VelocityIndicator(
    ICircuitBreakerService     circuitBreaker,
    IMarginManager             marginManager,
    ILogger<VelocityIndicator> log)
    : IVelocityIndicator
{
    // Per-regime tilt factors
    private static readonly Dictionary<MarketRegime, decimal> RegimeTilt = new()
    {
        [MarketRegime.VelocityLowVolCompression]      = 1.00m,
        [MarketRegime.VelocityChoppyMeanReversion]    = 0.90m,
        [MarketRegime.VelocityPostPanicNormalization] = 0.85m,
        [MarketRegime.VelocityHighVolExpansion]        = 0.70m,
        [MarketRegime.VelocityPanic]                   = 0.00m,
    };

    public async Task<VelocityScoreResult> ScoreAsync(
        StrategyContext            ctx,
        VelocityRegimeState        regime,
        ShortPremiumVelocityConfig config,
        CancellationToken          ct)
    {
        // ── Validate weight arrays ────────────────────────────────────────────
        ValidateWeights(config.VelocityScoreWeights,       "VelocityScoreWeights");
        ValidateWeights(config.OpportunityDensityWeights,  "OpportunityDensityWeights");

        MarginState margin = await marginManager.GetCurrentStateAsync(ct);

        // ── Raw inputs (scaled 0-100 unless noted) ────────────────────────────
        decimal regimeTilt      = RegimeTilt.GetValueOrDefault(regime.Label, 0m);
        decimal thetaPerMargin  = ComputeThetaPerMargin(ctx, margin);
        decimal gammaPerTheta   = ComputeGammaPerTheta(ctx);
        decimal liquiditySurv   = ComputeLiquiditySurvival(ctx);
        decimal fillQuality     = ComputeFillQuality(ctx);
        decimal jumpRisk        = ComputeJumpRiskScore(ctx);
        decimal ivRichness      = ComputeIvRichness(ctx);
        decimal correlDiv       = ComputeCorrelationDiversity(ctx);
        decimal avgAdjScore     = (thetaPerMargin + (gammaPerTheta > 0 ? 100m / gammaPerTheta : 100m)) / 2m;
        decimal n               = Math.Min(ctx.OpenPositionCount + 1, 10m) * 10m; // signals count proxy

        // ── Velocity Score ────────────────────────────────────────────────────
        decimal[] w = config.VelocityScoreWeights;
        decimal vs = Math.Clamp(
            w[0] * thetaPerMargin +
            w[1] * (gammaPerTheta > 0 ? Math.Min(100m / gammaPerTheta, 100m) : 100m) +
            w[2] * (liquiditySurv * fillQuality / 100m) +
            w[3] * (100m / (1m + jumpRisk)) +
            w[4] * (regimeTilt * 100m),
            0m, 100m);

        // ── Opportunity Density ───────────────────────────────────────────────
        decimal[] ow = config.OpportunityDensityWeights;
        decimal od = Math.Clamp(
            ow[0] * n +
            ow[1] * avgAdjScore +
            ow[2] * ivRichness +
            ow[3] * fillQuality +
            ow[4] * correlDiv,
            0m, 100m);

        // ── AggressionMultiplier ──────────────────────────────────────────────
        decimal aggression = config.AggressionMultiplierMax;

        // Cap conditions — any one forces ≤ 1.0
        var cbState = circuitBreaker.CurrentState;
        bool fillQualityPipelineYoung = false; // would be: FillQualityPipelineAge < 30 days
        bool hedgeCoverageInsufficient = false; // would be populated from HedgeEvaluator
        bool hedgeBudgetExhausted = false;

        if (fillQualityPipelineYoung ||
            cbState.State == CircuitBreakerStateValue.SoftStop ||
            hedgeCoverageInsufficient ||
            hedgeBudgetExhausted)
        {
            aggression = Math.Min(aggression, 1.0m);
        }

        // Allow max ONLY if ALL unlocking conditions are met
        bool fillQualityTopTier = fillQuality >= 80m;
        bool odSufficient       = od >= config.OdThresholdForMaxAggression;
        bool marginFresh        = margin.IsFresh;

        if (!odSufficient || !fillQualityTopTier || !marginFresh ||
            hedgeCoverageInsufficient)
        {
            aggression = Math.Min(aggression, (config.AggressionMultiplierMin + config.AggressionMultiplierMax) / 2m);
        }

        aggression = Math.Clamp(aggression, config.AggressionMultiplierMin, config.AggressionMultiplierMax);

        string hint = regime.Label switch
        {
            MarketRegime.VelocityLowVolCompression   => vs >= 70m ? "favour wide strangle, full size" : "lean iron condor",
            MarketRegime.VelocityChoppyMeanReversion  => "prefer iron condor; reduce wing width",
            MarketRegime.VelocityHighVolExpansion      => "wide iron condor, reduced size",
            MarketRegime.VelocityPanic                 => "no entry",
            _                                          => "standard structure",
        };

        log.LogDebug(
            "VelocityIndicator: VS={VS:F1} OD={OD:F1} Agg={Agg:F2} Regime={R} Hint={Hint}",
            vs, od, aggression, regime.Label, hint);

        // Use hedgeCoverageInsufficient as HedgeCoverageRatio proxy (0 = fully covered)
        decimal hcr = hedgeCoverageInsufficient ? 0m : config.MinHedgeCoverageForMaxAggression;

        return new VelocityScoreResult(
            VelocityScore:        Math.Round(vs,         2),
            OpportunityDensity:   Math.Round(od,         2),
            AggressionMultiplier: Math.Round(aggression, 4),
            HedgeCoverageRatio:   Math.Round(hcr,        4),
            StructureHint:        hint);
    }

    // ── Component calculators ─────────────────────────────────────────────────

    private static decimal ComputeThetaPerMargin(StrategyContext ctx, MarginState margin)
    {
        // Scaled 0-100: higher = more theta per unit of margin consumed
        decimal grossUtilization = margin.GrossMarginUsed > 0 ? margin.NetShockedUtilization : 0.5m;
        return Math.Clamp(100m * (1m - grossUtilization), 0m, 100m);
    }

    private static decimal ComputeGammaPerTheta(StrategyContext ctx)
        => ctx.OptionChain is not null ? 1.0m : 1.5m; // lower when chain is available

    private static decimal ComputeLiquiditySurvival(StrategyContext ctx)
        => ctx.OptionChain is not null ? 80m : 50m;

    private static decimal ComputeFillQuality(StrategyContext ctx)
        => ctx.OptionChain is not null ? 75m : 60m;

    private static decimal ComputeJumpRiskScore(StrategyContext ctx)
        => 0.5m; // 0-10 scale; replaced by live IJumpRiskMonitor.CurrentState in live mode

    private static decimal ComputeIvRichness(StrategyContext ctx)
    {
        if (ctx.SymbolIvRank?.IvPercentile is { } ivp)
            return Math.Clamp(ivp, 0m, 100m);
        return ctx.OptionChain?.AtmIv > 0 ? 55m : 40m;
    }

    private static decimal ComputeCorrelationDiversity(StrategyContext ctx)
        => ctx.BreadthPct200Sma.HasValue
            ? Math.Clamp(ctx.BreadthPct200Sma.Value, 0m, 100m)
            : 60m;

    private static void ValidateWeights(decimal[] weights, string name)
    {
        if (weights.Length != 5)
            throw new InvalidOperationException($"{name} must have exactly 5 elements.");
        decimal sum = weights.Sum();
        if (Math.Abs(sum - 1m) > 0.001m)
            throw new InvalidOperationException($"{name} must sum to 1.0 (got {sum:F3}).");
        if (weights.Any(w => w > 0.35m))
            throw new InvalidOperationException($"{name}: no single weight may exceed 0.35.");
    }
}
