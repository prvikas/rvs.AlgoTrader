using FluentAssertions;
using Moq;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Infrastructure.Services;
using Xunit;

namespace rvs.AlgoTrader.Tests.Unit.Services;

public class TrailingStopLossServiceTests
{
    private readonly TrailingStopLossService _svc = new(
        Mock.Of<IPositionRepository>(),
        Mock.Of<rvs.AlgoTrader.Domain.Interfaces.IClock>());

    [Fact]
    public void UpdateTrailingStop_LongPosition_StopMovesUp()
    {
        var state = new TrailingStopState(
            EntryPrice: 100m,
            CurrentStop: 97m,
            Direction: "BUY",
            ActivationThresholdPercent: 1.0m, // activate at 1% gain
            TrailStepPercent: 1.0m            // trail by 1%
        );

        // Price moves to 102 (2% gain, above activation threshold)
        var updated = _svc.UpdateStop(state, currentPrice: 102m);

        updated.CurrentStop.Should().BeGreaterThan(97m);
        updated.CurrentStop.Should().BeLessOrEqualTo(102m * (1 - 0.01m));
    }

    [Fact]
    public void UpdateTrailingStop_LongPosition_StopNeverGoesDown()
    {
        var state = new TrailingStopState(
            EntryPrice: 100m,
            CurrentStop: 97m,
            Direction: "BUY",
            ActivationThresholdPercent: 0.5m,
            TrailStepPercent: 1.0m);

        // Price rises then drops
        var state2 = _svc.UpdateStop(state, 104m);
        var state3 = _svc.UpdateStop(state2, 102m); // price drops back

        state3.CurrentStop.Should().BeGreaterThanOrEqualTo(state2.CurrentStop,
            because: "Stop loss must never regress (only tighten)");
    }

    [Fact]
    public void UpdateTrailingStop_BelowActivationThreshold_StopUnchanged()
    {
        var state = new TrailingStopState(
            EntryPrice: 100m,
            CurrentStop: 97m,
            Direction: "BUY",
            ActivationThresholdPercent: 2.0m, // activate at 2%
            TrailStepPercent: 1.0m);

        // Price at 100.5 — below 2% activation
        var updated = _svc.UpdateStop(state, 100.5m);

        updated.CurrentStop.Should().Be(97m, because: "Trail not yet activated");
    }

    [Fact]
    public void UpdateTrailingStop_ShortPosition_StopMovesDown()
    {
        var state = new TrailingStopState(
            EntryPrice: 100m,
            CurrentStop: 103m,
            Direction: "SELL",
            ActivationThresholdPercent: 1.0m,
            TrailStepPercent: 1.0m);

        // Price moves to 98 (2% decline in short's favour)
        var updated = _svc.UpdateStop(state, 98m);

        updated.CurrentStop.Should().BeLessThan(103m);
        updated.CurrentStop.Should().BeGreaterThanOrEqualTo(98m * (1 + 0.01m));
    }
}
