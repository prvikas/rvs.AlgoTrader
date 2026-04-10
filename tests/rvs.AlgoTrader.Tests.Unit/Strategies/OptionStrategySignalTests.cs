using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using rvs.AlgoTrader.Backtesting.Engine;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Strategies.CalendarSpread;
using rvs.AlgoTrader.Strategies.IronCondor;
using rvs.AlgoTrader.Strategies.ShortStraddleStrangle;
using Xunit;
using FluentAssertions;

namespace rvs.AlgoTrader.Tests.Unit.Strategies;

/// <summary>
/// Tests that option strategies:
/// 1. Populate SpreadSignalResult.SpotPrice and NearExpiryDate from the option chain.
/// 2. Skip / Hold correctly when option chain is absent.
/// 3. BacktestEngine helpers (expiry, ExtractSpreadConfig) behave correctly.
/// </summary>
public class OptionStrategySignalTests
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
    private static readonly Instant Now = Instant.FromUtc(2024, 6, 3, 4, 0); // 09:30 IST

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClosedCandle MakeCandle(decimal price = 22000m, int dayOffset = 0)
    {
        var d     = new LocalDate(2024, 6, 3).PlusDays(dayOffset);
        var open  = new LocalDateTime(d.Year, d.Month, d.Day, 9, 15, 0).InZoneLeniently(Ist);
        var close = open.Plus(Duration.FromHours(6) + Duration.FromMinutes(30));
        return new ClosedCandle("NIFTY50", "1D", open, close, price, price + 50, price - 50, price, 1_000_000);
    }

    // Vary prices slightly so Bollinger Bands have non-zero width (IronCondor range-bound check).
    // Oscillate ±50 around base so the last candle (at base) is inside the bands.
    private static IReadOnlyList<ClosedCandle> MakeCandles(int count = 60, decimal price = 22000m)
        => Enumerable.Range(0, count)
            .Select(i => MakeCandle(price + (i % 5 == 0 ? 40m : i % 3 == 0 ? -40m : 0m), i))
            .ToList();

    private static OptionChainSnapshot MakeChain(
        decimal spot = 22000m, decimal atmIv = 20m, LocalDate? expiry = null)
    {
        var exp = expiry ?? new LocalDate(2024, 6, 6); // nearest Thursday
        // AtmIv is computed as average IV of Options within 200 points of SpotPrice.
        // Place one CE + one PE at the ATM strike so AtmIv returns the desired value.
        var atm = Math.Round(spot / 50m) * 50m; // round to nearest 50
        var legs = new List<OptionLeg>
        {
            new(atm, "CE", 100m, 50000, 0, 10000, atmIv, 99m, 101m, 0.50m),
            new(atm, "PE", 100m, 50000, 0, 10000, atmIv, 99m, 101m, -0.50m),
        };
        return new OptionChainSnapshot("NIFTY50", Now, spot, exp, legs);
    }

    private static StrategyContext MakeContext(
        OptionChainSnapshot? chain = null,
        OptionChainSnapshot? nearChain = null,
        OptionChainSnapshot? farChain  = null,
        int candleCount = 60)
    {
        return new StrategyContext(
            StrategyInstanceId: Guid.NewGuid(),
            InternalSymbol:     "NIFTY50",
            Timeframe:          "1D",
            Candles:            MakeCandles(candleCount),
            ConfigJson:         "{}",
            CorrelationId:      "test",
            OptionChain:        chain,
            NearExpiryChain:    nearChain,
            FarExpiryChain:     farChain);
    }

    // ── IronCondor ────────────────────────────────────────────────────────────

    [Fact]
    public async Task IronCondor_WithChain_SpreadSignal_HasSpotAndExpiry()
    {
        var expiry   = new LocalDate(2024, 6, 6);
        var chain    = MakeChain(spot: 22000m, atmIv: 18m, expiry: expiry);
        var context  = MakeContext(chain: chain);
        var strategy = new IronCondorStrategy(new IronCondorConfig { MinAtmIv = 12m, MaxAtmIv = 35m });

        var result = await strategy.EvaluateAsync(context, CancellationToken.None);

        result.Spread.Should().NotBeNull("IronCondor with valid IV should emit a spread signal");
        result.Spread!.SpotPrice.Should().Be(22000m);
        result.Spread.NearExpiryDate.Should().Be(expiry);
        result.Spread.Legs.Should().HaveCount(4, "Iron Condor has 4 legs");
    }

    [Fact]
    public async Task IronCondor_WithoutChain_ReturnsSkip()
    {
        var context  = MakeContext(chain: null);
        var strategy = new IronCondorStrategy(new IronCondorConfig());

        var result = await strategy.EvaluateAsync(context, CancellationToken.None);

        result.Signal.Should().Be(SignalType.Hold);
        result.SkippedReason.Should().NotBeNull();
    }

    [Fact]
    public async Task IronCondor_LowIv_ReturnsHold()
    {
        var chain    = MakeChain(atmIv: 5m);
        var context  = MakeContext(chain: chain);
        var strategy = new IronCondorStrategy(new IronCondorConfig { MinAtmIv = 12m });

        var result = await strategy.EvaluateAsync(context, CancellationToken.None);

        result.Signal.Should().Be(SignalType.Hold);
        result.Spread.Should().BeNull();
    }

    // ── ShortStraddle ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ShortStraddle_WithChain_SpreadSignal_HasSpotAndExpiry()
    {
        var expiry   = new LocalDate(2024, 6, 6);
        var chain    = MakeChain(spot: 22000m, atmIv: 20m, expiry: expiry);
        var context  = MakeContext(chain: chain);
        var config   = new ShortStraddleStrangleConfig
        {
            StrategyType = ShortStraddleStrangleType.Straddle,
            MinAtmIv     = 18m,
            MinDte       = 0,
            MaxDte       = 0
        };
        var strategy = new ShortStraddleStrangleStrategy(config);

        var result = await strategy.EvaluateAsync(context, CancellationToken.None);

        result.Spread.Should().NotBeNull();
        result.Spread!.SpotPrice.Should().Be(22000m);
        result.Spread.NearExpiryDate.Should().Be(expiry);
        result.Spread.Legs.Should().HaveCount(2);
        result.Spread.Legs.Should().AllSatisfy(l =>
            l.Direction.Should().Be(OrderDirection.Sell, "short straddle sells all legs"));
    }

    [Fact]
    public async Task ShortStraddle_WithoutChain_ReturnsSkip()
    {
        var context  = MakeContext(chain: null);
        var strategy = new ShortStraddleStrangleStrategy(new ShortStraddleStrangleConfig());

        var result = await strategy.EvaluateAsync(context, CancellationToken.None);

        result.Signal.Should().Be(SignalType.Hold);
        result.SkippedReason.Should().NotBeNull();
    }

    // ── CalendarSpread ────────────────────────────────────────────────────────

    [Fact]
    public async Task CalendarSpread_WithDualChain_SpreadSignal_HasSpotAndNearExpiry()
    {
        var nearExpiry = new LocalDate(2024, 6, 6);
        var farExpiry  = new LocalDate(2024, 6, 27);
        var nearChain  = MakeChain(spot: 22000m, atmIv: 20m, expiry: nearExpiry);
        var farChain   = MakeChain(spot: 22000m, atmIv: 16m, expiry: farExpiry); // slope=4 >= MinIvSlope=2
        var context    = MakeContext(chain: nearChain, nearChain: nearChain, farChain: farChain);
        var config     = new CalendarSpreadConfig { MinAtmIv = 8m, MaxAtmIv = 25m, MinIvSlope = 2m };
        var strategy   = new CalendarSpreadStrategy(config);

        var result = await strategy.EvaluateAsync(context, CancellationToken.None);

        result.Spread.Should().NotBeNull("IV=20, slope=4 satisfies all filters");
        result.Spread!.SpotPrice.Should().Be(nearChain.SpotPrice);
        result.Spread.NearExpiryDate.Should().Be(nearExpiry);
        result.Spread.Legs.Should().HaveCount(2);
        result.Spread.Legs.Should().ContainSingle(l => l.Direction == OrderDirection.Sell && l.NearestWeekly);
        result.Spread.Legs.Should().ContainSingle(l => l.Direction == OrderDirection.Buy  && !l.NearestWeekly);
    }

    [Fact]
    public async Task CalendarSpread_InsufficientIvSlope_ReturnsHold()
    {
        var nearChain = MakeChain(atmIv: 20m);
        var farChain  = MakeChain(atmIv: 19m); // slope=1 < MinIvSlope=2
        var context   = MakeContext(chain: nearChain, nearChain: nearChain, farChain: farChain);
        var config    = new CalendarSpreadConfig { MinIvSlope = 2m };
        var strategy  = new CalendarSpreadStrategy(config);

        var result = await strategy.EvaluateAsync(context, CancellationToken.None);

        result.Signal.Should().Be(SignalType.Hold);
        result.Spread.Should().BeNull();
    }

    // ── BacktestEngine expiry helpers ─────────────────────────────────────────

    [Theory]
    [InlineData(2024, 6,  3, 2024, 6,  6)]  // Mon → Thu same week
    [InlineData(2024, 6,  6, 2024, 6, 13)]  // Thu → next Thu
    [InlineData(2024, 6,  7, 2024, 6, 13)]  // Fri → next Thu
    public void NearestWeeklyExpiry_AlwaysNextThursday(
        int fy, int fm, int fd, int ey, int em, int ed)
    {
        var from   = new LocalDate(fy, fm, fd);
        var result = BacktestEngine.NearestWeeklyExpiry(from);
        result.Should().Be(new LocalDate(ey, em, ed));
        result.DayOfWeek.Should().Be(IsoDayOfWeek.Thursday);
    }

    // NearestMonthlyExpiry always returns the last Thursday of (from.Month + 1).
    // "next month" = PlusMonths(1) relative to the bar date.
    [Theory]
    [InlineData(2024, 6,  3, 2024, 7, 25)]  // June 3  → last Thu of July
    [InlineData(2024, 6, 28, 2024, 7, 25)]  // June 28 → last Thu of July
    [InlineData(2024, 7,  1, 2024, 8, 29)]  // July 1  → last Thu of August
    public void NearestMonthlyExpiry_LastThursdayOfNextMonth(
        int fy, int fm, int fd, int ey, int em, int ed)
    {
        var from   = new LocalDate(fy, fm, fd);
        var result = BacktestEngine.NearestMonthlyExpiry(from);
        result.Should().Be(new LocalDate(ey, em, ed));
        result.DayOfWeek.Should().Be(IsoDayOfWeek.Thursday);
    }

    // ── ExtractSpreadConfig ────────────────────────────────────────────────────

    [Fact]
    public void ExtractSpreadConfig_DefaultsWhenNull()
    {
        var (maxLoss, profit, si) = BacktestEngine.ExtractSpreadConfig(null);
        maxLoss.Should().Be(2.0m);
        profit.Should().Be(0.50m);
        si.Should().Be(50m);
    }

    [Fact]
    public void ExtractSpreadConfig_ParsesExplicitValues()
    {
        const string json = """{"MaxLossMultiple":3,"ProfitTargetPct":0.4,"StrikeInterval":100}""";
        var (maxLoss, profit, si) = BacktestEngine.ExtractSpreadConfig(json);
        maxLoss.Should().Be(3m);
        profit.Should().Be(0.4m);
        si.Should().Be(100m);
    }

    [Fact]
    public void ExtractSpreadConfig_NormalisesWholePercentage()
    {
        const string json = """{"ProfitTargetPct":50}""";
        var (_, profit, _) = BacktestEngine.ExtractSpreadConfig(json);
        profit.Should().Be(0.50m, "values > 1 are divided by 100");
    }

    [Fact]
    public void ExtractSpreadConfig_FallsBackToVegaProfitTargetPct()
    {
        const string json = """{"VegaProfitTargetPct":0.6}""";
        var (_, profit, _) = BacktestEngine.ExtractSpreadConfig(json);
        profit.Should().Be(0.6m);
    }

    // ── SpreadSignalResult diagnostics ────────────────────────────────────────

    [Fact]
    public async Task IronCondor_DiagnosticsJson_ContainsAtmIv()
    {
        var chain    = MakeChain(spot: 22000m, atmIv: 18m);
        var context  = MakeContext(chain: chain);
        var strategy = new IronCondorStrategy(new IronCondorConfig { MinAtmIv = 12m });

        var result = await strategy.EvaluateAsync(context, CancellationToken.None);

        result.Spread!.DiagnosticsJson.Should().BeOfType<Dictionary<string, decimal>>();
        var diag = (Dictionary<string, decimal>)result.Spread.DiagnosticsJson!;
        diag.Should().ContainKey("atmIv");
        diag["atmIv"].Should().Be(18m);
    }
}
