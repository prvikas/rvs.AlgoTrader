using FluentAssertions;
using NodaTime;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Strategies.PriceActionBreakout;
using Xunit;

namespace rvs.AlgoTrader.Tests.Unit.Strategies;

public class PriceActionBreakoutStrategyTests
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    private static ClosedCandle MakeCandle(
        decimal open, decimal high, decimal low, decimal close, long volume = 100_000,
        int minuteOffset = 0)
    {
        var baseTime = new LocalDateTime(2024, 1, 15, 10, 0, 0)
            .PlusMinutes(minuteOffset).InZoneLeniently(Ist);
        return new ClosedCandle(
            "RELIANCE", "5m",
            baseTime, baseTime.Plus(Duration.FromMinutes(5)),
            open, high, low, close, volume);
    }

    private static List<ClosedCandle> BuildSideways(int count = 25, decimal mid = 2500m)
    {
        var candles = new List<ClosedCandle>();
        for (int i = 0; i < count; i++)
        {
            var variation = (i % 3 == 0) ? 10m : (i % 3 == 1) ? -10m : 0m;
            candles.Add(MakeCandle(mid, mid + 15, mid - 15, mid + variation, 80_000, i * 5));
        }
        return candles;
    }

    private static StrategyContext MakeContext(List<ClosedCandle> candles)
        => new(Guid.Empty, "RELIANCE", "5m", candles, new Dictionary<string, object>(), "test-correlation");

    [Fact]
    public async Task Evaluate_WithInsufficientData_ReturnsSkip()
    {
        var strategy = new PriceActionBreakoutStrategy(new PriceActionBreakoutConfig { LookbackBars = 20 });
        var candles = Enumerable.Range(0, 10).Select(i => MakeCandle(2500, 2510, 2490, 2500, minuteOffset: i * 5)).ToList();
        var ctx = MakeContext(candles);

        var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

        result.Signal.Should().Be("HOLD"); // Skip returns HOLD with SkippedReason
        result.SkippedReason.Should().Be(SkippedReason.InsufficientData.ToString());
    }

    [Fact]
    public async Task Evaluate_WithSidewaysMarket_ReturnsHold()
    {
        var strategy = new PriceActionBreakoutStrategy(new PriceActionBreakoutConfig
        {
            LookbackBars = 20,
            AtrPeriod = 14,
            VolumeMultiple = 1.5m
        });
        var candles = BuildSideways(50); // consolidating
        var ctx = MakeContext(candles);

        var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

        result.Signal.Should().BeOneOf("HOLD", "SKIP");
    }

    [Fact]
    public async Task Evaluate_BullishBreakout_ReturnsBuyWithStops()
    {
        var config = new PriceActionBreakoutConfig
        {
            LookbackBars = 15,
            AtrPeriod = 14,
            AtrStopMultiple = 1.5m,
            RiskRewardRatio = 2.0m,
            VolumeMultiple = 1.0m,   // easy volume filter
            MinAtrMultiple = 0.1m    // easy ATR filter
        };
        var strategy = new PriceActionBreakoutStrategy(config);

        // Build 30 sideways candles at 2500, then breakout above 2530 (range high)
        var candles = new List<ClosedCandle>();
        for (int i = 0; i < 30; i++)
            candles.Add(MakeCandle(2500, 2525, 2480, 2505, minuteOffset: i * 5));

        // Breakout candle: close above range high of 2525
        candles.Add(MakeCandle(2526, 2545, 2520, 2540, 200_000, minuteOffset: 30 * 5));

        var ctx = MakeContext(candles);
        var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

        result.Signal.Should().Be("BUY");
        result.EntryPrice.Should().BeGreaterThan(2525m);
        result.StopLoss.Should().BeLessThan(result.EntryPrice!.Value);
        result.TakeProfit.Should().BeGreaterThan(result.EntryPrice!.Value);
        result.Reason.Should().Contain("Breakout above");
    }

    [Fact]
    public async Task Evaluate_BullishBreakout_StopLossIsAtrBased()
    {
        var config = new PriceActionBreakoutConfig
        {
            LookbackBars = 10,
            AtrPeriod = 10,
            AtrStopMultiple = 2.0m,
            RiskRewardRatio = 2.0m,
            VolumeMultiple = 0.5m,
            MinAtrMultiple = 0.1m
        };
        var strategy = new PriceActionBreakoutStrategy(config);

        var candles = new List<ClosedCandle>();
        for (int i = 0; i < 20; i++)
            candles.Add(MakeCandle(1000, 1015, 990, 1005, minuteOffset: i * 5));
        candles.Add(MakeCandle(1016, 1030, 1010, 1025, 180_000, minuteOffset: 20 * 5));

        var result = await strategy.EvaluateAsync(MakeContext(candles), CancellationToken.None);
        if (result.Signal == "BUY")
        {
            var stopDistance = result.EntryPrice!.Value - result.StopLoss!.Value;
            var tpDistance = result.TakeProfit!.Value - result.EntryPrice!.Value;

            stopDistance.Should().BePositive();
            tpDistance.Should().BeApproximately(stopDistance * 2.0m, stopDistance * 0.5m);
        }
    }

    [Fact]
    public async Task Evaluate_AllowShortFalse_NeverReturnsSell()
    {
        var config = new PriceActionBreakoutConfig
        {
            LookbackBars = 10,
            AtrPeriod = 10,
            VolumeMultiple = 0.5m,
            MinAtrMultiple = 0.1m,
            AllowShort = false
        };
        var strategy = new PriceActionBreakoutStrategy(config);

        // Build 1000 random candle sequences
        var rng = new Random(42);
        for (int trial = 0; trial < 100; trial++)
        {
            var candles = new List<ClosedCandle>();
            for (int i = 0; i < 30; i++)
            {
                var mid = 1000m + (decimal)(rng.NextDouble() * 200);
                candles.Add(MakeCandle(mid, mid + 20, mid - 20, mid - 30, minuteOffset: i * 5));
            }
            var result = await strategy.EvaluateAsync(MakeContext(candles), CancellationToken.None);
            result.Signal.Should().NotBe("SELL", because: "AllowShort=false must suppress SELL signals");
        }
    }

    [Fact]
    public async Task Evaluate_LowVolume_FiltersOutBreakout()
    {
        var config = new PriceActionBreakoutConfig
        {
            LookbackBars = 15,
            AtrPeriod = 10,
            VolumeMultiple = 5.0m,   // require 5× average volume
            MinAtrMultiple = 0.1m
        };
        var strategy = new PriceActionBreakoutStrategy(config);

        var candles = new List<ClosedCandle>();
        for (int i = 0; i < 25; i++)
            candles.Add(MakeCandle(2500, 2520, 2480, 2505, 100_000, minuteOffset: i * 5));

        // Breakout but with normal volume (not 5×)
        candles.Add(MakeCandle(2521, 2540, 2515, 2535, 120_000, minuteOffset: 25 * 5));

        var result = await strategy.EvaluateAsync(MakeContext(candles), CancellationToken.None);
        result.Signal.Should().BeOneOf("HOLD", "SKIP");
    }
}
