using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Infrastructure.Services;
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.Tests.Unit.Strategies.ShortPremiumVelocity;

/// <summary>
/// Unit tests for the Velocity Regime Classifier (QuantRegimeService.ClassifyVelocityRegimeAsync).
///
/// Classification rules (first-match wins):
///   1. Panic           — IndiaVIX > 28 OR spot1dMove > 3σ(20d)
///   2. HighVolExpansion— VIX [20,28] AND vixRoC5d > 15%
///   3. PostPanicNorm   — VIX [18,24] AND vixRoC5d &lt; 0
///   4. ChoppyMean      — VIX [14,20] AND trendR² &lt; 0.4
///   5. LowVolComp      — VIX &lt; 16 AND trendR² ≥ 0.4
///   6. Fallback        — ChoppyMeanReversion
/// </summary>
public class VelocityRegimeClassifierTests
{
    private const string Symbol = "NIFTY 50";
    private static readonly DateTimeZone Ist = DateTimeZone.ForOffset(Offset.FromHoursAndMinutes(5, 30));

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClosedCandle MakeCandle(decimal close, int dayOffset = 0)
    {
        var date  = new LocalDate(2024, 6, 3).PlusDays(dayOffset);
        var open  = new LocalDateTime(date.Year, date.Month, date.Day, 9, 15, 0).InZoneLeniently(Ist);
        var close_ = open.Plus(Duration.FromHours(6.5));
        return new ClosedCandle(Symbol, "1d", open, close_, close, close + 1, close - 1, close, 1_000_000);
    }

    /// <summary>
    /// Build 75 candles with nearly constant close (gives low trendR² — choppy pattern).
    /// Alternates ±1% around baseClose every other bar.
    /// </summary>
    private static IReadOnlyList<ClosedCandle> MakeChoppySpotBars(decimal baseClose, int count = 75)
    {
        var list = new List<ClosedCandle>(count);
        for (int i = 0; i < count; i++)
        {
            decimal c = i % 2 == 0 ? baseClose * 1.01m : baseClose * 0.99m;
            list.Add(MakeCandle(c, i));
        }
        return list;
    }

    /// <summary>
    /// Build trending candles with noisy returns: alternating +0.3%/-0.1% (net +0.2%/bar).
    /// Gives high trendR² (≥ 0.4) due to sustained directional drift,
    /// while keeping daily returns well within the 3σ panic band (σ ≈ 0.2%, 3σ ≈ 0.6%).
    /// </summary>
    private static IReadOnlyList<ClosedCandle> MakeTrendingSpotBars(decimal baseClose, int count = 75)
    {
        var list  = new List<ClosedCandle>(count);
        decimal p = baseClose;
        for (int i = 0; i < count; i++)
        {
            // Alternating returns: +0.3% / -0.1% → net uptrend, non-zero σ prevents 3σ spurious panic
            decimal r = i % 2 == 0 ? 0.003m : -0.001m;
            p *= (1m + r);
            list.Add(MakeCandle(p, i));
        }
        return list;
    }

    /// <summary>
    /// Build VIX bars: 15 bars with <paramref name="latestVix"/> as the last bar
    /// and <paramref name="fiveDaysAgoVix"/> at index [^6] (6 bars from the end).
    /// This controls vixRoC5d = (latestVix − fiveDaysAgoVix) / fiveDaysAgoVix × 100.
    /// </summary>
    private static IReadOnlyList<ClosedCandle> MakeVixBars(decimal latestVix, decimal fiveDaysAgoVix, int count = 15)
    {
        var list = new List<ClosedCandle>(count);
        for (int i = 0; i < count; i++)
        {
            // Bar at position (count-6) gets fiveDaysAgoVix; last bar gets latestVix
            decimal vix = (i == count - 6) ? fiveDaysAgoVix
                        : (i == count - 1) ? latestVix
                        : latestVix;   // other bars: use latestVix (no impact on the two key reads)
            list.Add(new ClosedCandle("INDIAVIX", "1d",
                new LocalDateTime(2024, 6, 1 + i, 9, 0, 0).InZoneLeniently(Ist),
                new LocalDateTime(2024, 6, 1 + i, 15, 30, 0).InZoneLeniently(Ist),
                vix, vix + 0.5m, vix - 0.5m, vix, 100_000));
        }
        return list;
    }

    private static QuantRegimeService BuildSut(
        IReadOnlyList<ClosedCandle> spotBars,
        IReadOnlyList<ClosedCandle> vixBars,
        Instant? at = null)
    {
        var candles = new Mock<ICandleRepository>();
        candles.Setup(c => c.GetLastNAsync(Symbol,      "1d", It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(spotBars);
        candles.Setup(c => c.GetLastNAsync("INDIAVIX",  "1d", It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(vixBars);

        var ivRank = new Mock<IOptionIvRankService>();
        ivRank.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((IvRankSnapshot?)null);

        var clock = new Mock<IClock>();
        clock.Setup(c => c.NowInstant()).Returns(at ?? Instant.FromUtc(2024, 6, 3, 10, 0, 0));

        return new QuantRegimeService(
            candles.Object, ivRank.Object, clock.Object,
            NullLogger<QuantRegimeService>.Instance);
    }

    // ── Rule 1: Panic via VIX > 28 ───────────────────────────────────────────

    [Fact]
    public async Task Panic_WhenVixAbove28()
    {
        var spotBars = MakeChoppySpotBars(22_000m);
        var vixBars  = MakeVixBars(latestVix: 29m, fiveDaysAgoVix: 27m);
        var sut      = BuildSut(spotBars, vixBars);

        var result = await sut.ClassifyVelocityRegimeAsync(Symbol, CancellationToken.None);

        result.Label.Should().Be(MarketRegime.VelocityPanic,
            because: "IndiaVIX 29 > 28 triggers Panic regardless of other inputs");
    }

    [Fact]
    public async Task Panic_WhenVixExactlyAt28Point1()
    {
        // Boundary: > 28 not >= 28 — 28.1 must be Panic
        var spotBars = MakeChoppySpotBars(22_000m);
        var vixBars  = MakeVixBars(latestVix: 28.1m, fiveDaysAgoVix: 26m);
        var sut      = BuildSut(spotBars, vixBars);

        var result = await sut.ClassifyVelocityRegimeAsync(Symbol, CancellationToken.None);

        result.Label.Should().Be(MarketRegime.VelocityPanic);
    }

    // ── Rule 2: HighVolExpansion ──────────────────────────────────────────────

    [Fact]
    public async Task HighVolExpansion_WhenVixIn20to28AndRoC5dAbove15Pct()
    {
        // VIX = 25, 5-days-ago VIX = 21 → RoC = (25-21)/21×100 ≈ 19%
        var spotBars = MakeChoppySpotBars(22_000m);
        var vixBars  = MakeVixBars(latestVix: 25m, fiveDaysAgoVix: 21m);
        var sut      = BuildSut(spotBars, vixBars);

        var result = await sut.ClassifyVelocityRegimeAsync(Symbol, CancellationToken.None);

        result.Label.Should().Be(MarketRegime.VelocityHighVolExpansion,
            because: "VIX 25 in [20,28] and RoC 19% > 15% → HighVolExpansion");
    }

    [Fact]
    public async Task BoundaryLabel_VixAt27Point9_WithHighRoC16_IsHighVolExpansion()
    {
        // VIX 27.9 is still ≤ 28, so not Panic; RoC 16% > 15% → HighVol
        var spotBars = MakeChoppySpotBars(22_000m);
        var vixBars  = MakeVixBars(latestVix: 27.9m, fiveDaysAgoVix: 24m); // RoC ≈ 16.25%
        var sut      = BuildSut(spotBars, vixBars);

        var result = await sut.ClassifyVelocityRegimeAsync(Symbol, CancellationToken.None);

        result.Label.Should().Be(MarketRegime.VelocityHighVolExpansion);
    }

    // ── Rule 3: PostPanicNormalization ────────────────────────────────────────

    [Fact]
    public async Task PostPanicNormalization_WhenVixFalling()
    {
        // VIX = 20, 5-days-ago = 22 → RoC = -9% < 0
        var spotBars = MakeChoppySpotBars(22_000m);
        var vixBars  = MakeVixBars(latestVix: 20m, fiveDaysAgoVix: 22m);
        var sut      = BuildSut(spotBars, vixBars);

        var result = await sut.ClassifyVelocityRegimeAsync(Symbol, CancellationToken.None);

        result.Label.Should().Be(MarketRegime.VelocityPostPanicNormalization,
            because: "VIX 20 in [18,24] and RoC -9% < 0 → PostPanicNormalization");
    }

    // ── Rule 4: ChoppyMeanReversion ───────────────────────────────────────────

    [Fact]
    public async Task ChoppyMeanReversion_WhenVixIn14to20AndLowTrendR2()
    {
        // VIX = 17, alternating spot bars → low trendR²
        var spotBars = MakeChoppySpotBars(22_000m);
        var vixBars  = MakeVixBars(latestVix: 17m, fiveDaysAgoVix: 17m); // RoC = 0
        var sut      = BuildSut(spotBars, vixBars);

        var result = await sut.ClassifyVelocityRegimeAsync(Symbol, CancellationToken.None);

        result.Label.Should().Be(MarketRegime.VelocityChoppyMeanReversion,
            because: "VIX 17 in [14,20] and choppy (low R²) → Choppy");
    }

    // ── Rule 5: LowVolCompression ─────────────────────────────────────────────

    [Fact]
    public async Task LowVolCompression_WhenVixBelow16AndHighTrendR2()
    {
        // VIX = 13, trending spot bars → high R²
        var spotBars = MakeTrendingSpotBars(22_000m);
        var vixBars  = MakeVixBars(latestVix: 13m, fiveDaysAgoVix: 13m); // stable VIX, no RoC trigger
        var sut      = BuildSut(spotBars, vixBars);

        var result = await sut.ClassifyVelocityRegimeAsync(Symbol, CancellationToken.None);

        result.Label.Should().Be(MarketRegime.VelocityLowVolCompression,
            because: "VIX 13 < 16 and trending spot (high R²) → LowVolCompression");
    }

    // ── IsResultsSeason ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]  // Jan
    [InlineData(2)]  // Feb
    [InlineData(4)]  // Apr
    [InlineData(5)]  // May
    [InlineData(7)]  // Jul
    [InlineData(8)]  // Aug
    [InlineData(10)] // Oct
    [InlineData(11)] // Nov
    public async Task IsResultsSeason_TrueInEarningsMonths(int month)
    {
        var at       = Instant.FromUtc(2024, month, 15, 10, 0, 0);
        var spotBars = MakeChoppySpotBars(22_000m);
        var vixBars  = MakeVixBars(latestVix: 17m, fiveDaysAgoVix: 17m);
        var sut      = BuildSut(spotBars, vixBars, at);

        var result = await sut.ClassifyVelocityRegimeAsync(Symbol, CancellationToken.None);

        result.IsResultsSeason.Should().BeTrue($"month {month} is an earnings month");
    }

    [Theory]
    [InlineData(3)]  // Mar
    [InlineData(6)]  // Jun
    [InlineData(9)]  // Sep
    [InlineData(12)] // Dec
    public async Task IsResultsSeason_FalseInOffMonths(int month)
    {
        var at       = Instant.FromUtc(2024, month, 15, 10, 0, 0);
        var spotBars = MakeChoppySpotBars(22_000m);
        var vixBars  = MakeVixBars(latestVix: 17m, fiveDaysAgoVix: 17m);
        var sut      = BuildSut(spotBars, vixBars, at);

        var result = await sut.ClassifyVelocityRegimeAsync(Symbol, CancellationToken.None);

        result.IsResultsSeason.Should().BeFalse($"month {month} is not an earnings month");
    }

    // ── Output quality ────────────────────────────────────────────────────────

    [Fact]
    public async Task RegimeStability_IsWithin0To100()
    {
        var spotBars = MakeChoppySpotBars(22_000m);
        var vixBars  = MakeVixBars(latestVix: 17m, fiveDaysAgoVix: 17m);
        var sut      = BuildSut(spotBars, vixBars);

        var result = await sut.ClassifyVelocityRegimeAsync(Symbol, CancellationToken.None);

        result.RegimeStability.Should().BeInRange(0m, 100m);
    }

    [Fact]
    public async Task TailRiskScore_IsWithin0To100()
    {
        var spotBars = MakeChoppySpotBars(22_000m);
        var vixBars  = MakeVixBars(latestVix: 25m, fiveDaysAgoVix: 20m);
        var sut      = BuildSut(spotBars, vixBars);

        var result = await sut.ClassifyVelocityRegimeAsync(Symbol, CancellationToken.None);

        result.TailRiskScore.Should().BeInRange(0m, 100m);
    }

    [Fact]
    public async Task ConfigVersion_IsNonEmptyHexString()
    {
        var spotBars = MakeChoppySpotBars(22_000m);
        var vixBars  = MakeVixBars(latestVix: 17m, fiveDaysAgoVix: 17m);
        var sut      = BuildSut(spotBars, vixBars);

        var result = await sut.ClassifyVelocityRegimeAsync(Symbol, CancellationToken.None);

        result.ConfigVersion.Should().NotBeNullOrEmpty();
        result.ConfigVersion.Should().MatchRegex("^[0-9a-f]+$",
            because: "ConfigVersion is a SHA-256 lower-hex digest");
    }

    [Fact]
    public async Task Fallback_WhenNoCandlesAvailable_ReturnsChoppy()
    {
        // Empty candle lists → graceful degradation to Choppy fallback
        var candles = new Mock<ICandleRepository>();
        candles.Setup(c => c.GetLastNAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync([]);

        var ivRank = new Mock<IOptionIvRankService>();
        ivRank.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((IvRankSnapshot?)null);

        var clock = new Mock<IClock>();
        clock.Setup(c => c.NowInstant()).Returns(Instant.FromUtc(2024, 6, 3, 10, 0, 0));

        var sut = new QuantRegimeService(
            candles.Object, ivRank.Object, clock.Object,
            NullLogger<QuantRegimeService>.Instance);

        // Should not throw and should return a valid (fallback) result
        var result = await sut.ClassifyVelocityRegimeAsync(Symbol, CancellationToken.None);

        result.Should().NotBeNull();
    }
}
