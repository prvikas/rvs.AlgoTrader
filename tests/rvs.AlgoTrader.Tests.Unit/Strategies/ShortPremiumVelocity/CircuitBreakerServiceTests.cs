using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Strategies.ShortPremiumVelocity;
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.Tests.Unit.Strategies.ShortPremiumVelocity;

/// <summary>
/// Unit tests for CircuitBreakerService state machine:
///   Normal → SoftStop (dailyLoss ≥ SoftStopLossPct)
///   Normal/SoftStop → HardStop (dailyLoss ≥ HardStopLossPct)
///   HardStop → reset to Normal (next trading day + jumpRisk clear)
///
/// All tests use Guid.Empty as the strategy instanceId (single-instance scenario).
/// Multi-instance isolation is tested implicitly — each instanceId has independent state.
/// </summary>
public class CircuitBreakerServiceTests
{
    private static readonly ShortPremiumVelocityConfig DefaultConfig = new();
    private static readonly Guid TestInstance = Guid.Empty;

    /// <summary>
    /// Builds a scope factory that returns a no-op ISpvStateStore (no DB persistence in unit tests).
    /// </summary>
    private static IServiceScopeFactory BuildScopeFactory()
    {
        var store = new Mock<ISpvStateStore>();
        store.Setup(s => s.LoadCbStateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((PersistedCbState?)null); // no persisted state
        store.Setup(s => s.SaveCbStateAsync(
                It.IsAny<Guid>(), It.IsAny<PersistedCbState>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var sp = new Mock<IServiceProvider>();
        sp.Setup(p => p.GetService(typeof(ISpvStateStore))).Returns(store.Object);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(sp.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }

    private static CircuitBreakerService BuildSut(bool jumpRiskClear = true)
    {
        var publisher = new Mock<IPublisher>();
        publisher.Setup(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var clock = new Mock<IClock>();
        clock.Setup(c => c.NowInstant()).Returns(Instant.FromUtc(2024, 6, 3, 4, 30, 0)); // 10:00 IST

        var jumpRisk = new Mock<IJumpRiskMonitor>();
        jumpRisk.Setup(j => j.CurrentState).Returns(
            new JumpRiskState(1m, 1m, 0.1m, IsSoftStop: !jumpRiskClear, TriggeredAt: null));

        return new CircuitBreakerService(
            BuildScopeFactory(), publisher.Object, clock.Object, jumpRisk.Object,
            NullLogger<CircuitBreakerService>.Instance);
    }

    // ── Initial state ─────────────────────────────────────────────────────────

    [Fact]
    public void InitialState_IsNormal()
    {
        var sut = BuildSut();

        sut.GetState(TestInstance).State.Should().Be(CircuitBreakerStateValue.Normal);
        sut.GetState(TestInstance).TriggeredAt.Should().BeNull();
    }

    // ── SoftStop trigger ──────────────────────────────────────────────────────

    [Fact]
    public async Task SoftStop_TriggeredAtSoftStopLossPct()
    {
        var sut = BuildSut();

        await sut.EvaluateAsync(TestInstance, DefaultConfig.SoftStopLossPct, DefaultConfig, CancellationToken.None);

        sut.GetState(TestInstance).State.Should().Be(CircuitBreakerStateValue.SoftStop,
            because: $"dailyLoss = SoftStopLossPct={DefaultConfig.SoftStopLossPct} triggers SoftStop");
        sut.GetState(TestInstance).TriggeredAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SoftStop_NotTriggered_BelowSoftStopLossPct()
    {
        var sut = BuildSut();

        await sut.EvaluateAsync(TestInstance, DefaultConfig.SoftStopLossPct - 0.001m, DefaultConfig, CancellationToken.None);

        sut.GetState(TestInstance).State.Should().Be(CircuitBreakerStateValue.Normal,
            because: "loss just below SoftStopLossPct threshold should stay Normal");
    }

    // ── HardStop trigger ──────────────────────────────────────────────────────

    [Fact]
    public async Task HardStop_TriggeredAtHardStopLossPct()
    {
        var sut = BuildSut();

        await sut.EvaluateAsync(TestInstance, DefaultConfig.HardStopLossPct, DefaultConfig, CancellationToken.None);

        sut.GetState(TestInstance).State.Should().Be(CircuitBreakerStateValue.HardStop,
            because: $"dailyLoss = HardStopLossPct={DefaultConfig.HardStopLossPct} triggers HardStop");
        sut.GetState(TestInstance).TriggeredAt.Should().NotBeNull();
    }

    [Fact]
    public async Task HardStop_Skips_SoftStop_WhenLossExceedsBothThresholds()
    {
        var sut = BuildSut();

        // Loss exceeds both SoftStop and HardStop simultaneously
        await sut.EvaluateAsync(TestInstance, DefaultConfig.HardStopLossPct + 0.01m, DefaultConfig, CancellationToken.None);

        sut.GetState(TestInstance).State.Should().Be(CircuitBreakerStateValue.HardStop,
            because: "HardStop threshold takes priority over SoftStop when both exceeded");
    }

    // ── HardStop: ActivateKillSwitchCommand is published ─────────────────────

    [Fact]
    public async Task HardStop_PublishesKillSwitchCommand()
    {
        var publisher = new Mock<IPublisher>();
        publisher.Setup(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var clock = new Mock<IClock>();
        clock.Setup(c => c.NowInstant()).Returns(Instant.FromUtc(2024, 6, 3, 4, 30, 0));

        var jumpRisk = new Mock<IJumpRiskMonitor>();
        jumpRisk.Setup(j => j.CurrentState)
                .Returns(new JumpRiskState(1m, 1m, 0.1m, false, null));

        var sut = new CircuitBreakerService(
            BuildScopeFactory(), publisher.Object, clock.Object, jumpRisk.Object,
            NullLogger<CircuitBreakerService>.Instance);

        await sut.EvaluateAsync(TestInstance, DefaultConfig.HardStopLossPct, DefaultConfig, CancellationToken.None);

        publisher.Verify(
            p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "HardStop must publish ActivateKillSwitchCommand exactly once");
    }

    // ── ResetEligibleAt is set when tripped ──────────────────────────────────

    [Fact]
    public async Task ResetEligibleAt_IsSetOnSoftStop()
    {
        var sut = BuildSut();

        await sut.EvaluateAsync(TestInstance, DefaultConfig.SoftStopLossPct, DefaultConfig, CancellationToken.None);

        var state = sut.GetState(TestInstance);
        state.ResetEligibleAt.Should().NotBeNull(
            because: "ResetEligibleAt must be set so the service knows when reset is allowed");
        state.ResetEligibleAt!.Value.Should().BeGreaterThan(
            state.TriggeredAt!.Value,
            because: "Reset can only happen after triggering — next trading day at earliest");
    }

    // ── No transition when loss is zero ──────────────────────────────────────

    [Fact]
    public async Task ZeroLoss_NoStateTransition()
    {
        var sut = BuildSut();

        await sut.EvaluateAsync(TestInstance, 0m, DefaultConfig, CancellationToken.None);

        sut.GetState(TestInstance).State.Should().Be(CircuitBreakerStateValue.Normal);
    }

    // ── Per-instance isolation ────────────────────────────────────────────────

    [Fact]
    public async Task PerInstance_InstanceA_HardStop_DoesNotAffectInstanceB()
    {
        var sut = BuildSut();
        var instanceA = Guid.NewGuid();
        var instanceB = Guid.NewGuid();

        // Trigger HardStop on instance A
        await sut.EvaluateAsync(instanceA, DefaultConfig.HardStopLossPct, DefaultConfig, CancellationToken.None);

        // Instance B should remain Normal
        sut.GetState(instanceA).State.Should().Be(CircuitBreakerStateValue.HardStop,
            because: "instance A triggered HardStop");
        sut.GetState(instanceB).State.Should().Be(CircuitBreakerStateValue.Normal,
            because: "instance B is independent — HardStop on A must not bleed into B");
    }
}
