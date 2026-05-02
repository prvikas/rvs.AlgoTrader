using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

namespace rvs.AlgoTrader.Tests.Unit.Strategies.ShortPremiumVelocity;

/// <summary>
/// Unit tests for DteOptimizer.OptimizeDte.
///
/// DTE buckets (index 0–5): 0–3d (γ/θ≈2.5), 4–7d (γ/θ≈1.5), 8–14d (γ/θ≈1.0),
///   15–21d, 22–35d, 36–45d (all γ/θ≈0.7).
///
/// VelocityPanic → DteSelection.None (IsEligible = false).
/// Weekly bucket (DTE ≤ 7) requires γ/θ ≤ 1.5, LS ≥ 70, FQ ≥ 70.
/// </summary>
public class DteOptimizerTests
{
    private static readonly ShortPremiumVelocityConfig DefaultConfig = new();

    private static DteOptimizer BuildSut()
        => new(NullLogger<DteOptimizer>.Instance);

    private static VelocityRegimeState MakeRegime(MarketRegime label)
        => new(label, RegimeStability: 80m, TailRiskScore: 20m,
               VolOfVol: 1m, IsResultsSeason: false,
               ClassifiedAt: NodaTime.Instant.FromUtc(2024, 6, 3, 10, 0, 0),
               ConfigVersion: "test");

    private static StructureChoice MakeStructure(StructureType type = StructureType.IronCondor)
        => new(type, "Test", 1);

    private static StrategyContext MakeCtx()
        => new(Guid.NewGuid(), "NIFTY 50", "1d",
               [SpvTestHelpers.MakeCandle(22_000m)], "{}", "spv-test");

    // ── Panic → None ─────────────────────────────────────────────────────────

    [Fact]
    public void Panic_ReturnsDteSelectionNone()
    {
        var sut    = BuildSut();
        var regime = MakeRegime(MarketRegime.VelocityPanic);

        var result = sut.OptimizeDte(regime, MakeStructure(), MakeCtx(), DefaultConfig);

        result.IsEligible.Should().BeFalse(because: "No DTE allocation in Panic regime");
        result.BlockingGate.Should().NotBeNullOrEmpty(because: "Panic is a hard blocking gate");
    }

    // ── Weights missing → blocked ─────────────────────────────────────────────

    [Fact]
    public void NoDteWeights_ReturnsNotEligible()
    {
        var sut    = BuildSut();
        var regime = MakeRegime(MarketRegime.VelocityChoppyMeanReversion);
        // Config with empty DtePreferenceWeights
        var config = new ShortPremiumVelocityConfig { DtePreferenceWeights = new() };

        var result = sut.OptimizeDte(regime, MakeStructure(), MakeCtx(), config);

        result.IsEligible.Should().BeFalse(because: "Missing weight vector → no eligible DTE");
    }

    // ── Normal case: eligible DTE returned ────────────────────────────────────

    [Theory]
    [InlineData(MarketRegime.VelocityLowVolCompression)]
    [InlineData(MarketRegime.VelocityChoppyMeanReversion)]
    [InlineData(MarketRegime.VelocityPostPanicNormalization)]
    [InlineData(MarketRegime.VelocityHighVolExpansion)]
    public void NormalRegime_ReturnsEligibleDte(MarketRegime label)
    {
        var sut    = BuildSut();
        var regime = MakeRegime(label);

        var result = sut.OptimizeDte(regime, MakeStructure(), MakeCtx(), DefaultConfig);

        // With OptionChain = null, LiquiditySurvival = 50 < 70 → weekly gate blocks DTE 0-7
        // So result should be DTE ≥ 8 (one of the longer buckets)
        result.IsEligible.Should().BeTrue(because: $"{label} has DTE weights and non-panic regime");
        result.Dte.Should().BePositive(because: "Eligible DTE must be > 0");
    }

    // ── Eligible DTE is within a valid bucket range ────────────────────────────

    [Fact]
    public void EligibleDte_IsWithinKnownBucketRange()
    {
        var sut    = BuildSut();
        var regime = MakeRegime(MarketRegime.VelocityChoppyMeanReversion);

        var result = sut.OptimizeDte(regime, MakeStructure(), MakeCtx(), DefaultConfig);

        // Valid bucket representative DTE values: 2, 5, 11, 18, 28, 40
        int[] validDtes = [2, 5, 11, 18, 28, 40];
        if (result.IsEligible)
        {
            validDtes.Should().Contain(result.Dte,
                because: "DteOptimizer selects from the 6 predefined DTE bucket midpoints");
        }
    }

    // ── DTE 0-7 bucket skipped when LiquiditySurvival < 70 ────────────────────

    [Fact]
    public void WeeklyBucket_Skipped_WhenOptionChainAbsent()
    {
        // Without OptionChain, ExtractLiquiditySurvival returns 50 < 70 → weekly gate fails
        var sut    = BuildSut();
        var regime = MakeRegime(MarketRegime.VelocityChoppyMeanReversion);

        var result = sut.OptimizeDte(regime, MakeStructure(), MakeCtx(), DefaultConfig);

        if (result.IsEligible)
        {
            result.Dte.Should().BeGreaterThan(7,
                because: "DTE ≤ 7 bucket requires LiquiditySurvival ≥ 70; without option chain it is 50");
        }
    }

    // ── BlockingGate populated when not eligible ──────────────────────────────

    [Fact]
    public void NotEligible_PopulatesBlockingGate()
    {
        var sut    = BuildSut();
        var regime = MakeRegime(MarketRegime.VelocityPanic);

        var result = sut.OptimizeDte(regime, MakeStructure(), MakeCtx(), DefaultConfig);

        result.IsEligible.Should().BeFalse();
        result.BlockingGate.Should().NotBeNullOrEmpty(
            because: "BlockingGate must be populated to explain why DTE selection failed");
    }
}
