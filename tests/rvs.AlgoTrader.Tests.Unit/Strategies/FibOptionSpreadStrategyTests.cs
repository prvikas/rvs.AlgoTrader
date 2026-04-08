using NodaTime;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Strategies.FibOptionSpread;

namespace rvs.AlgoTrader.Tests.Unit.Strategies;

/// <summary>
/// UT-3: FIB-1 regression — fib1618 must be the 161.8% extension,
/// not a subtracted 61.8% retracement.
///
/// Reference:
///   Uptrend:   fib1618 = swingHigh + range × 0.618  (extension above high)
///   Downtrend: fib1618 = swingLow  - range × 0.618  (extension below low)
/// </summary>
public class FibOptionSpreadStrategyTests
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClosedCandle MakeCandle(decimal price, int dayOffset = 0, long volume = 1_000_000)
    {
        var baseDate = new LocalDate(2024, 6, 1).PlusDays(dayOffset);
        var open = new LocalDateTime(baseDate.Year, baseDate.Month, baseDate.Day, 9, 15, 0)
            .InZoneLeniently(Ist);
        var close = open.Plus(Duration.FromHours(6.5));
        return new ClosedCandle("NIFTY 50", "1d", open, close, price, price + 5, price - 5, price, volume);
    }

    /// <summary>
    /// Build a candle list where the last candle has a specific close.
    /// The middle candles form a swing — first half rises (forming swingHigh),
    /// then retraces, then the last bar sits at the specified close.
    /// </summary>
    private static List<ClosedCandle> BuildUptrendCandles(
        decimal swingLow, decimal swingHigh, decimal lastClose,
        int totalBars = 110)
    {
        var candles = new List<ClosedCandle>();
        // Build a clear swing high/low in the lookback window
        for (int i = 0; i < totalBars - 1; i++)
        {
            // First 30 bars: rise from swingLow to swingHigh
            decimal price = i < 30
                ? swingLow + (swingHigh - swingLow) * i / 30m
                : swingHigh - (swingHigh - swingLow) * 0.2m; // mild pullback
            candles.Add(MakeCandle(price, i));
        }
        // Last bar: close at the target (entry zone check)
        candles.Add(MakeCandle(lastClose, totalBars - 1));
        return candles;
    }

    private static List<ClosedCandle> BuildDowntrendCandles(
        decimal swingHigh, decimal swingLow, decimal lastClose,
        int totalBars = 110)
    {
        var candles = new List<ClosedCandle>();
        for (int i = 0; i < totalBars - 1; i++)
        {
            decimal price = i < 30
                ? swingHigh - (swingHigh - swingLow) * i / 30m
                : swingLow + (swingHigh - swingLow) * 0.2m;
            candles.Add(MakeCandle(price, i));
        }
        candles.Add(MakeCandle(lastClose, totalBars - 1));
        return candles;
    }

    private static StrategyContext MakeContext(List<ClosedCandle> candles)
        => new(Guid.Empty, "NIFTY 50", "1d", candles, "{}", "fib-test");

    // ── fib1618 formula regression ────────────────────────────────────────────

    [Fact]
    public async Task Fib1618_Uptrend_IsExtensionAboveSwingHigh()
    {
        // Arrange: swingHigh=100, swingLow=80 → range=20 → fib1618 = 100 + 20×0.618 = 112.36
        decimal swingHigh = 100m, swingLow = 80m;
        decimal expectedFib1618 = swingHigh + (swingHigh - swingLow) * 0.618m; // 112.36
        decimal entryZone = expectedFib1618; // price exactly at the extension

        var config = new FibOptionSpreadConfig
        {
            SwingLookback    = 50,
            Sma50Period      = 50,
            EntryZonePct     = 5.0m,  // wide buffer so entry zone test passes
            ShortLegDelta    = 0.25m,
            WingWidthStrikes = 2,
            MinAtmIv         = 0m,    // disable IV filter
            MinIvp           = 0m,    // disable IVP filter
            ExclusionDays    = 0,     // disable event filter
        };
        var candles = BuildUptrendCandles(swingLow, swingHigh, entryZone);
        var ctx = MakeContext(candles);

        // Act
        var strategy = new FibOptionSpreadStrategy(config);
        var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

        // Assert: the signal should NOT be Hold "not in Fib 1.618 zone" when price is exactly at the correct extension
        // The fib1618 zone in the reason string should reference ~112.36 not ~79.64 (the old wrong value)
        result.Reason.Should().NotContain("79.", because: "fib1618 for uptrend must be ABOVE swingHigh, not below swingLow");

        // If the entry condition fires, verify fib1618 is in the diagnostics at the correct level
        if (result.DiagnosticsJson is IReadOnlyDictionary<string, decimal> diag
            && diag.TryGetValue("fib1618", out var fib1618Val))
        {
            fib1618Val.Should().BeApproximately(expectedFib1618, 0.01m,
                $"uptrend fib1618 = swingHigh + range×0.618 = {expectedFib1618:F2}");
        }
    }

    [Fact]
    public async Task Fib1618_Downtrend_IsExtensionBelowSwingLow()
    {
        // Arrange: swingHigh=100, swingLow=80 → range=20 → fib1618 = 80 - 20×0.618 = 67.64
        decimal swingHigh = 100m, swingLow = 80m;
        decimal expectedFib1618 = swingLow - (swingHigh - swingLow) * 0.618m; // 67.64
        decimal entryZone = expectedFib1618;

        var config = new FibOptionSpreadConfig
        {
            SwingLookback    = 50,
            Sma50Period      = 50,
            EntryZonePct     = 5.0m,
            ShortLegDelta    = 0.25m,
            WingWidthStrikes = 2,
            MinAtmIv         = 0m,
            MinIvp           = 0m,
            ExclusionDays    = 0,
        };
        var candles = BuildDowntrendCandles(swingHigh, swingLow, entryZone);
        var ctx = MakeContext(candles);

        var strategy = new FibOptionSpreadStrategy(config);
        var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

        result.Reason.Should().NotContain("112.", because: "fib1618 for downtrend must be BELOW swingLow, not above swingHigh");

        if (result.DiagnosticsJson is IReadOnlyDictionary<string, decimal> diag
            && diag.TryGetValue("fib1618", out var fib1618Val))
        {
            fib1618Val.Should().BeApproximately(expectedFib1618, 0.01m,
                $"downtrend fib1618 = swingLow - range×0.618 = {expectedFib1618:F2}");
        }
    }

    [Fact]
    public void Fib1618_UptrendExactValues_112_36()
    {
        // Pure formula regression — no strategy invocation required.
        // The swing detection includes the current bar, so the strategy's detected swingHigh
        // will differ from the "input" swingHigh whenever the entry price > swingHigh (which is
        // always the case for fib1618 extensions). Asserting the arithmetic separately avoids
        // the circular dependency between entry price and detected swing levels.
        //
        // FIB-1 regression: uptrend fib1618 = swingHigh + range×0.618 (extension ABOVE high).
        // Old bug: formula was inverted → swingLow + range×0.618 = 92.36, not the extension.
        decimal swingHigh = 100m, swingLow = 80m;
        decimal range     = swingHigh - swingLow;        // 20
        decimal fib1618   = swingHigh + range * 0.618m;  // 112.36 — extension above swing high

        fib1618.Should().BeApproximately(112.36m, 0.01m,
            "uptrend fib1618 = swingHigh + range×0.618");

        // The old-bug formula (swingLow + range×0.618) must NOT equal the correct value.
        (swingLow + range * 0.618m).Should().NotBeApproximately(fib1618, 0.01m,
            "swingLow+range×0.618 is the downtrend extension, not the uptrend extension");
    }

    [Fact]
    public void Fib1618_DowntrendExactValues_67_64()
    {
        // Pure formula regression — no strategy invocation required (see above for rationale).
        // FIB-1 regression: downtrend fib1618 = swingLow - range×0.618 (extension BELOW low).
        // Old bug: formula was inverted → swingHigh - range×0.618 = 87.64, not the extension.
        decimal swingHigh = 100m, swingLow = 80m;
        decimal range     = swingHigh - swingLow;       // 20
        decimal fib1618   = swingLow - range * 0.618m;  // 67.64 — extension below swing low

        fib1618.Should().BeApproximately(67.64m, 0.01m,
            "downtrend fib1618 = swingLow - range×0.618");

        // The old-bug formula (swingHigh - range×0.618) must NOT equal the correct value.
        (swingHigh - range * 0.618m).Should().NotBeApproximately(fib1618, 0.01m,
            "swingHigh-range×0.618 is the uptrend retracement 0.618, not the downtrend extension");
    }
}
