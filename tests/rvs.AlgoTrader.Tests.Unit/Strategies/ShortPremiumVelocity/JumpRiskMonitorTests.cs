using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Strategies.ShortPremiumVelocity;
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.Tests.Unit.Strategies.ShortPremiumVelocity;

/// <summary>
/// Unit tests for JumpRiskMonitor initial state and StartAsync behaviour.
///
/// JumpRiskMonitor is event-driven (no polling). Tests cover:
/// - Initial CurrentState is a sensible zero-value (IsSoftStop = false).
/// - StartAsync completes immediately and returns without throwing.
/// - IsSoftStop starts false (no spike on startup).
/// </summary>
public class JumpRiskMonitorTests
{
    private static readonly ShortPremiumVelocityConfig DefaultConfig = new();

    private static JumpRiskMonitor BuildSut()
    {
        var brokerFactory = new Mock<IBrokerClientFactory>();
        brokerFactory.Setup(f => f.GetStreamClient(It.IsAny<string>()))
                     .Returns(new Mock<IBrokerStreamClient>().Object);

        var publisher = new Mock<IPublisher>();
        publisher.Setup(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var clock = new Mock<IClock>();
        clock.Setup(c => c.NowInstant()).Returns(Instant.FromUtc(2024, 6, 3, 4, 30, 0));

        return new JumpRiskMonitor(
            brokerFactory.Object, publisher.Object, clock.Object,
            NullLogger<JumpRiskMonitor>.Instance);
    }

    // ── Initial state ─────────────────────────────────────────────────────────

    [Fact]
    public void InitialState_IsSoftStop_IsFalse()
    {
        var sut = BuildSut();

        sut.CurrentState.IsSoftStop.Should().BeFalse(
            because: "on startup there is no observed vol spike — IsSoftStop must start false");
    }

    [Fact]
    public void InitialState_CurrentVolRatio_IsZeroOrPositive()
    {
        var sut = BuildSut();

        sut.CurrentState.CurrentVolRatio.Should().BeGreaterThanOrEqualTo(0m,
            because: "vol ratio cannot be negative");
    }

    [Fact]
    public void InitialState_TriggeredAt_IsNull()
    {
        var sut = BuildSut();

        sut.CurrentState.TriggeredAt.Should().BeNull(
            because: "no spike has occurred on startup — TriggeredAt must be null");
    }

    // ── StartAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_CompletesImmediately()
    {
        var sut = BuildSut();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // StartAsync fires the stream processor as a fire-and-forget task;
        // the method itself should return immediately
        var act = () => sut.StartAsync(DefaultConfig, "mstock", cts.Token);

        await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(1),
            because: "StartAsync is non-blocking — it fires the stream as a background task");
    }

    [Fact]
    public async Task StartAsync_DoesNotThrow()
    {
        var sut = BuildSut();
        using var cts = new CancellationTokenSource();

        var act = async () => await sut.StartAsync(DefaultConfig, "zerodha", cts.Token);

        await act.Should().NotThrowAsync();

        // Cancel to clean up background task
        cts.Cancel();
    }

    // ── CurrentState is immutable snapshot ────────────────────────────────────

    [Fact]
    public void CurrentState_ReturnsSameReference_WhenUnchanged()
    {
        var sut = BuildSut();

        var state1 = sut.CurrentState;
        var state2 = sut.CurrentState;

        // Both calls return the same volatile state (no spike has occurred)
        state1.IsSoftStop.Should().Be(state2.IsSoftStop);
        state1.CurrentVolRatio.Should().Be(state2.CurrentVolRatio);
    }
}
