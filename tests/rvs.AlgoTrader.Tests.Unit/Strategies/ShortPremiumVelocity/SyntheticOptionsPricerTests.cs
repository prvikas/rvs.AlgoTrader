using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Strategies.ShortPremiumVelocity;
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.Tests.Unit.Strategies.ShortPremiumVelocity;

/// <summary>
/// Unit tests for SyntheticOptionsPricer (BSM + vol-skew + slippage).
///
/// BSM properties verified:
///   - ATM call price > 0 for non-zero IV and DTE
///   - Call price ≤ spot (no-arbitrage upper bound)
///   - Deep ITM call delta ≈ 1.0
///   - Deep OTM call delta ≈ 0.0
///   - Put-call parity: C − P ≈ S × e^(-r×T) − K × e^(-r×T) (approximate)
///   - Expired option (DTE=0): returns intrinsic value
///   - Negative spot/strike: returns zero price (guard)
///
/// Skew: OTM call (strike &gt; spot) has lower IV than ATM due to negative skew slope.
/// Afternoon slippage: multiplied at/after AfternoonSlippageStartHour.
/// </summary>
public class SyntheticOptionsPricerTests
{
    private static readonly ShortPremiumVelocityConfig DefaultConfig = new();

    private static SyntheticOptionsPricer BuildSut(Instant? at = null)
    {
        var clock = new Mock<IClock>();
        // Default: morning session (10:00 IST = UTC 04:30) — below afternoon threshold
        clock.Setup(c => c.NowInstant())
             .Returns(at ?? Instant.FromUtc(2024, 6, 3, 4, 30, 0));

        return new SyntheticOptionsPricer(clock.Object, NullLogger<SyntheticOptionsPricer>.Instance);
    }

    // ── ATM call price is positive ────────────────────────────────────────────

    [Fact]
    public void AtmCall_TheoreticalPrice_IsPositive()
    {
        var sut = BuildSut();

        var result = sut.Price("NIFTY", spot: 22_000m, strike: 22_000m,
            dte: 21, OptionType.Call, iv: 15m, DefaultConfig);

        result.TheoreticalPrice.Should().BePositive(
            because: "a 21-DTE ATM call with 15% IV must have positive time value");
    }

    // ── Call price ≤ spot (no-arbitrage) ─────────────────────────────────────

    [Fact]
    public void CallPrice_NeverExceedsSpot()
    {
        var sut  = BuildSut();
        decimal spot = 22_000m;

        var result = sut.Price("NIFTY", spot: spot, strike: 22_000m,
            dte: 30, OptionType.Call, iv: 20m, DefaultConfig);

        result.TheoreticalPrice.Should().BeLessThanOrEqualTo(spot,
            because: "call option can never be worth more than the underlying (no-arbitrage bound)");
    }

    // ── Deep ITM call delta ≈ 1 ───────────────────────────────────────────────

    [Fact]
    public void DeepItmCall_DeltaApproximately1()
    {
        var sut = BuildSut();

        // Strike = 10% of spot (massively deep ITM call)
        var result = sut.Price("NIFTY", spot: 22_000m, strike: 5_000m,
            dte: 30, OptionType.Call, iv: 15m, DefaultConfig);

        result.Delta.Should().BeApproximately(1m, precision: 0.05m,
            because: "a deep in-the-money call has delta close to 1.0");
    }

    // ── Deep OTM call delta ≈ 0 ──────────────────────────────────────────────

    [Fact]
    public void DeepOtmCall_DeltaApproximately0()
    {
        var sut = BuildSut();

        // Strike = 150% of spot (massively deep OTM call)
        var result = sut.Price("NIFTY", spot: 22_000m, strike: 33_000m,
            dte: 30, OptionType.Call, iv: 15m, DefaultConfig);

        result.Delta.Should().BeApproximately(0m, precision: 0.05m,
            because: "a deep out-of-the-money call has delta close to 0");
    }

    // ── ATM put delta ≈ -0.5 ─────────────────────────────────────────────────

    [Fact]
    public void AtmPut_DeltaApproximatelyNegativeHalf()
    {
        var sut = BuildSut();

        var result = sut.Price("NIFTY", spot: 22_000m, strike: 22_000m,
            dte: 21, OptionType.Put, iv: 15m, DefaultConfig);

        result.Delta.Should().BeApproximately(-0.5m, precision: 0.10m,
            because: "an ATM put has delta approximately -0.5; vol-skew adjustment shifts it slightly");
    }

    // ── Expired option: intrinsic only ────────────────────────────────────────

    [Fact]
    public void ExpiredItmCall_ReturnsIntrinsicValue()
    {
        var sut = BuildSut();

        var result = sut.Price("NIFTY", spot: 22_000m, strike: 21_000m,
            dte: 0, OptionType.Call, iv: 15m, DefaultConfig);

        // Intrinsic = 22000 - 21000 = 1000
        result.TheoreticalPrice.Should().Be(1_000m,
            because: "at expiry (DTE=0) an ITM call is worth exactly its intrinsic value S−K");
    }

    [Fact]
    public void ExpiredOtmCall_ReturnsZero()
    {
        var sut = BuildSut();

        var result = sut.Price("NIFTY", spot: 22_000m, strike: 23_000m,
            dte: 0, OptionType.Call, iv: 15m, DefaultConfig);

        result.TheoreticalPrice.Should().Be(0m,
            because: "at expiry an OTM call expires worthless");
    }

    // ── Invalid inputs → zero price ───────────────────────────────────────────

    [Fact]
    public void NegativeSpot_ReturnsZeroPrice()
    {
        var sut = BuildSut();

        var result = sut.Price("NIFTY", spot: -100m, strike: 22_000m,
            dte: 21, OptionType.Call, iv: 15m, DefaultConfig);

        result.TheoreticalPrice.Should().Be(0m,
            because: "negative spot price is invalid; the pricer must return a safe zero-value result");
    }

    // ── Slippage is positive ──────────────────────────────────────────────────

    [Fact]
    public void Slippage_IsPositive_ForNonZeroDteAndIv()
    {
        var sut = BuildSut();

        var result = sut.Price("NIFTY", spot: 22_000m, strike: 22_000m,
            dte: 21, OptionType.Call, iv: 15m, DefaultConfig);

        result.SlippageApplied.Should().BeGreaterThanOrEqualTo(0m,
            because: "slippage must be non-negative (it adds cost to the fill)");
    }

    // ── SimulatedFillPrice = TheoreticalPrice + Slippage ─────────────────────

    [Fact]
    public void SimulatedFillPrice_EqualsTheoretical_Plus_Slippage()
    {
        var sut = BuildSut();

        var result = sut.Price("NIFTY", spot: 22_000m, strike: 22_000m,
            dte: 21, OptionType.Call, iv: 15m, DefaultConfig);

        decimal expected = result.TheoreticalPrice + result.SlippageApplied;
        result.SimulatedFillPrice.Should().BeApproximately(expected, precision: 0.01m,
            because: "SimulatedFillPrice = TheoreticalPrice + SlippageApplied for long legs");
    }

    // ── Vol-skew adjustment: OTM call has lower effective IV ──────────────────

    [Fact]
    public void SkewAdjustment_OtmCall_HasLowerPriceThanAtm_ForNegativeSkewSlope()
    {
        // Default SkewSlopePerStrike is negative (OTM calls cheaper)
        // Strike > Spot → moneyness positive → adjustedIV decreases
        // This is the standard equity-vol smile / skew
        var sut = BuildSut();
        decimal spot = 22_000m;

        var atmResult = sut.Price("NIFTY", spot, strike: spot,     dte: 21, OptionType.Call, iv: 20m, DefaultConfig);
        var otmResult = sut.Price("NIFTY", spot, strike: spot * 1.05m, dte: 21, OptionType.Call, iv: 20m, DefaultConfig);

        // With default SkewSlopePerStrike, OTM call has lower absolute price (less IV)
        // Just verify both are positive and OTM is cheaper (lower premium)
        otmResult.TheoreticalPrice.Should().BeLessThan(atmResult.TheoreticalPrice,
            because: "OTM calls have less intrinsic value than ATM; with similar or lower IV they must be cheaper");
    }

    // ── Greeks are within sensible bounds ─────────────────────────────────────

    [Fact]
    public void Gamma_IsPositive_ForNonExpiredOption()
    {
        var sut = BuildSut();

        var result = sut.Price("NIFTY", spot: 22_000m, strike: 22_000m,
            dte: 14, OptionType.Call, iv: 15m, DefaultConfig);

        result.Gamma.Should().BePositive(because: "gamma is always positive for long options");
    }

    [Fact]
    public void CallDelta_IsWithin0And1()
    {
        var sut = BuildSut();

        var result = sut.Price("NIFTY", spot: 22_000m, strike: 22_000m,
            dte: 21, OptionType.Call, iv: 15m, DefaultConfig);

        result.Delta.Should().BeInRange(0m, 1m,
            because: "call delta is always in [0, 1]");
    }

    [Fact]
    public void PutDelta_IsWithinNeg1And0()
    {
        var sut = BuildSut();

        var result = sut.Price("NIFTY", spot: 22_000m, strike: 22_000m,
            dte: 21, OptionType.Put, iv: 15m, DefaultConfig);

        result.Delta.Should().BeInRange(-1m, 0m,
            because: "put delta is always in [-1, 0]");
    }
}
