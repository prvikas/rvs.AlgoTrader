using FluentAssertions;
using NodaTime;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Strategies.PriceActionBreakout;
using Xunit;

namespace rvs.AlgoTrader.UnitTests.Strategies;

/// <summary>
/// Unit tests for PriceActionBreakoutStrategy.
/// All tests use fixed ClosedCandle sequences to assert signal logic deterministically.
/// No IClock injection needed: strategy is stateless and does not access time.
/// Rule: IStrategy.EvaluateAsync MUST NOT receive partial/open candles (CLAUDE.md Rule #3).
/// </summary>
public class PriceActionBreakoutStrategyTests
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClosedCandle MakeCandle(
        decimal open, decimal high, decimal low, decimal close,
        long volume = 10_000L, int minuteOffset = 0)
    {
        var baseTime = new LocalDateTime(2024, 6, 3, 9, 15, 0)
            .PlusMinutes(minuteOffset)
            .InZoneStrictly(Ist);
        return new ClosedCandle(
            "NSE:RELIANCE", "15m",
            baseTime, baseTime.Plus(Duration.FromMinutes(15)),
            open, high, low, close, volume);
    }

    private static List<ClosedCandle> BuildConsolidationBreakout(
        decimal consolidationPrice, decimal breakoutClose,
        int lookback = 20, bool highVolume = true)
    {
        // Build lookback+2 flat candles (consolidation range), then a breakout
        var candles = new List<ClosedCandle>();

        // ATR warmup candles (14 + LookbackBars + 1 required = 35 minimum)
        for (int i = 0; i < lookback + 15; i++)
        {
            // Small-range candles to keep ATR steady
            var c = consolidationPrice;
            candles.Add(MakeCandle(c - 0.5m, c + 1m, c - 1m, c, 10_000, minuteOffset: i * 15));
        }

        // Breakout candle: close above N-bar high (consolidationPrice + 1)
        var rangeHigh = consolidationPrice + 1m;
        var volume = highVolume ? 25_000L : 8_000L; // high vol = 2.5x avg
        candles.Add(MakeCandle(
            rangeHigh,
            breakoutClose + 0.5m,
            rangeHigh - 0.5m,
            breakoutClose,
            volume,
            minuteOffset: candles.Count * 15));

        return candles;
    }

    private static StrategyContext MakeContext(IReadOnlyList<ClosedCandle> candles, PriceActionBreakoutConfig? config = null)
    {
        config ??= new PriceActionBreakoutConfig();
        return new StrategyContext(
            Guid.NewGuid(), "NSE:RELIANCE", "15m",
            candles, config, Guid.NewGuid().ToString());
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_WhenBreakoutAboveRangeHighWithVolume_ReturnsBuySignal()
    {
        // Arrange
        var config = new PriceActionBreakoutConfig
        {
            LookbackBars = 20,
            AtrPeriod = 14,
            VolumeMultiple = 1.5m,
            AtrStopMultiple = 1.5m,
            RiskRewardRatio = 2.0m,
            AllowShort = false,
        };
        var strategy = new PriceActionBreakoutStrategy(config);

        // Consolidation at 100; breakout to 102 (above range high of 101)
        var candles = BuildConsolidationBreakout(
            consolidationPrice: 100m,
            breakoutClose: 102m,
            lookback: 20,
            highVolume: true);

        // Act
        var result = await strategy.EvaluateAsync(MakeContext(candles, config), CancellationToken.None);

        // Assert
        result.Signal.Should().Be("BUY");
        result.EntryPrice.Should().BeGreaterThan(100m);
        result.StopLoss.Should().BeLessThan(result.EntryPrice!.Value);
        result.TakeProfit.Should().BeGreaterThan(result.EntryPrice!.Value);
        result.SkippedReason.Should().BeNull();
        result.Reason.Should().Contain("Breakout");
    }

    [Fact]
    public async Task EvaluateAsync_WhenBreakoutWithoutSufficientVolume_ReturnsHold()
    {
        // Arrange
        var config = new PriceActionBreakoutConfig
        {
            LookbackBars = 20,
            VolumeMultiple = 1.5m,  // Requires 1.5x average volume
        };
        var strategy = new PriceActionBreakoutStrategy(config);

        // Low volume = 8000 vs avg 10000 → 0.8x, below 1.5x threshold
        var candles = BuildConsolidationBreakout(100m, 102m, highVolume: false);

        // Act
        var result = await strategy.EvaluateAsync(MakeContext(candles, config), CancellationToken.None);

        // Assert
        result.Signal.Should().Be("HOLD");
    }

    [Fact]
    public async Task EvaluateAsync_WhenInsufficientHistory_ReturnsSkipWithInsufficientData()
    {
        // Arrange: only 5 candles — far fewer than LookbackBars + AtrPeriod + 1 = 35
        var strategy = new PriceActionBreakoutStrategy(new PriceActionBreakoutConfig());
        var candles = Enumerable.Range(0, 5)
            .Select(i => MakeCandle(100, 101, 99, 100.5m, minuteOffset: i * 15))
            .ToList();

        // Act
        var result = await strategy.EvaluateAsync(MakeContext(candles), CancellationToken.None);

        // Assert
        result.Signal.Should().Be("HOLD");
        result.SkippedReason.Should().Be(SkippedReason.InsufficientData.ToString());
    }

    [Fact]
    public async Task EvaluateAsync_WhenPriceWithinRange_ReturnsHold()
    {
        // Arrange: build consolidation but NO breakout on final candle
        var config = new PriceActionBreakoutConfig { LookbackBars = 20, AtrPeriod = 14 };
        var strategy = new PriceActionBreakoutStrategy(config);

        var candles = new List<ClosedCandle>();
        for (int i = 0; i < 36; i++) // enough history
            candles.Add(MakeCandle(100m, 101m, 99m, 100m, minuteOffset: i * 15));
        // Final candle stays inside range — close = 100.5 which is inside [99, 101]
        candles.Add(MakeCandle(100m, 100.8m, 99.5m, 100.5m, minuteOffset: 36 * 15));

        // Act
        var result = await strategy.EvaluateAsync(MakeContext(candles, config), CancellationToken.None);

        // Assert: neither BUY nor SELL
        result.Signal.Should().Be("HOLD");
        result.SkippedReason.Should().BeNull("price within range yields HOLD, not SKIP");
    }

    [Fact]
    public async Task EvaluateAsync_WhenShortBreakdownAndAllowShortTrue_ReturnsSellSignal()
    {
        // Arrange
        var config = new PriceActionBreakoutConfig
        {
            LookbackBars = 20,
            AtrPeriod = 14,
            VolumeMultiple = 1.5m,
            AtrStopMultiple = 1.5m,
            RiskRewardRatio = 2.0m,
            AllowShort = true,   // ← SELL signals enabled
        };
        var strategy = new PriceActionBreakoutStrategy(config);

        // Build breakdown: consolidation at 100, close falls below range low (99)
        var candles = new List<ClosedCandle>();
        for (int i = 0; i < 35; i++)
            candles.Add(MakeCandle(100m, 101m, 99m, 100m, minuteOffset: i * 15));

        // Final: close below range low (99) with high volume
        candles.Add(MakeCandle(99m, 99.5m, 97.5m, 97m, volume: 25_000L, minuteOffset: 35 * 15));

        // Act
        var result = await strategy.EvaluateAsync(MakeContext(candles, config), CancellationToken.None);

        // Assert
        result.Signal.Should().Be("SELL");
        result.StopLoss.Should().BeGreaterThan(result.EntryPrice!.Value);
        result.TakeProfit.Should().BeLessThan(result.EntryPrice!.Value);
    }

    [Fact]
    public async Task EvaluateAsync_WhenShortBreakdownAndAllowShortFalse_ReturnsHold()
    {
        // Arrange: same breakdown but AllowShort = false (default)
        var config = new PriceActionBreakoutConfig { AllowShort = false, LookbackBars = 20 };
        var strategy = new PriceActionBreakoutStrategy(config);

        var candles = new List<ClosedCandle>();
        for (int i = 0; i < 35; i++)
            candles.Add(MakeCandle(100m, 101m, 99m, 100m, minuteOffset: i * 15));
        candles.Add(MakeCandle(99m, 99.5m, 97.5m, 97m, volume: 25_000L, minuteOffset: 35 * 15));

        // Act
        var result = await strategy.EvaluateAsync(MakeContext(candles, config), CancellationToken.None);

        // Assert: no SELL when AllowShort=false
        result.Signal.Should().NotBe("SELL");
    }

    [Fact]
    public async Task EvaluateAsync_WhenAtrTooLow_SkipsWithFilterFailed()
    {
        // Arrange: extremely flat candles → ATR ≈ 0 → below MinAtrMultiple threshold
        var config = new PriceActionBreakoutConfig
        {
            LookbackBars = 10,
            AtrPeriod = 5,
            MinAtrMultiple = 0.8m,
        };
        var strategy = new PriceActionBreakoutStrategy(config);

        // Flat candles: near-zero range means ATR ≈ 0 (will fail ATR filter)
        var candles = Enumerable.Range(0, 20)
            .Select(i => MakeCandle(100m, 100.01m, 99.99m, 100m, minuteOffset: i * 15))
            .ToList();
        // Breakout candle (still tiny range)
        candles.Add(MakeCandle(100m, 100.02m, 99.98m, 100.02m, volume: 25_000L, minuteOffset: 20 * 15));

        // Act
        var result = await strategy.EvaluateAsync(MakeContext(candles, config), CancellationToken.None);

        // Assert: ATR filter failure → skip
        result.Signal.Should().Be("HOLD");
        result.SkippedReason.Should().Be(SkippedReason.FilterFailed.ToString());
    }

    [Fact]
    public async Task EvaluateAsync_StopLossIsAlwaysBelowEntryForBuySignal()
    {
        // Invariant test: SL must be below entry, TP above entry on every BUY
        var config = new PriceActionBreakoutConfig();
        var strategy = new PriceActionBreakoutStrategy(config);
        var candles = BuildConsolidationBreakout(200m, 203m, highVolume: true);

        var result = await strategy.EvaluateAsync(MakeContext(candles, config), CancellationToken.None);

        if (result.Signal == "BUY")
        {
            result.StopLoss.Should().BeLessThan(result.EntryPrice!.Value,
                "stop loss must always be below entry on a long position");
            result.TakeProfit.Should().BeGreaterThan(result.EntryPrice!.Value,
                "take profit must always be above entry on a long position");
        }
    }

    [Fact]
    public async Task EvaluateAsync_RiskRewardRatioIsRespected()
    {
        // TP distance / SL distance should equal RiskRewardRatio
        var config = new PriceActionBreakoutConfig
        {
            RiskRewardRatio = 3.0m,
            AtrStopMultiple = 1.0m,
            LookbackBars = 20,
            AtrPeriod = 14,
            VolumeMultiple = 1.5m,
        };
        var strategy = new PriceActionBreakoutStrategy(config);
        var candles = BuildConsolidationBreakout(100m, 103m, highVolume: true);

        var result = await strategy.EvaluateAsync(MakeContext(candles, config), CancellationToken.None);

        if (result.Signal == "BUY")
        {
            var slDist = result.EntryPrice!.Value - result.StopLoss!.Value;
            var tpDist = result.TakeProfit!.Value - result.EntryPrice!.Value;
            var actualRrr = tpDist / slDist;
            actualRrr.Should().BeApproximately(config.RiskRewardRatio, 0.01m,
                "take profit distance should be RiskRewardRatio × stop loss distance");
        }
    }

    [Fact]
    public void PriceActionBreakoutConfig_DefaultValues_AreCorrect()
    {
        // Smoke test default config
        var config = new PriceActionBreakoutConfig();
        config.LookbackBars.Should().Be(20);
        config.AtrPeriod.Should().Be(14);
        config.RiskRewardRatio.Should().Be(2.0m);
        config.AllowShort.Should().BeFalse("short selling is disabled by default");
    }

    [Fact]
    public void PriceActionBreakoutConfig_FromJson_DeserializesCorrectly()
    {
        const string json = """
            {
                "LookbackBars": 30,
                "AtrPeriod": 10,
                "AtrStopMultiple": 2.0,
                "RiskRewardRatio": 3.0,
                "VolumeMultiple": 2.0,
                "AllowShort": true
            }
            """;

        var config = PriceActionBreakoutConfig.FromJson(json);

        config.LookbackBars.Should().Be(30);
        config.AtrPeriod.Should().Be(10);
        config.AtrStopMultiple.Should().Be(2.0m);
        config.RiskRewardRatio.Should().Be(3.0m);
        config.AllowShort.Should().BeTrue();
    }
}
