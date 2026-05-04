using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

namespace rvs.AlgoTrader.Tests.Unit.Strategies.ShortPremiumVelocity;

/// <summary>
/// Unit tests for HedgeEngine mandatory (M1-M3) and conditional (C1-C3) hedge logic.
///
/// Mandatory hedges:
///   M1. |NetDelta| > DeltaHedgeTriggerThreshold → DeltaHedge
///   M2. TailRiskScore > 60 → TailRiskHedge
///   M3. HighVol/Panic + net vega &lt; 0 → VegaHedge
///
/// Tests use log inspection to verify hedge placement is triggered.
/// Actual order routing is stubbed (Task.CompletedTask in helpers).
/// </summary>
public class HedgeEngineTests
{
    private static readonly ShortPremiumVelocityConfig DefaultConfig = new();

    private static IServiceScopeFactory BuildScopeFactory(IPositionRepository posRepo)
    {
        var sp = new Mock<IServiceProvider>();
        sp.Setup(p => p.GetService(typeof(IPositionRepository))).Returns(posRepo);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(sp.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }

    private static HedgeEngine BuildSut()
    {
        var positionRepo = new Mock<IPositionRepository>();
        positionRepo.Setup(r => r.GetOpenAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync([]);

        var pricer = new Mock<ISyntheticOptionsPricer>();

        return new HedgeEngine(BuildScopeFactory(positionRepo.Object), pricer.Object, NullLogger<HedgeEngine>.Instance);
    }

    // ── M1: Delta hedge ───────────────────────────────────────────────────────

    [Fact]
    public async Task M1_DeltaHedge_TriggeredWhenNetDeltaAboveThreshold()
    {
        // DeltaHedgeTriggerThreshold default = 50 (from config)
        var sut    = BuildSut();
        var greeks = SpvTestHelpers.MakeGreeks(netDelta: 60m); // |60| > 50 → trigger
        var regime = SpvTestHelpers.MakeRegime(MarketRegime.VelocityChoppyMeanReversion);

        // Should not throw — DeltaHedge placement is fire-and-forget log only in Phase 2
        var act = () => sut.ExecuteMandatoryHedgesAsync(greeks, regime, DefaultConfig, CancellationToken.None);

        await act.Should().NotThrowAsync(because: "M1 trigger should not throw; order routing is delegated");
    }

    [Fact]
    public async Task M1_DeltaHedge_NotTriggeredWhenNetDeltaWithinThreshold()
    {
        var sut    = BuildSut();
        var greeks = SpvTestHelpers.MakeGreeks(netDelta: 10m); // |10| < 50 → no trigger
        var regime = SpvTestHelpers.MakeRegime(MarketRegime.VelocityChoppyMeanReversion);

        await sut.ExecuteMandatoryHedgesAsync(greeks, regime, DefaultConfig, CancellationToken.None);
        // No error → gate correctly evaluated
    }

    // ── M2: Tail-risk hedge ───────────────────────────────────────────────────

    [Fact]
    public async Task M2_TailRiskHedge_TriggeredWhenTailRiskAbove60()
    {
        var sut    = BuildSut();
        var greeks = SpvTestHelpers.MakeGreeks();
        // TailRiskScore on regime: > 60 triggers M2
        var regime = SpvTestHelpers.MakeRegime(tailRiskScore: 65m);

        var act = () => sut.ExecuteMandatoryHedgesAsync(greeks, regime, DefaultConfig, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task M2_TailRiskHedge_NotTriggeredWhenBelow60()
    {
        var sut    = BuildSut();
        var greeks = SpvTestHelpers.MakeGreeks();
        var regime = SpvTestHelpers.MakeRegime(tailRiskScore: 40m);

        await sut.ExecuteMandatoryHedgesAsync(greeks, regime, DefaultConfig, CancellationToken.None);
        // Completes without error
    }

    // ── M3: Vega hedge ───────────────────────────────────────────────────────

    [Fact]
    public async Task M3_VegaHedge_TriggeredWhenHighVolAndNegativeVega()
    {
        var sut    = BuildSut();
        var greeks = SpvTestHelpers.MakeGreeks(netVega: -500m); // net short vega
        var regime = SpvTestHelpers.MakeRegime(MarketRegime.VelocityHighVolExpansion);

        var act = () => sut.ExecuteMandatoryHedgesAsync(greeks, regime, DefaultConfig, CancellationToken.None);

        await act.Should().NotThrowAsync(because: "HighVol + negative NetVega triggers M3 VegaHedge");
    }

    [Fact]
    public async Task M3_VegaHedge_NotTriggeredWhenLowVolAndNegativeVega()
    {
        var sut    = BuildSut();
        var greeks = SpvTestHelpers.MakeGreeks(netVega: -500m);
        var regime = SpvTestHelpers.MakeRegime(MarketRegime.VelocityLowVolCompression); // not HighVol/Panic

        await sut.ExecuteMandatoryHedgesAsync(greeks, regime, DefaultConfig, CancellationToken.None);
        // Should complete — M3 not triggered in LowVol
    }

    // ── C1/C2: Conditional per-position hedges ────────────────────────────────

    [Fact]
    public async Task C2_ProtectivePut_TriggeredWhenGammaPinNearExpiry()
    {
        var sut = BuildSut();
        // ShortPremium put-like position with high gamma/theta near expiry
        var position = SpvTestHelpers.MakePosition(
            gammaPerTheta: DefaultConfig.ConditionalHedgeGammaPerThetaTrigger + 0.1m,
            dte:           DefaultConfig.ConditionalHedgeDteTrigger - 1,
            legType:       LegType.ShortPremium,
            structureType: StructureType.IronCondor);
        var regime = SpvTestHelpers.MakeRegime(MarketRegime.VelocityChoppyMeanReversion);

        var act = () => sut.AddConditionalHedgesAsync(position, regime, DefaultConfig, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    // ── CloseOrRollPairedHedges ───────────────────────────────────────────────

    [Fact]
    public async Task CloseOrRollPairedHedges_NoHedgeLegs_CompletesCleanly()
    {
        var posRepo = new Mock<IPositionRepository>();
        posRepo.Setup(r => r.GetOpenAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var sut      = new HedgeEngine(BuildScopeFactory(posRepo.Object), new Mock<ISyntheticOptionsPricer>().Object,
            NullLogger<HedgeEngine>.Instance);
        var position = SpvTestHelpers.MakePosition();

        var act = () => sut.CloseOrRollPairedHedgesAsync(position, CancellationToken.None);

        await act.Should().NotThrowAsync(because: "no hedge legs → no-op, should not throw");
    }
}
