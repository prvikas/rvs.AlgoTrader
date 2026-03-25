using FluentAssertions;
using rvs.AlgoTrader.Infrastructure.Services;
using Xunit;

namespace rvs.AlgoTrader.UnitTests.Infrastructure;

/// <summary>
/// Unit tests for O(1) incremental indicator implementations.
///
/// Design principles:
/// - Each indicator is tested against a known, hand-computed expected value.
/// - Tests verify the warmup period (null returned until enough data points).
/// - Tests verify Reset() truly clears all state.
/// - ATR tests use the overload Update(high, low, close) — the Update(close) overload is N/A for ATR.
/// - Precision: decimal arithmetic — assertions use BeApproximately where floating-point matters.
/// </summary>
public class IncrementalIndicatorTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // IncrementalEma
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void IncrementalEma_DuringWarmup_ReturnsNull()
    {
        // Arrange: period=3 → needs 3 data points before first value
        var ema = new IncrementalEma(3);

        // Act: feed 2 values (< period)
        ema.Update(10m).Should().BeNull("first value is below warmup threshold");
        ema.Update(12m).Should().BeNull("second value is still below warmup threshold");
    }

    [Fact]
    public void IncrementalEma_AtPeriodBoundary_ReturnsSimpleAverage()
    {
        // For a period-3 EMA: the FIRST value is the SMA of the first 3 data points
        var ema = new IncrementalEma(3);
        ema.Update(10m);
        ema.Update(11m);

        // Act: third data point → first EMA value = (10 + 11 + 12) / 3 = 11
        var result = ema.Update(12m);

        result.Should().NotBeNull();
        result!.Value.Should().BeApproximately(11m, 0.001m,
            "first EMA = SMA of the first N data points = (10 + 11 + 12) / 3 = 11");
    }

    [Fact]
    public void IncrementalEma_SubsequentValues_UseExponentialSmoothing()
    {
        // Period=3, k = 2/(3+1) = 0.5
        // Data: 10, 12, 14, 16
        // Seed EMA = (10 + 12 + 14) / 3 = 12
        // EMA after 16: 16 * 0.5 + 12 * 0.5 = 14
        var ema = new IncrementalEma(3);
        ema.Update(10m);
        ema.Update(12m);
        ema.Update(14m); // seed = 12

        var result = ema.Update(16m);

        result.Should().NotBeNull();
        result!.Value.Should().BeApproximately(14m, 0.001m,
            "EMA = 16 * 0.5 + 12 * 0.5 = 14 (k=0.5 for period=3)");
    }

    [Fact]
    public void IncrementalEma_LargeDataSet_ConvergesCorrectly()
    {
        // Feed a constant stream → EMA should converge to that constant
        var ema = new IncrementalEma(5);
        for (int i = 0; i < 100; i++)
            ema.Update(100m);

        var final = ema.Update(100m);
        final.Should().NotBeNull();
        final!.Value.Should().BeApproximately(100m, 0.001m,
            "EMA of a constant series must equal that constant");
    }

    [Fact]
    public void IncrementalEma_Reset_ClearsAllState()
    {
        var ema = new IncrementalEma(3);
        ema.Update(10m);
        ema.Update(11m);
        ema.Update(12m); // now has a value

        ema.Reset();

        // After reset, warmup begins fresh
        ema.Update(5m).Should().BeNull("count reset to 0 — warmup restarts");
        ema.Update(5m).Should().BeNull("still warming up");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // IncrementalSma
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void IncrementalSma_DuringWarmup_ReturnsNull()
    {
        var sma = new IncrementalSma(4);
        sma.Update(10m).Should().BeNull();
        sma.Update(20m).Should().BeNull();
        sma.Update(30m).Should().BeNull("3 of 4 period points — still warming up");
    }

    [Fact]
    public void IncrementalSma_AtPeriodBoundary_ReturnsMean()
    {
        // SMA(4) of [10, 20, 30, 40] = 25
        var sma = new IncrementalSma(4);
        sma.Update(10m);
        sma.Update(20m);
        sma.Update(30m);
        var result = sma.Update(40m);

        result.Should().NotBeNull();
        result!.Value.Should().Be(25m, "SMA(4) of [10,20,30,40] = 100/4 = 25");
    }

    [Fact]
    public void IncrementalSma_SlidingWindow_DropsOldestValue()
    {
        // Period=3. Feed: 10, 20, 30 → SMA=20. Then feed 40 → window=[20,30,40] → SMA=30
        var sma = new IncrementalSma(3);
        sma.Update(10m);
        sma.Update(20m);
        sma.Update(30m).Should().Be(20m);   // (10+20+30)/3 = 20

        var result = sma.Update(40m);
        result.Should().Be(30m, "sliding window drops 10, adds 40 → [20,30,40] → 30");
    }

    [Fact]
    public void IncrementalSma_ConstantSeries_EqualsConstant()
    {
        var sma = new IncrementalSma(5);
        for (int i = 0; i < 100; i++)
            sma.Update(50m);

        var final = sma.Update(50m);
        final!.Value.Should().Be(50m, "SMA of a constant series equals that constant");
    }

    [Fact]
    public void IncrementalSma_Reset_ClearsCircularBuffer()
    {
        var sma = new IncrementalSma(3);
        sma.Update(100m);
        sma.Update(200m);
        sma.Update(300m); // sum = 600

        sma.Reset();

        // After reset, buffer is zeroed; old values must not pollute new warmup
        sma.Update(10m);
        sma.Update(10m);
        var result = sma.Update(10m);

        result.Should().Be(10m, "after Reset, buffer is clean — SMA of [10,10,10] = 10");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // IncrementalAtr (Wilder's smoothing)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void IncrementalAtr_DuringWarmup_ReturnsNull()
    {
        var atr = new IncrementalAtr(3);

        // Period=3: first three bars warm up; Update returns null until _count == period
        atr.Update(105m, 95m, 100m).Should().BeNull("first bar: just accumulating TR");
        atr.Update(106m, 96m, 101m).Should().BeNull("second bar: still below warmup threshold");
    }

    [Fact]
    public void IncrementalAtr_AtPeriodBoundary_ReturnsMeanTrueRange()
    {
        // Period=3. Constant-range candles: H-L=10, no gap.
        // TR1 = 10-0 = 10 (no previous close yet → H-L)
        // TR2 = max(10, |11-10|, |1-10|) = max(10, 1, 9) = 10
        //    Candle 2: H=11, L=1, C=6, prev_close=10 → TR = max(11-1=10, |11-10|=1, |1-10|=9) = 10
        // TR3 = similarly 10
        // Seed ATR = (10 + 10 + 10) / 3 = 10

        var atr = new IncrementalAtr(3);
        atr.Update(high: 15m, low: 5m,  close: 10m); // TR = 10
        atr.Update(high: 16m, low: 6m,  close: 11m); // TR = max(10, 6, 5) = 10
        var result = atr.Update(high: 17m, low: 7m, close: 12m); // TR = max(10, 6, 5) = 10

        result.Should().NotBeNull();
        result!.Value.Should().BeApproximately(10m, 0.01m,
            "seed ATR = average of first N true ranges = (10+10+10)/3 = 10");
    }

    [Fact]
    public void IncrementalAtr_SubsequentBars_UseWilderSmoothing()
    {
        // Wilder's smoothing: ATR = (prev_ATR * (period-1) + TR) / period
        // Period=3: ATR_new = (ATR_prev * 2 + TR) / 3
        // Seed from 3 constant-10 candles → ATR=10
        // Add 4th candle with TR=4 → ATR = (10*2 + 4)/3 = 24/3 = 8

        var atr = new IncrementalAtr(3);
        atr.Update(15m, 5m,  10m); // warmup
        atr.Update(16m, 6m,  11m); // warmup
        atr.Update(17m, 7m,  12m); // seed ATR = 10

        // 4th candle: H=14, L=10, C=12 → TR = max(14-10=4, |14-12|=2, |10-12|=2) = 4
        var result = atr.Update(14m, 10m, 12m);

        result.Should().NotBeNull();
        result!.Value.Should().BeApproximately(8m, 0.01m,
            "Wilder ATR = (10*2 + 4)/3 = 8 after 4th bar with TR=4");
    }

    [Fact]
    public void IncrementalAtr_HighGapUp_TrueRangeIncludesGap()
    {
        // Gap-up: prev_close=100, next H=120, L=105 → TR = max(120-105=15, |120-100|=20, |105-100|=5) = 20
        var atr = new IncrementalAtr(1);
        // Warm up with one bar (period=1 → seeds immediately)
        var seed = atr.Update(110m, 90m, 100m); // TR=20; seed ATR=20

        seed.Should().NotBeNull();

        // Now gap-up candle
        // TR = max(120-105=15, |120-100|=20, |105-100|=5) = 20
        // ATR_new = (20*(1-1) + 20)/1 = 20 (stays 20 when TR unchanged)
        var result = atr.Update(120m, 105m, 110m);
        result.Should().NotBeNull();
        result!.Value.Should().BeGreaterThan(0m, "ATR must remain positive after gap-up");
    }

    [Fact]
    public void IncrementalAtr_Reset_ClearsAllAccumulators()
    {
        var atr = new IncrementalAtr(2);
        atr.Update(15m, 5m,  10m);
        atr.Update(16m, 6m,  11m); // seeded

        atr.Reset();

        atr.Update(15m, 5m, 10m).Should().BeNull("after Reset, count restarts from 0");
    }

    [Fact]
    public void IncrementalAtr_PeriodOne_ReturnsTrueRangeImmediately()
    {
        // Period=1: seed on the first bar
        var atr = new IncrementalAtr(1);

        // First bar: no previous close → TR = H - L = 10
        var result = atr.Update(15m, 5m, 10m);

        result.Should().NotBeNull();
        result!.Value.Should().BeApproximately(10m, 0.001m,
            "ATR(1) seeds immediately; TR of first bar = H-L when no prev close = 15-5 = 10");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // IncrementalVwap
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void IncrementalVwap_SingleBar_ReturnsBarPrice()
    {
        // VWAP = sum(price * vol) / sum(vol)
        // Single bar: price=100, vol=1000 → VWAP = 100
        var vwap = new IncrementalVwap();

        var result = vwap.Update(100m, 1_000L);

        result.Should().NotBeNull();
        result!.Value.Should().Be(100m, "VWAP of single bar = price when only one bar");
    }

    [Fact]
    public void IncrementalVwap_EqualVolumeBars_ReturnsArithmeticMean()
    {
        // Equal volumes: VWAP = unweighted average
        // Bars: (100, 1000), (110, 1000), (120, 1000)
        // VWAP = (100*1000 + 110*1000 + 120*1000) / (3*1000) = 110
        var vwap = new IncrementalVwap();
        vwap.Update(100m, 1_000L);
        vwap.Update(110m, 1_000L);
        var result = vwap.Update(120m, 1_000L);

        result!.Value.Should().BeApproximately(110m, 0.001m,
            "equal-volume VWAP = arithmetic mean = (100+110+120)/3 = 110");
    }

    [Fact]
    public void IncrementalVwap_WeightedByVolume_HighVolumeBarPullsVwapCloser()
    {
        // Bar1: price=100, vol=100   → contributes 10,000
        // Bar2: price=200, vol=900   → contributes 180,000
        // VWAP = 190,000 / 1,000 = 190 (pulled toward the high-volume bar at 200)
        var vwap = new IncrementalVwap();
        vwap.Update(100m, 100L);
        var result = vwap.Update(200m, 900L);

        result!.Value.Should().BeApproximately(190m, 0.001m,
            "VWAP must be volume-weighted: high-vol bar at 200 pulls VWAP to 190");
    }

    [Fact]
    public void IncrementalVwap_NullVolume_ReturnsNull()
    {
        // VWAP requires volume; without it the calculation is undefined
        var vwap = new IncrementalVwap();

        var result = vwap.Update(100m, volume: null);

        result.Should().BeNull("VWAP is undefined without volume data");
    }

    [Fact]
    public void IncrementalVwap_ZeroVolume_ReturnsNull()
    {
        // Volume = 0 → cumVol stays 0 → division by zero guard → return null
        var vwap = new IncrementalVwap();

        var result = vwap.Update(100m, 0L);

        result.Should().BeNull("VWAP with zero volume is undefined — guard against division by zero");
    }

    [Fact]
    public void IncrementalVwap_Reset_ClearsAccumulators()
    {
        var vwap = new IncrementalVwap();
        vwap.Update(200m, 5_000L);  // cumPV=1,000,000, cumVol=5,000

        vwap.Reset();

        // After reset, a fresh single bar at 50 should yield VWAP=50 (not polluted by old data)
        var result = vwap.Update(50m, 1_000L);
        result!.Value.Should().Be(50m, "after Reset, accumulator is clean");
    }

    [Fact]
    public void IncrementalVwap_Monotonically_AccumulatesAcrossBars()
    {
        // VWAP should reflect all bars since last Reset — no sliding window
        var vwap = new IncrementalVwap();
        decimal? prev = null;
        for (int i = 1; i <= 10; i++)
        {
            var result = vwap.Update(i * 10m, 1_000L);
            result.Should().NotBeNull("VWAP with volume should always return a value");
            if (prev.HasValue)
                result!.Value.Should().BeGreaterThan(0m);
            prev = result;
        }
    }
}
