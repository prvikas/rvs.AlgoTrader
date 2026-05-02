using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

/// <summary>
/// Selects the optimal spread structure based on the current velocity regime and score.
/// Pure function — no I/O. Evaluates candidates in priority order; returns first where
/// VelocityScore ≥ MinVelocityScoreForEntry. Logs every selection decision.
///
/// VelocityPanic → StructureChoice.None (HedgeEngine takes over).
/// VelocityHighVolExpansion → NEVER ShortStraddleStrangle.
/// </summary>
public sealed class StructureSelector(ILogger<StructureSelector> log) : IStructureSelector
{
    public StructureChoice SelectStructure(
        VelocityRegimeState        regime,
        VelocityScoreResult        score,
        ShortPremiumVelocityConfig config)
    {
        if (regime.Label == MarketRegime.VelocityPanic)
        {
            log.LogInformation("StructureSelector: VelocityPanic → None");
            return new StructureChoice(StructureType.None, "Regime=Panic — no new entries", 0);
        }

        if (score.VelocityScore < config.MinVelocityScoreForEntry)
        {
            log.LogInformation(
                "StructureSelector: VS={VS:F1} < MinEntry={Min:F1} → None",
                score.VelocityScore, config.MinVelocityScoreForEntry);
            return new StructureChoice(StructureType.None,
                $"VelocityScore={score.VelocityScore:F1}<MinEntry={config.MinVelocityScoreForEntry:F1}", 0);
        }

        StructureChoice? choice = regime.Label switch
        {
            MarketRegime.VelocityLowVolCompression      => SelectLowVol(score, config),
            MarketRegime.VelocityChoppyMeanReversion    => SelectChoppy(score, config),
            MarketRegime.VelocityPostPanicNormalization => SelectPostPanic(score, config),
            MarketRegime.VelocityHighVolExpansion        => SelectHighVol(score, config),
            _ => null,
        };

        if (choice == null || choice.Type == StructureType.None)
        {
            log.LogWarning("StructureSelector: no structure matched for regime={R} VS={VS:F1}",
                regime.Label, score.VelocityScore);
            return new StructureChoice(StructureType.None,
                $"No structure matched for regime={regime.Label}", 0);
        }

        log.LogInformation(
            "StructureSelector: regime={R} → {Type} (priority={P}) VS={VS:F1} | {Reason}",
            regime.Label, choice.Type, choice.Priority, score.VelocityScore, choice.Reason);
        return choice;
    }

    // ── VelocityLowVolCompression ─────────────────────────────────────────────
    // 1. ShortStraddleStrangle
    // 2. IronCondor (if IVRichness low)
    // 3. CalendarSpread (if front IV < back IV)
    private StructureChoice SelectLowVol(VelocityScoreResult score, ShortPremiumVelocityConfig config)
    {
        // Priority 1: ShortStraddleStrangle — ideal in low-vol compression
        return new StructureChoice(
            StructureType.ShortStraddleStrangle,
            $"LowVolCompression: ShortStraddleStrangle optimal (VS={score.VelocityScore:F1})", 1);
        // Priority 2/3 are fallbacks used when structure gates later fail (handled by RollDecisionEngine)
    }

    // ── VelocityChoppyMeanReversion ───────────────────────────────────────────
    // 1. IronCondor
    // 2. VerticalCreditSpread (if PCR skewed)
    // 3. ShortStraddleStrangle ONLY if GammaPerTheta ≤ 1.0
    private StructureChoice SelectChoppy(VelocityScoreResult score, ShortPremiumVelocityConfig config)
    {
        // Priority 1: IronCondor — best for mean-reverting range
        return new StructureChoice(
            StructureType.IronCondor,
            $"ChoppyMeanReversion: IronCondor (VS={score.VelocityScore:F1})", 1);
    }

    // ── VelocityPostPanicNormalization ────────────────────────────────────────
    // 1. IronCondor
    // 2. VerticalCreditSpread (put credit, elevated skew)
    // 3. CalendarSpread
    private StructureChoice SelectPostPanic(VelocityScoreResult score, ShortPremiumVelocityConfig config)
    {
        // Hint from indicator can steer toward put credit spreads when skew is elevated
        if (score.StructureHint.Contains("put", StringComparison.OrdinalIgnoreCase))
        {
            return new StructureChoice(
                StructureType.VerticalCreditSpread,
                $"PostPanicNormalization: VerticalCreditSpread (put skew elevated, VS={score.VelocityScore:F1})", 2);
        }
        return new StructureChoice(
            StructureType.IronCondor,
            $"PostPanicNormalization: IronCondor (VS={score.VelocityScore:F1})", 1);
    }

    // ── VelocityHighVolExpansion ──────────────────────────────────────────────
    // 1. IronCondor (wide wings — WingWidthStrikes override from config)
    // 2. VerticalCreditSpread (small size only)
    // NEVER ShortStraddleStrangle
    private StructureChoice SelectHighVol(VelocityScoreResult score, ShortPremiumVelocityConfig config)
    {
        return new StructureChoice(
            StructureType.IronCondor,
            $"HighVolExpansion: wide IronCondor (VS={score.VelocityScore:F1}; NEVER straddle/strangle)", 1);
    }
}
