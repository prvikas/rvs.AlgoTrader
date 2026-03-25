using FluentAssertions;
using NodaTime;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Strategies.EmaVwapMomentum;
using Xunit;

namespace rvs.AlgoTrader.UnitTests.Strategies;

/// <summary>
/// Unit tests for EmaVwapMomentumStrategy.
///
/// Testing approach:
/// - Build synthetic candle sequences that deterministically trigger or suppress each condition.
/// - Use a fixed candle baseline (price=100, vol=1000) then create breakout bars.
/// - All candles are closed — never partial bar (Rule #3).
/// - Tests named: MethodName_Scenario_ExpectedResult
/// </summary>
public class EmaVwapMomentumStrategyTests
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    // ── Test helpers ──────────────────────────────────────────────────────

    private static EmaVwapMomentumConfig DefaultConfig(
        bool allowShort    = false,
        bool useOptionChain = false,
        decimal rrr        = 2.0m) =>
        new()
        {
            FastEmaPeriod   = 5,
            SlowEmaPeriod   = 10,
            BbPeriod        = 10,
            BbStdDev        = 2.0m,
            AtrPeriod       = 5,
            AtrStopMultiple = 1.5m,
            MinAtrPct       = 0.1m,     // very low threshold so tests don't get blocked by ATR filter
            VolumeMultiple  = 1.5m,
            RiskRewardRatio = rrr,
            AllowShort      = allowShort,
            UseOptionChain  = useOptionChain
        };

    private static ClosedCandle Bar(
        decimal close,
        long volume    = 1_000L,
        decimal? open  = null,
        decimal? high  = null,
        decimal? low   = null,
        int dayOffset  = 0,
        int minuteOffset = 0)
    {
        var ts = new LocalDateTime(2024, 1, 15, 9, 20, 0)
            .PlusMinutes(minuteOffset)
            .PlusDays(dayOffset)
            .InZoneStrictly(Ist);

        return new ClosedCandle(
            InternalSymbol: "NSE:NIFTY50",
            Timeframe:      "5m",
            OpenTime:       ts,
            CloseTime:      ts.Plus(Duration.FromMinutes(5)),
            Open:           open  ?? close,
            High:           high  ?? close + 2,
            Low:            low   ?? close - 2,
            Close:          close,
            Volume:         volume
        );
    }

    /// <summary>
    /// Builds a candle list designed to trigger a BUY signal on the last bar.
    /// Approach:
    ///   - warmup bars: flat price=100, vol=1000 (slow EMA seeds here)
    ///   - rising bars: price climbs from 100 → 115 to create fast EMA crossover
    ///   - final bar: closes at 118 with high volume (confirmation)
    ///
    /// VWAP will be close to the average of all bars. Since the final bars trend up,
    /// VWAP ≈ avg(100..118) ≈ 109. Close=118 > VWAP ✓
    /// BB midline after rising bars ≈ 110-112. Close=118 > midline ✓
    /// EMA(5) fast > EMA(10) slow after sustained rise ✓
    /// Volume on last bar = highVol > 1.5× avg ✓
    /// </summary>
    private static List<ClosedCandle> BuildBullishCrossover(int warmupCount = 25, decimal highVol = 2500L)
    {
        var bars = new List<ClosedCandle>();

        // Warmup: flat price at 100 (seeds both EMAs at ~100)
        for (int i = 0; i < warmupCount; i++)
            bars.Add(Bar(100m, volume: 1_000L, minuteOffset: i));

        // Rising bars: price increases — fast EMA will start crossing slow EMA
        bars.Add(Bar(102m, 1_000L, minuteOffset: warmupCount));
        bars.Add(Bar(104m, 1_000L, minuteOffset: warmupCount + 1));
        bars.Add(Bar(107m, 1_000L, minuteOffset: warmupCount + 2));
        bars.Add(Bar(110m, 1_000L, minuteOffset: warmupCount + 3));
        bars.Add(Bar(113m, 1_000L, minuteOffset: warmupCount + 4));

        // The penultimate bar has fast EMA just below slow EMA (constructed so crossover happens next)
        bars.Add(Bar(115m, 1_000L, minuteOffset: warmupCount + 5));

        // Final bar: breakout bar with high volume
        bars.Add(Bar(close: 118m, volume: (long)highVol,
            open: 115m, high: 120m, low: 114m, minuteOffset: warmupCount + 6));

        return bars;
    }

    private static StrategyContext MakeContext(
        IReadOnlyList<ClosedCandle> candles,
        EmaVwapMomentumConfig config,
        OptionChainSnapshot? optionChain = null) =>
        new StrategyContext(
            StrategyInstanceId: Guid.NewGuid(),
            InternalSymbol:     "NSE:NIFTY50",
            Timeframe:          "5m",
            Candles:            candles,
            ConfigJson:         config,
            CorrelationId:      Guid.NewGuid().ToString(),
            OptionChain:        optionChain);

    // ── Insufficient data ─────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_WhenInsufficientCandles_ReturnsSkip()
    {
        var strategy = new EmaVwapMomentumStrategy(DefaultConfig());
        var tooFewCandles = new[] { Bar(100m), Bar(101m), Bar(102m) };
        var ctx = MakeContext(tooFewCandles, DefaultConfig());

        var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

        result.Signal.Should().Be("HOLD", "SKIP returns HOLD signal with SkippedReason set");
        result.SkippedReason.Should().Be(SkippedReason.InsufficientData.ToString());
    }

    // ── BUY signal ─────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_WhenEmaCrossoverAboveVwapWithVolume_ReturnsBuy()
    {
        var config = DefaultConfig();
        var strategy = new EmaVwapMomentumStrategy(config);
        var candles = BuildBullishCrossover(warmupCount: 25, highVol: 2500L);
        var ctx = MakeContext(candles, config);

        var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

        result.Signal.Should().Be("BUY");
        result.EntryPrice.Should().NotBeNull().And.BeGreaterThan(0);
        result.StopLoss.Should().NotBeNull().And.BeLessThan(result.EntryPrice!.Value);
        result.TakeProfit.Should().NotBeNull().And.BeGreaterThan(result.EntryPrice!.Value);
    }

    [Fact]
    public async Task EvaluateAsync_BuySignal_StopLossIsBeforeEntry()
    {
        var config = DefaultConfig();
        var strategy = new EmaVwapMomentumStrategy(config);
        var candles = BuildBullishCrossover();
        var ctx = MakeContext(candles, config);

        var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

        if (result.Signal == "BUY")
        {
            result.StopLoss.Should().BeLessThan(result.EntryPrice!.Value,
                "stop loss must always be below entry for long positions");
        }
    }

    [Fact]
    public async Task EvaluateAsync_BuySignal_RiskRewardRatioIsRespected()
    {
        var rrr = 2.5m;
        var config = DefaultConfig(rrr: rrr);
        var strategy = new EmaVwapMomentumStrategy(config);
        var candles = BuildBullishCrossover();
        var ctx = MakeContext(candles, config);

        var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

        if (result.Signal == "BUY")
        {
            var risk = result.EntryPrice!.Value - result.StopLoss!.Value;
            var reward = result.TakeProfit!.Value - result.EntryPrice!.Value;
            (reward / risk).Should().BeApproximately(rrr, 0.01m,
                $"TP must be exactly {rrr}× the risk (entry - SL)");
        }
    }

    // ── HOLD when conditions not met ──────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_WhenNoCrossover_ReturnsHold()
    {
        var config = DefaultConfig();
        var strategy = new EmaVwapMomentumStrategy(config);

        // Perfectly flat candles → EMAs equal, no crossover ever
        var flatCandles = Enumerable.Range(0, 40)
            .Select(i => Bar(100m, 1_000L, minuteOffset: i))
            .ToList();

        var ctx = MakeContext(flatCandles, config);
        var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

        result.Signal.Should().Be("HOLD");
        result.EntryPrice.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateAsync_WhenVolumeBelowThreshold_ReturnsHold()
    {
        var config = DefaultConfig();
        var strategy = new EmaVwapMomentumStrategy(config);

        // Build crossover candles but last bar has LOW volume
        var candles = BuildBullishCrossover(warmupCount: 25, highVol: 500L); // 500 < 1.5 × 1000 avg

        var ctx = MakeContext(candles, config);
        var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

        result.Signal.Should().Be("HOLD",
            "low volume on signal bar means no conviction — signal suppressed");
    }

    // ── SELL / short signal ───────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_WhenAllowShortFalse_NeverReturnsSell()
    {
        var config = DefaultConfig(allowShort: false);
        var strategy = new EmaVwapMomentumStrategy(config);

        // Build bearish candles (declining price)
        var bearish = new List<ClosedCandle>();
        for (int i = 0; i < 30; i++) bearish.Add(Bar(100m, 1_000L, minuteOffset: i));
        // Declining bars
        bearish.Add(Bar(98m,  1_000L, minuteOffset: 30));
        bearish.Add(Bar(95m,  1_000L, minuteOffset: 31));
        bearish.Add(Bar(92m,  1_000L, minuteOffset: 32));
        bearish.Add(Bar(89m,  1_000L, minuteOffset: 33));
        bearish.Add(Bar(86m,  2_500L, minuteOffset: 34)); // high volume but AllowShort=false

        var ctx = MakeContext(bearish, config);
        var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

        result.Signal.Should().NotBe("SELL",
            "AllowShort=false means no short signals regardless of conditions");
    }

    // ── Option chain filter ───────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_WhenOptionChainPcrBearish_SuppressesBuySignal()
    {
        // Arrange: setup option chain with PCR=1.5 (bearish) → should suppress BUY
        var config = DefaultConfig(useOptionChain: true);
        config.PcrBullishThreshold = 0.8m; // PCR must be < 0.8 for bullish bias

        var strategy = new EmaVwapMomentumStrategy(config);
        var candles = BuildBullishCrossover();

        // Build an option chain with bearish PCR (heavy PE OI vs CE OI)
        var optionChain = BuildOptionChain(spot: 118m, pcr: 1.5m);
        var ctx = MakeContext(candles, config, optionChain);

        var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

        result.Signal.Should().Be("HOLD",
            "PCR=1.5 is above the bullish threshold of 0.8 — option chain filter should suppress BUY");
    }

    [Fact]
    public async Task EvaluateAsync_WhenOptionChainPcrBullish_AllowsBuySignal()
    {
        // Arrange: option chain with PCR=0.6 (bullish) → should allow BUY
        var config = DefaultConfig(useOptionChain: true);
        config.PcrBullishThreshold = 0.8m;

        var strategy = new EmaVwapMomentumStrategy(config);
        var candles = BuildBullishCrossover();

        var optionChain = BuildOptionChain(spot: 118m, pcr: 0.6m);
        var ctx = MakeContext(candles, config, optionChain);

        var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

        result.Signal.Should().Be("BUY",
            "PCR=0.6 is below the bullish threshold — option chain filter passes");
    }

    [Fact]
    public async Task EvaluateAsync_WhenOptionChainIsNull_TreatedAsBullishBias()
    {
        // When UseOptionChain=true but OptionChain not available (fetch failed),
        // strategy should degrade gracefully and allow signals (null = no filter)
        var config = DefaultConfig(useOptionChain: true);
        var strategy = new EmaVwapMomentumStrategy(config);
        var candles = BuildBullishCrossover();

        var ctx = MakeContext(candles, config, optionChain: null); // null = OC fetch failed

        // Strategy handles null gracefully: context.OptionChain == null → skip OC check
        // The strategy code does: if (config.UseOptionChain && context.OptionChain != null)
        // So null OC → ocBullishBias stays true → signal passes
        var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

        result.Signal.Should().Be("BUY",
            "null option chain means OC filter is skipped — price action signal passes through");
    }

    [Fact]
    public async Task EvaluateAsync_WhenPriceNearCeOiWall_SuppressesBuySignal()
    {
        // Arrange: CE wall at 119 (1pt above close=118) → within OiWallBufferPct=2% → suppress BUY
        // 118 >= 119 × (1 - 0.02) = 119 × 0.98 = 116.62 → TRUE → near CE wall → suppress
        var config = DefaultConfig(useOptionChain: true);
        config.OiWallBufferPct = 2.0m; // 2% buffer — price within 2% of CE wall is suppressed

        var strategy = new EmaVwapMomentumStrategy(config);
        var candles = BuildBullishCrossover(); // last bar closes at ~118

        // CE wall at 119 (just 1pt above close of 118).
        // NearestCeResistance requires StrikePrice > SpotPrice, so 119 > 118 ✓.
        // Buffer check: 118 >= 119 × (1 - 0.02) = 116.62 → TRUE → near CE wall → suppress BUY.
        var optionChain = BuildOptionChainWithCeWall(spot: 118m, ceWallStrike: 119m, pcr: 0.6m);
        var ctx = MakeContext(candles, config, optionChain);

        var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

        result.Signal.Should().Be("HOLD",
            "price (118) is within 2% of CE resistance wall (119) — buying here would hit the wall immediately");
    }

    // ── Diagnostics ───────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_BuySignal_DiagnosticsContainAllIndicators()
    {
        var config = DefaultConfig();
        var strategy = new EmaVwapMomentumStrategy(config);
        var candles = BuildBullishCrossover();
        var ctx = MakeContext(candles, config);

        var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

        if (result.Signal == "BUY")
        {
            result.DiagnosticsJson.Should().NotBeNull("diagnostics help with troubleshooting live signals");
            var json = System.Text.Json.JsonSerializer.Serialize(result.DiagnosticsJson);
            json.Should().Contain("FastEma", "diagnostics must include FastEma value");
            json.Should().Contain("SlowEma", "diagnostics must include SlowEma value");
            json.Should().Contain("Vwap",    "diagnostics must include VWAP value");
            json.Should().Contain("Atr",     "diagnostics must include ATR value");
        }
    }

    // ── ATR filter ────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_WhenMarketTooFlat_AtrFilterSkipsSignal()
    {
        var config = DefaultConfig();
        config.MinAtrPct = 5.0m; // require ATR ≥ 5% of close — impossible for normal candles

        var strategy = new EmaVwapMomentumStrategy(config);
        var candles = BuildBullishCrossover();
        var ctx = MakeContext(candles, config);

        var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

        // With 5% MinAtrPct requirement, the ATR of ~2 on a 118 close (1.7%) fails the filter
        result.Signal.Should().Be("HOLD");
        result.SkippedReason.Should().Be(SkippedReason.FilterFailed.ToString());
    }

    // ── Option chain builder helpers ──────────────────────────────────────

    /// <summary>
    /// Builds a synthetic option chain with a given spot price and PCR.
    /// Creates equal CE/PE OI at all strikes except adjusting the total to hit the target PCR.
    /// PCR = totalPeOi / totalCeOi
    /// To achieve PCR=1.5: peOi = 1.5 × ceOi
    /// </summary>
    private static OptionChainSnapshot BuildOptionChain(decimal spot, decimal pcr)
    {
        var strikes = new[] { spot - 200, spot - 100, spot, spot + 100, spot + 200 };
        const long ceOi = 100_000L;
        var peOiTarget = (long)(ceOi * pcr); // total PE OI to achieve desired PCR
        var peOiPerStrike = peOiTarget / strikes.Length;

        var legs = strikes.SelectMany(strike => new[]
        {
            new OptionLeg(strike, "CE", LastTradedPrice: 5m, OpenInterest: ceOi, OiChange: 0, Volume: 1000, ImpliedVolatility: 18m, BidPrice: 4.9m, AskPrice: 5.1m, Delta: 0.5m),
            new OptionLeg(strike, "PE", LastTradedPrice: 5m, OpenInterest: peOiPerStrike, OiChange: 0, Volume: 800, ImpliedVolatility: 20m, BidPrice: 4.8m, AskPrice: 5.2m, Delta: -0.5m),
        }).ToList();

        return new OptionChainSnapshot(
            UnderlyingSymbol: "NIFTY 50",
            FetchedAt:        NodaTime.Instant.FromUtc(2024, 1, 15, 4, 20, 0),
            SpotPrice:        spot,
            Expiry:           new NodaTime.LocalDate(2024, 1, 18),
            Options:          legs.AsReadOnly());
    }

    /// <summary>
    /// Builds an option chain where the specified CE strike has extremely high OI (resistance wall).
    /// Uses ceWallStrike directly in the strike list to ensure NearestCeResistance picks it up.
    /// The ceWallStrike must be strictly above spot for NearestCeResistance to detect it.
    /// </summary>
    private static OptionChainSnapshot BuildOptionChainWithCeWall(decimal spot, decimal ceWallStrike, decimal pcr)
    {
        // Include ceWallStrike alongside surrounding strikes so it appears in the chain.
        // Deduplicate and sort to avoid duplicates if ceWallStrike happens to equal a default strike.
        var strikes = new[] { spot - 2, spot - 1, spot, ceWallStrike, ceWallStrike + 1 }
            .Distinct()
            .OrderBy(s => s)
            .ToArray();
        const long normalCeOi = 50_000L;
        const long wallCeOi   = 1_000_000L; // massive OI at the wall strike

        var legs = strikes.SelectMany(strike => new[]
        {
            new OptionLeg(strike, "CE",
                LastTradedPrice: 5m,
                OpenInterest: strike == ceWallStrike ? wallCeOi : normalCeOi,
                OiChange: 0, Volume: 1000, ImpliedVolatility: 18m, BidPrice: 4.9m, AskPrice: 5.1m, Delta: 0.5m),
            new OptionLeg(strike, "PE",
                LastTradedPrice: 5m,
                OpenInterest: (long)(normalCeOi * pcr),
                OiChange: 0, Volume: 800, ImpliedVolatility: 20m, BidPrice: 4.8m, AskPrice: 5.2m, Delta: -0.5m),
        }).ToList();

        return new OptionChainSnapshot(
            UnderlyingSymbol: "NIFTY 50",
            FetchedAt:        NodaTime.Instant.FromUtc(2024, 1, 15, 4, 20, 0),
            SpotPrice:        spot,
            Expiry:           new NodaTime.LocalDate(2024, 1, 18),
            Options:          legs.AsReadOnly());
    }
}
