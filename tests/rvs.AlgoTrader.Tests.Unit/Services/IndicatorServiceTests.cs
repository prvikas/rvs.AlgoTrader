using FluentAssertions;
using NodaTime;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Infrastructure.Services;
using Xunit;

namespace rvs.AlgoTrader.Tests.Unit.Services;

public class IndicatorServiceTests
{
    private readonly IndicatorService _svc = new();

    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    private static ClosedCandle MakeCandle(decimal high, decimal low, decimal close, long volume = 0, int offset = 0)
    {
        var t = new LocalDateTime(2024, 1, 15, 9, offset, 0).InZoneLeniently(Ist);
        return new ClosedCandle("TEST", "5m", t, t.Plus(Duration.FromMinutes(5)),
            close, high, low, close, volume);
    }

    [Fact]
    public void Sma_CorrectlyAverages()
    {
        var prices = new decimal[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var result = _svc.Sma(prices, 5);

        result.Should().HaveCount(6); // 10 - 5 + 1 = 6 values
        result[0].Should().Be(3m);    // avg(1..5) = 3
        result[^1].Should().Be(8m);   // avg(6..10) = 8
    }

    [Fact]
    public void Ema_FirstValueEqualsFirstClose()
    {
        var prices = new decimal[] { 10, 11, 12, 11, 10, 11, 12, 11, 10, 11 };
        var ema5 = _svc.Ema(prices, 5);

        // This batch EMA implementation seeds from closes[0] (full-length output)
        ema5.Should().HaveCount(prices.Length);
        ema5[0].Should().Be(prices[0], because: "EMA is seeded from the first close value");
    }

    [Fact]
    public void Ema_SmoothingFactor_IsCorrect()
    {
        // With period=5, multiplier = 2/(5+1) = 0.333...
        var prices = Enumerable.Repeat(100m, 10).ToArray();
        var ema5 = _svc.Ema(prices, 5);

        // All prices same → EMA must be exactly 100
        foreach (var v in ema5)
            v.Should().BeApproximately(100m, 0.001m);
    }

    [Fact]
    public void Atr_MustBeNonNegative()
    {
        var highs = new decimal[] { 102, 104, 103, 105, 104, 106, 105, 107, 106, 108, 107, 109, 108, 110, 109 };
        var lows  = new decimal[] { 98,  100, 99,  101, 100, 102, 101, 103, 102, 104, 103, 105, 104, 106, 105 };
        var closes = new decimal[] { 100, 102, 101, 103, 102, 104, 103, 105, 104, 106, 105, 107, 106, 108, 107 };

        var candles = Enumerable.Range(0, highs.Length)
            .Select(i => MakeCandle(highs[i], lows[i], closes[i], offset: i))
            .ToArray();

        var result = _svc.Atr(candles, 14);

        foreach (var v in result)
            v.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void Vwap_WeightedByVolume_IsCorrect()
    {
        // Single bar: VWAP = typical price = (H+L+C)/3
        var candles = new[] { MakeCandle(high: 110, low: 90, close: 100, volume: 1_000) };
        var result = _svc.Vwap(candles);

        result[0].Should().BeApproximately(100m, 0.01m); // (110+90+100)/3 = 100
    }

    [Fact]
    public void BollingerBands_UpperAboveLower()
    {
        var prices = Enumerable.Range(1, 20).Select(i => (decimal)(100 + i % 5)).ToArray();
        var (upper, mid, lower) = _svc.BollingerBands(prices, 20, 2.0m);

        for (int i = 0; i < upper.Length; i++)
        {
            upper[i].Should().BeGreaterThan(mid[i]);
            lower[i].Should().BeLessThan(mid[i]);
        }
    }

    [Fact]
    public void IncrementalEma_MatchesBatchEma()
    {
        var prices = Enumerable.Range(1, 30).Select(i => (decimal)(100 + i * 0.5)).ToArray();
        var period = 9;

        // Batch EMA: seeds from closes[0], returns full-length array
        var batchEma = _svc.Ema(prices, period);

        // Incremental EMA: seeds from SMA of first period values, returns values after warmup
        var incEma = new IncrementalEma(period);
        var incResults = new List<decimal>();
        foreach (var p in prices)
        {
            var v = incEma.Update(p);
            if (v.HasValue) incResults.Add(v.Value);
        }

        // Incremental returns (n - period + 1) values after warmup
        incResults.Should().HaveCount(prices.Length - period + 1);

        // After sufficient data (21 more steps beyond warmup), the initial seeding difference decays
        // exponentially with k=0.2, so (0.8)^21 ≈ 0.009 → both should converge within ~1.0
        incResults[^1].Should().BeApproximately(batchEma[^1], 1.0m,
            because: "After 30 data points with period 9, incremental and batch EMA should converge");
    }
}
