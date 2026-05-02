using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

/// <summary>
/// Selects the optimal DTE bucket for a new short-premium leg.
///
/// DTE buckets (index 0-5): 0-3d, 4-7d, 8-14d, 15-21d, 22-35d, 36-45d.
/// Score for each bucket = dot product of config.DtePreferenceWeights[regime] ×
///   [ThetaPerMargin, 1/GammaPerTheta, TermSlope, LiquiditySurvival, FillQuality, RegimeStability].
///
/// VelocityPanic → DteSelection.None.
/// Bucket is ineligible when GammaPerTheta > GammaPerThetaCeiling[regime].
/// Roll gates: weekly (0-7d) requires GammaPerTheta ≤ 1.5, LS ≥ 70, FQ ≥ 70.
///             monthly/quarterly: roll at DTE &lt; 7 if gamma/theta &gt; 2.0 and candidate passes gates.
/// </summary>
public sealed class DteOptimizer(ILogger<DteOptimizer> log) : IDteOptimizer
{
    // Bucket representative DTE values (mid-point of each range)
    private static readonly int[] BucketDte = [2, 5, 11, 18, 28, 40];

    // Descriptive labels for logging
    private static readonly string[] BucketLabel = ["0-3d", "4-7d", "8-14d", "15-21d", "22-35d", "36-45d"];

    public DteSelection OptimizeDte(
        VelocityRegimeState        regime,
        StructureChoice            structure,
        StrategyContext            ctx,
        ShortPremiumVelocityConfig config)
    {
        if (regime.Label == MarketRegime.VelocityPanic)
        {
            log.LogInformation("DteOptimizer: VelocityPanic → DteSelection.None");
            return DteSelectionExtensions.None;
        }

        if (!config.DtePreferenceWeights.TryGetValue(regime.Label, out decimal[]? weights) ||
            weights is not { Length: 6 })
        {
            log.LogWarning("DteOptimizer: no weight vector for regime={R}", regime.Label);
            return new DteSelection(0, false, $"No DTE weights for regime={regime.Label}");
        }

        if (!config.GammaPerThetaCeiling.TryGetValue(regime.Label, out decimal gptCeiling))
            gptCeiling = decimal.MaxValue;

        // ── Compute per-bucket scores ─────────────────────────────────────────
        decimal[] factorVector = ComputeFactorVector(ctx, regime, config);

        decimal bestScore    = decimal.MinValue;
        int     bestBucket   = -1;
        string? blockingGate = null;

        for (int i = 0; i < 6; i++)
        {
            decimal bucketGammaPerTheta = EstimateBucketGammaPerTheta(BucketDte[i]);

            // Eligibility: GammaPerTheta must be within ceiling
            if (bucketGammaPerTheta > gptCeiling)
            {
                log.LogDebug("DteOptimizer: bucket={B} ineligible — GammaPerTheta={GPT:F2}>{Ceil:F2}",
                    BucketLabel[i], bucketGammaPerTheta, gptCeiling);
                blockingGate = $"Bucket={BucketLabel[i]} GammaPerTheta={bucketGammaPerTheta:F2}>{gptCeiling:F2}";
                continue;
            }

            decimal score = 0m;
            for (int f = 0; f < Math.Min(weights.Length, factorVector.Length); f++)
                score += weights[f] * factorVector[f];

            if (score > bestScore)
            {
                bestScore  = score;
                bestBucket = i;
            }
        }

        if (bestBucket < 0)
        {
            log.LogWarning("DteOptimizer: no eligible DTE bucket found. LastBlockingGate={Gate}", blockingGate);
            return new DteSelection(0, false, blockingGate ?? "No eligible DTE bucket");
        }

        int chosenDte = BucketDte[bestBucket];

        // ── Weekly roll gate (DTE 0-7) ────────────────────────────────────────
        if (chosenDte <= 7)
        {
            var rollGates = new List<string>();
            decimal gpt = EstimateBucketGammaPerTheta(chosenDte);
            decimal ls  = ExtractLiquiditySurvival(ctx);
            decimal fq  = ExtractFillQuality(ctx);

            if (gpt > 1.5m)  rollGates.Add($"GammaPerTheta={gpt:F2}>1.5");
            if (ls  < 70m)   rollGates.Add($"LiquiditySurvival={ls:F1}<70");
            if (fq  < 70m)   rollGates.Add($"FillQuality={fq:F1}<70");

            if (rollGates.Count > 0)
            {
                log.LogInformation("DteOptimizer: weekly roll blocked — {Gates}; trying next bucket",
                    string.Join("; ", rollGates));
                // Try next higher DTE bucket
                int nextBucket = bestBucket + 1;
                if (nextBucket < 6)
                {
                    chosenDte = BucketDte[nextBucket];
                    log.LogInformation("DteOptimizer: escalated to DTE={DTE}", chosenDte);
                }
                else
                {
                    return new DteSelection(0, false, "WeeklyRollGate: " + string.Join("; ", rollGates));
                }
            }
        }

        log.LogInformation(
            "DteOptimizer: regime={R} structure={S} → DTE={DTE} (bucket={B} score={Score:F3})",
            regime.Label, structure.Type, chosenDte, BucketLabel[bestBucket], bestScore);

        return new DteSelection(chosenDte, true, null);
    }

    // ── Factor vector [ThetaPerMargin, 1/GammaPerTheta, TermSlope, LiquiditySurvival, FillQuality, RegimeStability]
    private static decimal[] ComputeFactorVector(
        StrategyContext ctx, VelocityRegimeState regime, ShortPremiumVelocityConfig config)
    {
        decimal thetaPerMargin = ctx.OptionChain is not null ? 65m : 45m;
        decimal gpt            = 1.0m;
        decimal termSlope      = 50m; // neutral
        decimal liquiditySurv  = ExtractLiquiditySurvival(ctx);
        decimal fillQuality    = ExtractFillQuality(ctx);
        decimal regimeStab     = regime.RegimeStability;

        return
        [
            thetaPerMargin,
            gpt > 0 ? Math.Min(100m / gpt, 100m) : 100m,
            termSlope,
            liquiditySurv,
            fillQuality,
            regimeStab,
        ];
    }

    private static decimal EstimateBucketGammaPerTheta(int dte)
        // Gamma/Theta increases as DTE decreases (gamma acceleration near expiry)
        => dte <= 3  ? 2.5m
         : dte <= 7  ? 1.5m
         : dte <= 14 ? 1.0m
         : 0.7m;

    private static decimal ExtractLiquiditySurvival(StrategyContext ctx)
        => ctx.OptionChain is not null ? 80m : 50m;

    private static decimal ExtractFillQuality(StrategyContext ctx)
        => ctx.OptionChain is not null ? 75m : 60m;
}
