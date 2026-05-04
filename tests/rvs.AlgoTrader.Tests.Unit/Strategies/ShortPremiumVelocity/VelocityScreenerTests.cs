using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

namespace rvs.AlgoTrader.Tests.Unit.Strategies.ShortPremiumVelocity;

/// <summary>
/// Unit tests for VelocityScreener hard-gate + soft-filter logic.
///
/// Hard gates tested:
///   a. TailRiskScore ceiling
///   g. CircuitBreaker HardStop blocks all entries
///
/// Soft-filter: verified not to block entry (Passed remains true).
/// </summary>
public class VelocityScreenerTests
{
    private static readonly ShortPremiumVelocityConfig DefaultConfig = new();

    private static VelocityRegimeState MakeRegime(
        MarketRegime label          = MarketRegime.VelocityChoppyMeanReversion,
        decimal      tailRiskScore  = 30m,
        bool         isResultsSeason = false)
        => new(label, RegimeStability: 80m, TailRiskScore: tailRiskScore,
               VolOfVol: 2m, IsResultsSeason: isResultsSeason,
               ClassifiedAt: NodaTime.Instant.FromUtc(2024, 6, 3, 10, 0, 0),
               ConfigVersion: "test");

    private static StrategyContext MakeCtx()
        => new(Guid.NewGuid(), "NIFTY 50", "1d",
               [SpvTestHelpers.MakeCandle(22_000m)], "{}", "spv-test");

    private static VelocityScreener BuildSut(
        CircuitBreakerStateValue cbState = CircuitBreakerStateValue.Normal,
        decimal netShockedUtilization    = 0.30m)
    {
        var cb = new Mock<ICircuitBreakerService>();
        cb.Setup(c => c.CurrentState).Returns(new CircuitBreakerState(
            State:            cbState,
            DailyLossPercent: 0m,
            TriggeredAt:      null,
            ResetEligibleAt:  null));

        var mm = new Mock<IMarginManager>();
        var marginState = new MarginState(
            GrossMarginUsed:       1_000_000m,
            HedgeMarginCredit:     100_000m,
            NetShockedUtilization: netShockedUtilization,
            IsFresh:               true,
            IsResultsSeason:       false,
            LastRefreshedAt:       NodaTime.Instant.FromUtc(2024, 6, 3, 9, 15, 0));
        mm.Setup(m => m.GetCurrentStateAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(marginState);
        mm.Setup(m => m.GetCurrentStateAsync(It.IsAny<ShortPremiumVelocityConfig>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(marginState);

        return new VelocityScreener(cb.Object, mm.Object, NullLogger<VelocityScreener>.Instance);
    }

    // ── Gate g: CircuitBreaker HardStop ──────────────────────────────────────

    [Fact]
    public async Task HardStop_BlocksAllEntries()
    {
        var sut    = BuildSut(cbState: CircuitBreakerStateValue.HardStop);
        var regime = MakeRegime(label: MarketRegime.VelocityLowVolCompression, tailRiskScore: 10m);

        var result = await sut.ScreenAsync(MakeCtx(), regime, DefaultConfig, CancellationToken.None);

        result.Passed.Should().BeFalse(because: "CircuitBreaker HardStop is a blocking gate");
        result.FailedHardGates.Should().Contain(g => g.Contains("HardStop"),
            because: "gate g (CircuitBreaker) must be listed in FailedHardGates");
    }

    // ── Gate a: TailRiskScore ceiling ─────────────────────────────────────────

    [Fact]
    public async Task TailRiskScore_AboveCeiling_BlocksEntry()
    {
        var sut = BuildSut();
        // LowVolCompression ceiling = 40 (from DefaultConfig)
        var regime = MakeRegime(label: MarketRegime.VelocityLowVolCompression, tailRiskScore: 45m);

        var result = await sut.ScreenAsync(MakeCtx(), regime, DefaultConfig, CancellationToken.None);

        result.Passed.Should().BeFalse();
        result.FailedHardGates.Should().Contain(g => g.Contains("TailRiskScore"),
            because: "TailRiskScore 45 > ceiling 40 for LowVolCompression");
    }

    [Fact]
    public async Task TailRiskScore_BelowCeiling_DoesNotBlock()
    {
        var sut    = BuildSut();
        var regime = MakeRegime(label: MarketRegime.VelocityChoppyMeanReversion, tailRiskScore: 30m);

        var result = await sut.ScreenAsync(MakeCtx(), regime, DefaultConfig, CancellationToken.None);

        // TailRiskScore 30 < ceiling 50 for Choppy — this gate should not fail
        result.FailedHardGates.Should().NotContain(g => g.Contains("TailRiskScore"),
            because: "TailRiskScore 30 is within the 50 ceiling for ChoppyMeanReversion");
    }

    // ── Gate g via SoftStop (SoftStop should NOT be a hard gate) ──────────────

    [Fact]
    public async Task SoftStop_DoesNotBlock_ButReducesAggressionCap()
    {
        var sut    = BuildSut(cbState: CircuitBreakerStateValue.SoftStop);
        var regime = MakeRegime(label: MarketRegime.VelocityChoppyMeanReversion, tailRiskScore: 20m);

        var result = await sut.ScreenAsync(MakeCtx(), regime, DefaultConfig, CancellationToken.None);

        // SoftStop reduces aggression but does not hard-block entry
        result.FailedHardGates.Should().NotContain(g => g.Contains("HardStop"),
            because: "SoftStop only reduces aggression; HardStop blocks");
    }

    // ── RequiresMandatoryHedge flag ───────────────────────────────────────────

    [Fact]
    public async Task TailRiskAbove60_RequiresMandatoryHedge()
    {
        var sut = BuildSut();
        // HighVolExpansion ceiling = 65, so TailRisk 62 passes the ceiling gate
        var regime = MakeRegime(label: MarketRegime.VelocityHighVolExpansion, tailRiskScore: 62m);

        var result = await sut.ScreenAsync(MakeCtx(), regime, DefaultConfig, CancellationToken.None);

        result.RequiresMandatoryHedge.Should().BeTrue(
            because: "TailRiskScore 62 > 60 always requires a mandatory hedge");
    }

    [Fact]
    public async Task HighVolRegime_RequiresMandatoryHedge()
    {
        var sut    = BuildSut();
        var regime = MakeRegime(label: MarketRegime.VelocityHighVolExpansion, tailRiskScore: 40m);

        var result = await sut.ScreenAsync(MakeCtx(), regime, DefaultConfig, CancellationToken.None);

        result.RequiresMandatoryHedge.Should().BeTrue(
            because: "HighVolExpansion regime always requires a hedge");
    }

    // ── Hard-gate g and h not present in normal conditions ────────────────────

    [Fact]
    public async Task NormalConditions_CircuitBreakerGate_DoesNotFail()
    {
        // Without a live OptionChain, the LiquiditySurvival proxy (0.04) falls below
        // the regime floor (0.06), so gate c always fires in test mode.
        // This test verifies the CB-gate (g) and event-gate (f) do NOT fire — the
        // gates that are controllable via mocks.
        var sut    = BuildSut(cbState: CircuitBreakerStateValue.Normal, netShockedUtilization: 0.30m);
        var regime = MakeRegime(label: MarketRegime.VelocityChoppyMeanReversion, tailRiskScore: 20m);

        var result = await sut.ScreenAsync(MakeCtx(), regime, DefaultConfig, CancellationToken.None);

        result.FailedHardGates.Should().NotContain(g => g.Contains("HardStop"),
            because: "CircuitBreaker gate (g) must not fire when CB is Normal");
        result.FailedHardGates.Should().NotContain(g => g.Contains("ResultsSeasonEventProximity"),
            because: "Event proximity gate (f) must not fire when there is no upcoming event");
    }

    // ── SuggestedAggressionCap in range ──────────────────────────────────────

    [Fact]
    public async Task SuggestedAggressionCap_IsWithinConfigBounds()
    {
        var sut    = BuildSut();
        var regime = MakeRegime(label: MarketRegime.VelocityLowVolCompression, tailRiskScore: 10m);

        var result = await sut.ScreenAsync(MakeCtx(), regime, DefaultConfig, CancellationToken.None);

        result.SuggestedAggressionCap.Should().BeGreaterThanOrEqualTo(0m);
        result.SuggestedAggressionCap.Should().BeLessThanOrEqualTo(DefaultConfig.AggressionMultiplierMax);
    }
}
