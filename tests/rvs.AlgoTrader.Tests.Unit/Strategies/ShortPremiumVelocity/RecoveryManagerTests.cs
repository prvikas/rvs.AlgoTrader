using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Strategies.ShortPremiumVelocity;
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.Tests.Unit.Strategies.ShortPremiumVelocity;

/// <summary>
/// Unit tests for RecoveryManager sizing multiplier and step-up logic.
///
/// Panic lock (highest priority):
///   HardStop OR VelocityPanic → multiplier = RecoveryMultiplierMin[Panic] = 0.50.
///
/// Recovery steps:
///   Step 0 / not in recovery → multiplier = 1.0 (uncapped).
///   Step 1 (initial)         → multiplier = RecoveryMultiplierMin[regime].
///   Step 2 (stabilise)       → multiplier = midpoint of min+max.
///   Step 3 (rebuild)         → multiplier = RecoveryMultiplierMax[regime].
///
/// All tests use Guid.Empty as instanceId (single-instance scenario).
/// Per-instance isolation test verifies that separate instanceIds have independent state.
/// </summary>
public class RecoveryManagerTests
{
    private static readonly ShortPremiumVelocityConfig DefaultConfig = new();
    private static readonly Guid TestInstance = Guid.Empty;

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

    private static RecoveryManager BuildSut(
        CircuitBreakerStateValue cbState  = CircuitBreakerStateValue.Normal,
        bool                     jumpStop = false)
    {
        var positions = new Mock<IPositionRepository>();
        positions.Setup(r => r.GetClosedTodayAsync(
                It.IsAny<IEnumerable<Guid>>(), It.IsAny<LocalDate>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([]);

        var cb = new Mock<ICircuitBreakerService>();
        // Mock both CurrentState (compat property) and GetState(Guid) for per-instance access
        var cbStateObj = new CircuitBreakerState(cbState, 0m, null, null);
        cb.Setup(c => c.CurrentState).Returns(cbStateObj);
        cb.Setup(c => c.GetState(It.IsAny<Guid>())).Returns(cbStateObj);

        var clock = new Mock<IClock>();
        clock.Setup(c => c.NowInstant())
             .Returns(Instant.FromUtc(2024, 6, 3, 10, 0, 0));

        return new RecoveryManager(
            BuildScopeFactory(positions.Object), cb.Object, clock.Object,
            NullLogger<RecoveryManager>.Instance);
    }

    // ── Normal (not in recovery) ──────────────────────────────────────────────

    [Fact]
    public void NotInRecovery_ReturnsFullMultiplier()
    {
        var sut    = BuildSut(cbState: CircuitBreakerStateValue.Normal);
        var regime = SpvTestHelpers.MakeRegime(MarketRegime.VelocityChoppyMeanReversion);

        var multiplier = sut.GetRecoveryMultiplier(TestInstance, regime, DefaultConfig);

        multiplier.Should().Be(1.0m,
            because: "when not in recovery mode the multiplier is uncapped at 1.0");
    }

    // ── Panic lock overrides all steps ────────────────────────────────────────

    [Fact]
    public void PanicRegime_ReturnsPanicMin()
    {
        var sut    = BuildSut(cbState: CircuitBreakerStateValue.Normal);
        var regime = SpvTestHelpers.MakeRegime(MarketRegime.VelocityPanic);

        var multiplier = sut.GetRecoveryMultiplier(TestInstance, regime, DefaultConfig);

        decimal expected = DefaultConfig.RecoveryMultiplierMin.GetValueOrDefault(MarketRegime.VelocityPanic, 0.50m);
        multiplier.Should().Be(expected,
            because: "VelocityPanic regime activates the panic lock regardless of recovery step");
    }

    [Fact]
    public void HardStop_ReturnsPanicMin()
    {
        var sut    = BuildSut(cbState: CircuitBreakerStateValue.HardStop);
        var regime = SpvTestHelpers.MakeRegime(MarketRegime.VelocityChoppyMeanReversion);

        var multiplier = sut.GetRecoveryMultiplier(TestInstance, regime, DefaultConfig);

        decimal expected = DefaultConfig.RecoveryMultiplierMin.GetValueOrDefault(MarketRegime.VelocityPanic, 0.50m);
        multiplier.Should().Be(expected,
            because: "CircuitBreaker HardStop activates the panic lock — same floor as Panic regime");
    }

    // ── Step 1 multiplier after EvaluateStepUpAsync enters recovery ───────────

    [Fact]
    public async Task AfterEnteringRecovery_Step1_ReturnsRegimeMin()
    {
        var sut = BuildSut(cbState: CircuitBreakerStateValue.SoftStop);
        var regime = SpvTestHelpers.MakeRegime(MarketRegime.VelocityChoppyMeanReversion);

        // EvaluateStepUpAsync detects SoftStop and enters recovery at step 1
        await sut.EvaluateStepUpAsync(TestInstance, regime, DefaultConfig, CancellationToken.None);

        decimal multiplier = sut.GetRecoveryMultiplier(TestInstance, regime, DefaultConfig);
        decimal expected   = DefaultConfig.RecoveryMultiplierMin.GetValueOrDefault(
            MarketRegime.VelocityChoppyMeanReversion, 0.50m);

        multiplier.Should().Be(expected,
            because: "Recovery step 1 uses RecoveryMultiplierMin for the current regime");
    }

    // ── Multiplier is within [0, 1.25] for all regimes ────────────────────────

    [Theory]
    [InlineData(MarketRegime.VelocityLowVolCompression)]
    [InlineData(MarketRegime.VelocityChoppyMeanReversion)]
    [InlineData(MarketRegime.VelocityPostPanicNormalization)]
    [InlineData(MarketRegime.VelocityHighVolExpansion)]
    [InlineData(MarketRegime.VelocityPanic)]
    public void Multiplier_IsWithinValidRange_ForAllRegimes(MarketRegime label)
    {
        var sut    = BuildSut();
        var regime = SpvTestHelpers.MakeRegime(label);

        var multiplier = sut.GetRecoveryMultiplier(TestInstance, regime, DefaultConfig);

        multiplier.Should().BeInRange(0m, 1.25m,
            because: "Recovery multiplier must always stay within the [0, RecoveryMultiplierMax[max]] band");
    }

    // ── EvaluateStepUpAsync: no transition when CB is Normal ─────────────────

    [Fact]
    public async Task EvaluateStepUp_WhenNormal_DoesNotEnterRecovery()
    {
        var sut    = BuildSut(cbState: CircuitBreakerStateValue.Normal);
        var regime = SpvTestHelpers.MakeRegime(MarketRegime.VelocityChoppyMeanReversion);

        bool steppedUp = await sut.EvaluateStepUpAsync(TestInstance, regime, DefaultConfig, CancellationToken.None);

        steppedUp.Should().BeFalse(because: "CB is Normal — no recovery to step up from");
        // Should stay at uncapped 1.0
        sut.GetRecoveryMultiplier(TestInstance, regime, DefaultConfig).Should().Be(1.0m);
    }

    // ── Per-instance isolation ────────────────────────────────────────────────

    [Fact]
    public async Task PerInstance_InstanceA_Recovery_DoesNotAffectInstanceB()
    {
        var sut = BuildSut(cbState: CircuitBreakerStateValue.SoftStop);
        var instanceA = Guid.NewGuid();
        var instanceB = Guid.NewGuid();
        var regime    = SpvTestHelpers.MakeRegime(MarketRegime.VelocityChoppyMeanReversion);

        // Enter recovery on instance A
        await sut.EvaluateStepUpAsync(instanceA, regime, DefaultConfig, CancellationToken.None);

        decimal multA = sut.GetRecoveryMultiplier(instanceA, regime, DefaultConfig);
        decimal multB = sut.GetRecoveryMultiplier(instanceB, regime, DefaultConfig);

        multA.Should().BeLessThan(1.0m,
            because: "instance A entered recovery — multiplier must be capped");
        multB.Should().Be(1.0m,
            because: "instance B is independent — recovery on A must not reduce B's multiplier");
    }
}
