using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

namespace rvs.AlgoTrader.Tests.Unit.Strategies.ShortPremiumVelocity;

/// <summary>
/// Unit tests for VelocityIndicator.ScoreAsync.
///
/// VS (0–100): weighted composite of ThetaPerMargin, 1/GammaPerTheta, LiquiditySurvival,
///   JumpRisk, RegimeTiltFactor.
/// VelocityPanic → RegimeTiltFactor = 0 → VS biased low.
/// AggressionMultiplier: capped to 1.0 by SoftStop or marginal conditions.
/// </summary>
public class VelocityIndicatorTests
{
    private static readonly ShortPremiumVelocityConfig DefaultConfig = new();

    private static VelocityIndicator BuildSut(
        CircuitBreakerStateValue cbState            = CircuitBreakerStateValue.Normal,
        decimal                  netShockedUtil      = 0.30m)
    {
        var cb = new Mock<ICircuitBreakerService>();
        cb.Setup(c => c.CurrentState).Returns(new CircuitBreakerState(
            cbState, 0m, null, null));

        var mm = new Mock<IMarginManager>();
        var marginState = new MarginState(
            GrossMarginUsed:       1_000_000m,
            HedgeMarginCredit:     50_000m,
            NetShockedUtilization: netShockedUtil,
            IsFresh:               true,
            IsResultsSeason:       false,
            LastRefreshedAt:       NodaTime.Instant.FromUtc(2024, 6, 3, 9, 15, 0));
        mm.Setup(m => m.GetCurrentStateAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(marginState);
        mm.Setup(m => m.GetCurrentStateAsync(It.IsAny<ShortPremiumVelocityConfig>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(marginState);

        return new VelocityIndicator(cb.Object, mm.Object, NullLogger<VelocityIndicator>.Instance);
    }

    // ── VelocityScore within [0,100] ──────────────────────────────────────────

    [Theory]
    [InlineData(MarketRegime.VelocityLowVolCompression)]
    [InlineData(MarketRegime.VelocityChoppyMeanReversion)]
    [InlineData(MarketRegime.VelocityPostPanicNormalization)]
    [InlineData(MarketRegime.VelocityHighVolExpansion)]
    [InlineData(MarketRegime.VelocityPanic)]
    public async Task VelocityScore_IsWithin0To100(MarketRegime label)
    {
        var sut    = BuildSut();
        var regime = SpvTestHelpers.MakeRegime(label);

        var result = await sut.ScoreAsync(SpvTestHelpers.MakeCtx(), regime, DefaultConfig, CancellationToken.None);

        result.VelocityScore.Should().BeInRange(0m, 100m,
            because: "VelocityScore is always clamped to [0,100]");
    }

    // ── OpportunityDensity within [0,100] ────────────────────────────────────

    [Fact]
    public async Task OpportunityDensity_IsWithin0To100()
    {
        var sut    = BuildSut();
        var regime = SpvTestHelpers.MakeRegime(MarketRegime.VelocityChoppyMeanReversion);

        var result = await sut.ScoreAsync(SpvTestHelpers.MakeCtx(), regime, DefaultConfig, CancellationToken.None);

        result.OpportunityDensity.Should().BeInRange(0m, 100m);
    }

    // ── Panic regime produces lower VS than low-vol ───────────────────────────

    [Fact]
    public async Task Panic_ProducesLowerVelocityScore_ThanLowVol()
    {
        var sut       = BuildSut();
        var panicReg  = SpvTestHelpers.MakeRegime(MarketRegime.VelocityPanic);
        var lowVolReg = SpvTestHelpers.MakeRegime(MarketRegime.VelocityLowVolCompression);

        var panicResult  = await sut.ScoreAsync(SpvTestHelpers.MakeCtx(), panicReg,  DefaultConfig, CancellationToken.None);
        var lowVolResult = await sut.ScoreAsync(SpvTestHelpers.MakeCtx(), lowVolReg, DefaultConfig, CancellationToken.None);

        panicResult.VelocityScore.Should().BeLessThanOrEqualTo(lowVolResult.VelocityScore,
            because: "Panic regime has RegimeTiltFactor=0 which suppresses VelocityScore");
    }

    // ── SoftStop caps AggressionMultiplier ────────────────────────────────────

    [Fact]
    public async Task SoftStop_CapsAggressionMultiplierAt1()
    {
        var sut    = BuildSut(cbState: CircuitBreakerStateValue.SoftStop);
        var regime = SpvTestHelpers.MakeRegime(MarketRegime.VelocityLowVolCompression);

        var result = await sut.ScoreAsync(SpvTestHelpers.MakeCtx(), regime, DefaultConfig, CancellationToken.None);

        result.AggressionMultiplier.Should().BeLessThanOrEqualTo(1.0m,
            because: "SoftStop limits the aggression multiplier to ≤ 1.0");
    }

    // ── Normal state allows higher aggression ─────────────────────────────────

    [Fact]
    public async Task Normal_AggressionMultiplier_IsPositive()
    {
        var sut    = BuildSut(cbState: CircuitBreakerStateValue.Normal);
        var regime = SpvTestHelpers.MakeRegime(MarketRegime.VelocityLowVolCompression);

        var result = await sut.ScoreAsync(SpvTestHelpers.MakeCtx(), regime, DefaultConfig, CancellationToken.None);

        result.AggressionMultiplier.Should().BePositive();
    }

    // ── HedgeCoverageRatio populated ─────────────────────────────────────────

    [Fact]
    public async Task HedgeCoverageRatio_IsNonNegative()
    {
        var sut    = BuildSut();
        var regime = SpvTestHelpers.MakeRegime(MarketRegime.VelocityChoppyMeanReversion);

        var result = await sut.ScoreAsync(SpvTestHelpers.MakeCtx(), regime, DefaultConfig, CancellationToken.None);

        result.HedgeCoverageRatio.Should().BeGreaterThanOrEqualTo(0m);
    }
}
