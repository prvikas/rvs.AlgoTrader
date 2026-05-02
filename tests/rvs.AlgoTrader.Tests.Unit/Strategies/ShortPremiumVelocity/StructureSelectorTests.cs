using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

namespace rvs.AlgoTrader.Tests.Unit.Strategies.ShortPremiumVelocity;

/// <summary>
/// Unit tests for StructureSelector.SelectStructure.
///
/// Key rules:
///   - VelocityPanic → StructureType.None
///   - VelocityScore &lt; MinEntry → StructureType.None
///   - LowVolCompression → ShortStraddleStrangle
///   - ChoppyMeanReversion → IronCondor
///   - HighVolExpansion → NEVER ShortStraddleStrangle
///   - PostPanicNormalization → VerticalCreditSpread or CalendarSpread
/// </summary>
public class StructureSelectorTests
{
    private static readonly ShortPremiumVelocityConfig DefaultConfig = new();

    private static StructureSelector BuildSut()
        => new(NullLogger<StructureSelector>.Instance);

    private static VelocityRegimeState MakeRegime(MarketRegime label)
        => new(label, RegimeStability: 80m, TailRiskScore: 20m,
               VolOfVol: 1m, IsResultsSeason: false,
               ClassifiedAt: NodaTime.Instant.FromUtc(2024, 6, 3, 10, 0, 0),
               ConfigVersion: "test");

    private static VelocityScoreResult MakeScore(decimal vs = 70m, decimal od = 70m)
        => new(VelocityScore: vs, OpportunityDensity: od,
               AggressionMultiplier: 1.0m, HedgeCoverageRatio: 0.20m, StructureHint: "");

    // ── Panic → None ─────────────────────────────────────────────────────────

    [Fact]
    public void Panic_ReturnsNone()
    {
        var sut    = BuildSut();
        var regime = MakeRegime(MarketRegime.VelocityPanic);
        var score  = MakeScore(vs: 90m); // high VS would normally trigger entry

        var result = sut.SelectStructure(regime, score, DefaultConfig);

        result.Type.Should().Be(StructureType.None,
            because: "VelocityPanic must never allow new entries");
        result.Reason.Should().Contain("Panic");
    }

    // ── Below minimum velocity score → None ───────────────────────────────────

    [Fact]
    public void LowVelocityScore_ReturnsNone()
    {
        var sut    = BuildSut();
        var regime = MakeRegime(MarketRegime.VelocityLowVolCompression);
        var score  = MakeScore(vs: DefaultConfig.MinVelocityScoreForEntry - 1m); // just below threshold

        var result = sut.SelectStructure(regime, score, DefaultConfig);

        result.Type.Should().Be(StructureType.None,
            because: $"VelocityScore below MinVelocityScoreForEntry={DefaultConfig.MinVelocityScoreForEntry} must block entry");
    }

    // ── LowVolCompression → ShortStraddleStrangle ─────────────────────────────

    [Fact]
    public void LowVolCompression_ReturnsShortStraddleStrangle()
    {
        var sut    = BuildSut();
        var regime = MakeRegime(MarketRegime.VelocityLowVolCompression);
        var score  = MakeScore(vs: 75m);

        var result = sut.SelectStructure(regime, score, DefaultConfig);

        result.Type.Should().Be(StructureType.ShortStraddleStrangle,
            because: "Low-vol compression is ideal for short straddle/strangle");
        result.Priority.Should().Be(1, because: "ShortStraddleStrangle is Priority 1 in LowVolCompression");
    }

    // ── ChoppyMeanReversion → IronCondor ─────────────────────────────────────

    [Fact]
    public void ChoppyMeanReversion_ReturnsIronCondor()
    {
        var sut    = BuildSut();
        var regime = MakeRegime(MarketRegime.VelocityChoppyMeanReversion);
        var score  = MakeScore(vs: 70m);

        var result = sut.SelectStructure(regime, score, DefaultConfig);

        result.Type.Should().Be(StructureType.IronCondor,
            because: "Choppy mean-reversion favours the defined-risk IronCondor structure");
    }

    // ── HighVolExpansion → never ShortStraddleStrangle ────────────────────────

    [Fact]
    public void HighVolExpansion_NeverReturnsShortStraddleStrangle()
    {
        var sut    = BuildSut();
        var regime = MakeRegime(MarketRegime.VelocityHighVolExpansion);
        var score  = MakeScore(vs: 80m);

        var result = sut.SelectStructure(regime, score, DefaultConfig);

        result.Type.Should().NotBe(StructureType.ShortStraddleStrangle,
            because: "naked straddle/strangle is prohibited in HighVolExpansion — undefined risk");
    }

    // ── PostPanicNormalization ────────────────────────────────────────────────

    [Fact]
    public void PostPanicNormalization_ReturnsValidStructure()
    {
        var sut    = BuildSut();
        var regime = MakeRegime(MarketRegime.VelocityPostPanicNormalization);
        var score  = MakeScore(vs: 65m);

        var result = sut.SelectStructure(regime, score, DefaultConfig);

        result.Type.Should().NotBe(StructureType.None,
            because: "PostPanicNormalization with adequate VS should allow a structure");
        result.Type.Should().NotBe(StructureType.ShortStraddleStrangle,
            because: "PostPanicNormalization uses defined-risk structures only");
    }

    // ── Reason populated ──────────────────────────────────────────────────────

    [Fact]
    public void SelectStructure_AlwaysPopulatesReason()
    {
        var sut    = BuildSut();
        var regime = MakeRegime(MarketRegime.VelocityLowVolCompression);
        var score  = MakeScore(vs: 75m);

        var result = sut.SelectStructure(regime, score, DefaultConfig);

        result.Reason.Should().NotBeNullOrEmpty(because: "Reason is required for dashboard display and audit trail");
    }
}
