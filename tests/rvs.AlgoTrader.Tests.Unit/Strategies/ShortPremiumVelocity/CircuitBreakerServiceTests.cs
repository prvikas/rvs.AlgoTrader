using FluentAssertions;
using MediatR;
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
/// </summary>
public class CircuitBreakerServiceTests
{
    private static readonly ShortPremiumVelocityConfig DefaultConfig = new();

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
            publisher.Object, clock.Object, jumpRisk.Object,
            NullLogger<CircuitBreakerService>.Instance);
    }

    // ── Initial state ─────────────────────────────────────────────────────────

    [Fact]
    public void InitialState_IsNormal()
    {
        var sut = BuildSut();

        sut.CurrentState.State.Should().Be(CircuitBreakerStateValue.Normal);
        sut.CurrentState.TriggeredAt.Should().BeNull();
    }

    // ── SoftStop trigger ──────────────────────────────────────────────────────

    [Fact]
    public async Task SoftStop_TriggeredAtSoftStopLossPct()
    {
        var sut = BuildSut();

        await sut.EvaluateAsync(DefaultConfig.SoftStopLossPct, DefaultConfig, CancellationToken.None);

        sut.CurrentState.State.Should().Be(CircuitBreakerStateValue.SoftStop,
            because: $"dailyLoss = SoftStopLossPct={DefaultConfig.SoftStopLossPct} triggers SoftStop");
        sut.CurrentState.TriggeredAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SoftStop_NotTriggered_BelowSoftStopLossPct()
    {
        var sut = BuildSut();

        await sut.EvaluateAsync(DefaultConfig.SoftStopLossPct - 0.001m, DefaultConfig, CancellationToken.None);

        sut.CurrentState.State.Should().Be(CircuitBreakerStateValue.Normal,
            because: "loss just below SoftStopLossPct threshold should stay Normal");
    }

    // ── HardStop trigger ──────────────────────────────────────────────────────

    [Fact]
    public async Task HardStop_TriggeredAtHardStopLossPct()
    {
        var sut = BuildSut();

        await sut.EvaluateAsync(DefaultConfig.HardStopLossPct, DefaultConfig, CancellationToken.None);

        sut.CurrentState.State.Should().Be(CircuitBreakerStateValue.HardStop,
            because: $"dailyLoss = HardStopLossPct={DefaultConfig.HardStopLossPct} triggers HardStop");
        sut.CurrentState.TriggeredAt.Should().NotBeNull();
    }

    [Fact]
    public async Task HardStop_Skips_SoftStop_WhenLossExceedsBothThresholds()
    {
        var sut = BuildSut();

        // Loss exceeds both SoftStop and HardStop simultaneously
        await sut.EvaluateAsync(DefaultConfig.HardStopLossPct + 0.01m, DefaultConfig, CancellationToken.None);

        sut.CurrentState.State.Should().Be(CircuitBreakerStateValue.HardStop,
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
            publisher.Object, clock.Object, jumpRisk.Object,
            NullLogger<CircuitBreakerService>.Instance);

        await sut.EvaluateAsync(DefaultConfig.HardStopLossPct, DefaultConfig, CancellationToken.None);

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

        await sut.EvaluateAsync(DefaultConfig.SoftStopLossPct, DefaultConfig, CancellationToken.None);

        sut.CurrentState.ResetEligibleAt.Should().NotBeNull(
            because: "ResetEligibleAt must be set so the service knows when reset is allowed");
        sut.CurrentState.ResetEligibleAt!.Value.Should().BeGreaterThan(
            sut.CurrentState.TriggeredAt!.Value,
            because: "Reset can only happen after triggering — next trading day at earliest");
    }

    // ── No transition when loss is zero ──────────────────────────────────────

    [Fact]
    public async Task ZeroLoss_NoStateTransition()
    {
        var sut = BuildSut();

        await sut.EvaluateAsync(0m, DefaultConfig, CancellationToken.None);

        sut.CurrentState.State.Should().Be(CircuitBreakerStateValue.Normal);
    }
}
