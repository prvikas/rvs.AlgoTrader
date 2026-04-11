using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using rvs.AlgoTrader.Backtesting.Engine;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Strategies.CalendarSpread;
using rvs.AlgoTrader.Strategies.IntradayPcrOptions;
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

    // ── FilteredAroundAtm ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds a chain with ATM legs + far-OTM legs (1000 pts away) to verify filtering.
    /// </summary>
    private static OptionChainSnapshot MakeChainWithFarOtm(decimal spot = 22000m, decimal atmIv = 20m, decimal farIv = 40m)
    {
        var atm     = Math.Round(spot / 50m) * 50m;
        var farCall = atm + 1000m;   // 20 strikes OTM
        var farPut  = atm - 1000m;
        var legs = new List<OptionLeg>
        {
            new(atm,     "CE", 100m, 100_000L, 0L, 10_000L, atmIv, 99m,  101m,  0.50m),
            new(atm,     "PE", 100m, 100_000L, 0L, 10_000L, atmIv, 99m,  101m, -0.50m),
            new(farCall, "CE",  10m, 500_000L, 0L, 50_000L, farIv,  9m,   11m,  0.05m),
            new(farPut,  "PE",  10m, 500_000L, 0L, 50_000L, farIv,  9m,   11m, -0.05m),
        };
        return new OptionChainSnapshot("NIFTY50", Now, spot, new LocalDate(2024, 6, 6), legs);
    }

    [Fact]
    public void FilteredAroundAtm_RemovesFarOtmLegs()
    {
        // Far legs are 1000 pts away. With strikeInterval=50, nStrikes=5 → radius = 250 pts.
        var chain    = MakeChainWithFarOtm(spot: 22000m);
        var filtered = chain.FilteredAroundAtm(strikeInterval: 50m, nStrikes: 5);

        filtered.Options.Should().HaveCount(2, "only ATM CE + PE within 250 pts should survive");
        filtered.Options.Should().AllSatisfy(o =>
            Math.Abs(o.StrikePrice - chain.SpotPrice).Should().BeLessOrEqualTo(250m));
    }

    [Fact]
    public void FilteredAroundAtm_PreservesSpotAndExpiry()
    {
        var chain    = MakeChainWithFarOtm(spot: 22000m);
        var filtered = chain.FilteredAroundAtm(50m, 5);

        filtered.SpotPrice.Should().Be(chain.SpotPrice);
        filtered.Expiry.Should().Be(chain.Expiry);
        filtered.UnderlyingSymbol.Should().Be(chain.UnderlyingSymbol);
    }

    [Fact]
    public void FilteredAroundAtm_AtmIv_ReflectsNearStrikesOnly()
    {
        // Far legs have IV=40; ATM legs have IV=20.
        // After filtering, AtmIv should be 20 (not skewed by far legs).
        var chain    = MakeChainWithFarOtm(spot: 22000m, atmIv: 20m, farIv: 40m);
        var filtered = chain.FilteredAroundAtm(50m, 5);

        filtered.AtmIv.Should().Be(20m, "far-OTM legs with IV=40 should be excluded");
    }

    [Fact]
    public void FilteredAroundAtm_PcrReflectsNearStrikesOnly()
    {
        // ATM CE OI = ATM PE OI = 10000 → PCR(OI) = 1.0
        // Far legs have OI = 50000 each; if included, PCR would be different.
        // After filtering only ATM legs remain → PCR(OI) = 1.0.
        var chain    = MakeChainWithFarOtm(spot: 22000m);
        var filtered = chain.FilteredAroundAtm(50m, 5);

        filtered.PutCallRatioOI.Should().Be(1.0m, "equal ATM put/call OI should yield PCR=1");
    }

    [Fact]
    public void FilteredAroundAtm_ZeroNStrikes_ReturnsUnfiltered()
    {
        var chain    = MakeChainWithFarOtm();
        var filtered = chain.FilteredAroundAtm(50m, nStrikes: 0);

        filtered.Options.Should().HaveCount(chain.Options.Count, "nStrikes=0 disables filter");
    }

    [Fact]
    public void FilteredAroundAtm_ZeroStrikeInterval_ReturnsUnfiltered()
    {
        var chain    = MakeChainWithFarOtm();
        var filtered = chain.FilteredAroundAtm(strikeInterval: 0m, nStrikes: 5);

        filtered.Options.Should().HaveCount(chain.Options.Count, "strikeInterval=0 disables filter");
    }

    [Fact]
    public void FilteredAroundAtm_LargeNStrikes_IncludesAllLegs()
    {
        // nStrikes=30 → radius = 30×50 = 1500 pts; far legs are 1000 pts away → included.
        var chain    = MakeChainWithFarOtm(spot: 22000m);
        var filtered = chain.FilteredAroundAtm(50m, nStrikes: 30);

        filtered.Options.Should().HaveCount(4, "all 4 legs are within 1500 pts radius");
    }

    // ── IntradayPcrOptions uses filtered PCR ─────────────────────────────────

    [Fact]
    public async Task IntradayPcrOptions_FarOtmLegs_DoNotDistortPcr()
    {
        // Build a chain where far-OTM puts dominate the OI (→ PCR >> 1 = bullish if unfiltered),
        // but near-ATM OI is balanced (PCR = 1 → neutral zone, no signal).
        // With ChainStrikeDepth=5 (250 pts radius), far OTM is excluded → neutral → Hold.
        var spot = 22000m;
        var atm  = 22000m;
        var legs = new List<OptionLeg>
        {
            // Near-ATM: balanced OI
            new(atm, "CE", 100m, 50_000L,  0L, 10_000L, 20m, 99m, 101m,  0.50m),
            new(atm, "PE", 100m, 50_000L,  0L, 10_000L, 20m, 99m, 101m, -0.50m),
            // Far-OTM puts: huge OI that would push PCR above PcrUpperThreshold (1.2) if included
            new(atm - 1000m, "PE", 5m, 2_000_000L, 0L, 200_000L, 40m, 4m, 6m, -0.02m),
        };
        var chain   = new OptionChainSnapshot("NIFTY50", Now, spot, new LocalDate(2024, 6, 6), legs);
        var candles = MakeCandles(10, 22000m);
        // Candle close = 22000, VWAP ≈ 22000 → within 0.5% tolerance

        var ctx = new StrategyContext(
            Guid.NewGuid(), "NIFTY50", "5m", candles, "{}", "test",
            OptionChain: chain);

        // Config with tight strike depth (only ±5 strikes = ±250 pts) to exclude far puts
        var config = new IntradayPcrOptionsConfig
        {
            ChainStrikeDepth  = 5,
            StrikeInterval    = 50m,
            PcrUpperThreshold = 1.2m,
            PcrLowerThreshold = 0.8m,
            ObserveStartHour  = 9, ObserveStartMinute  = 15,
            ObserveEndHour    = 9, ObserveEndMinute    = 14,  // window ends before candle time
            GapThresholdPts   = 99999m,   // no gap trigger
        };
        var strategy = new IntradayPcrOptionsStrategy(config);

        var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

        // With far-OTM excluded, near-ATM PCR = 1.0 → neutral zone [0.8, 1.2] → Hold
        result.Signal.Should().Be(SignalType.Hold);
        result.Reason.Should().Contain("neutral zone",
            "balanced near-ATM OI gives PCR=1.0 which is in the neutral band");
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

    // ── BuildSyntheticLegs (BT-OPT-1 critical fix) ───────────────────────────

    [Fact]
    public void BuildSyntheticLegs_BullishBar_PcrBelowNeutralZone()
    {
        // Bullish prev bar → CE OI inflated, PE OI compressed → PCR < 0.8
        var legs  = BacktestEngine.BuildSyntheticLegs(22000m, 50m, 18m, prevBarBullish: true, nStrikes: 5);
        var chain = new OptionChainSnapshot("NIFTY50", Now, 22000m, new LocalDate(2024, 6, 6), legs);

        chain.PutCallRatioOI.Should().BeLessThan(0.8m,
            "bullish prior bar should produce CE-heavy OI pyramid (PCR < 0.8)");
    }

    [Fact]
    public void BuildSyntheticLegs_BearishBar_PcrAboveNeutralZone()
    {
        // Bearish prev bar → PE OI inflated, CE OI compressed → PCR > 1.2
        var legs  = BacktestEngine.BuildSyntheticLegs(22000m, 50m, 18m, prevBarBullish: false, nStrikes: 5);
        var chain = new OptionChainSnapshot("NIFTY50", Now, 22000m, new LocalDate(2024, 6, 6), legs);

        chain.PutCallRatioOI.Should().BeGreaterThan(1.2m,
            "bearish prior bar should produce PE-heavy OI pyramid (PCR > 1.2)");
    }

    [Fact]
    public void BuildSyntheticLegs_ProducesCorrectLegCount()
    {
        // nStrikes=5 → ATM (1 CE + 1 PE) + 5 OTM CE + 5 OTM PE = 12 legs total
        var legs = BacktestEngine.BuildSyntheticLegs(22000m, 50m, 18m, prevBarBullish: true, nStrikes: 5);

        legs.Should().HaveCount(12, "ATM + 5 OTM on each side = 12 legs");
        legs.Count(l => l.OptionType == "CE").Should().Be(6);
        legs.Count(l => l.OptionType == "PE").Should().Be(6);
    }

    [Fact]
    public void BuildSyntheticLegs_AtmStrikeLegs_HaveExactlyRequestedIv()
    {
        const decimal atmIv = 22m;
        var legs = BacktestEngine.BuildSyntheticLegs(22000m, 50m, atmIv, prevBarBullish: true, nStrikes: 5);

        // n=0 legs (ATM strike) have no IV skew applied — both CE and PE should equal atmIv exactly
        var atmCe = legs.First(l => l.OptionType == "CE" && l.StrikePrice == 22000m);
        var atmPe = legs.First(l => l.OptionType == "PE" && l.StrikePrice == 22000m);

        atmCe.ImpliedVolatility.Should().Be(atmIv, "no CE discount applied at n=0 (ATM)");
        atmPe.ImpliedVolatility.Should().Be(atmIv, "no PE skew applied at n=0 (ATM)");
    }

    [Fact]
    public void BuildSyntheticLegs_OiDecaysAwayFromAtm()
    {
        var legs = BacktestEngine.BuildSyntheticLegs(22000m, 50m, 18m, prevBarBullish: true, nStrikes: 5);

        // ATM CE has the highest OI; each successive OTM CE strike should have strictly less OI
        var ceLegs = legs.Where(l => l.OptionType == "CE")
                         .OrderBy(l => l.StrikePrice)   // ascending: ATM first (22000, 22050, …)
                         .ToList();
        for (int k = 0; k < ceLegs.Count - 1; k++)
            ceLegs[k].OpenInterest.Should().BeGreaterThan(ceLegs[k + 1].OpenInterest,
                $"CE OI at strike {ceLegs[k].StrikePrice} should exceed OI at {ceLegs[k + 1].StrikePrice}");
    }

    [Fact]
    public void BuildSyntheticLegs_PutIvSkew_OtmPutsMoreExpensiveThanAtm()
    {
        var legs = BacktestEngine.BuildSyntheticLegs(22000m, 50m, 18m, prevBarBullish: false, nStrikes: 5);

        var atmPe = legs.First(l => l.OptionType == "PE" && l.StrikePrice == 22000m);
        var otmPe = legs.First(l => l.OptionType == "PE" && l.StrikePrice == 22000m - 50m);  // 1 strike OTM

        otmPe.ImpliedVolatility.Should().BeGreaterThan(atmPe.ImpliedVolatility,
            "OTM puts carry positive IV skew (+0.5%/strike) in Indian index options");
    }
}
