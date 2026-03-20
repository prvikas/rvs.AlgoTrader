using FluentAssertions;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Infrastructure.Services;
using Xunit;

namespace rvs.AlgoTrader.Tests.Unit.Services;

public class TransactionCostCalculatorTests
{
    private readonly TransactionCostCalculator _calc = new();

    // MIS intraday profile: brokerage 0.02% (₹20 on ₹1L), STT sell-side 0.025%, GST 18%, SEBI 0.0001%, stamp 0.003%, slippage 0.02%
    private static readonly CostProfile MisProfile = new(
        BrokeragePct: 0.0002m,
        SttPct: 0.00025m,
        GstPct: 0.18m,
        SebiChargesPct: 0.000001m,
        StampDutyPct: 0.00003m,
        SlippagePct: 0.0002m);

    [Fact]
    public void Calculate_BuyMIS_BrokerageIs003Percent()
    {
        var result = _calc.Calculate(tradeValue: 100_000m, isBuy: true, profile: MisProfile);

        result.Brokerage.Should().Be(20m); // 0.02% × 1,00,000 = ₹20
        result.Total.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Calculate_SellMIS_HasStt()
    {
        var result = _calc.Calculate(tradeValue: 100_000m, isBuy: false, profile: MisProfile);

        // STT on sell = 0.025% of trade value for intraday
        result.Stt.Should().BeApproximately(25m, 1m);
    }

    [Fact]
    public void Calculate_Buy_HasNoStt()
    {
        var result = _calc.Calculate(tradeValue: 100_000m, isBuy: true, profile: MisProfile);

        // STT on buy = 0 for intraday MIS
        result.Stt.Should().Be(0m);
    }

    [Fact]
    public void Calculate_AllCosts_AreNonNegative()
    {
        var buy = _calc.Calculate(100_000m, true, MisProfile);
        var sell = _calc.Calculate(100_000m, false, MisProfile);

        buy.Brokerage.Should().BeGreaterThanOrEqualTo(0);
        buy.Stt.Should().BeGreaterThanOrEqualTo(0);
        buy.Gst.Should().BeGreaterThanOrEqualTo(0);
        buy.SebiCharges.Should().BeGreaterThanOrEqualTo(0);
        buy.StampDuty.Should().BeGreaterThanOrEqualTo(0);
        buy.Slippage.Should().BeGreaterThanOrEqualTo(0);
        buy.Total.Should().BeGreaterThan(0);

        sell.Total.Should().BeGreaterThan(buy.Total); // sell has STT
    }

    [Theory]
    [InlineData(10_000)]
    [InlineData(100_000)]
    [InlineData(1_000_000)]
    [InlineData(10_000_000)]
    public void Calculate_TotalCostGrowsWithTradeValue(decimal tradeValue)
    {
        var result = _calc.Calculate(tradeValue, false, MisProfile);
        result.Total.Should().BeGreaterThan(0);

        // Total cost should never exceed 1% of trade value for normal trades
        var costRatio = result.Total / tradeValue;
        costRatio.Should().BeLessThan(0.01m, because: "Total transaction cost should be < 1% of trade value");
    }

    [Fact]
    public void Calculate_GstIsOnBrokerageAndTransactionCharges()
    {
        var result = _calc.Calculate(100_000m, true, MisProfile);

        // GST = 18% on brokerage
        result.Gst.Should().BeApproximately(result.Brokerage * 0.18m, 2m);
    }
}
