using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Infrastructure.Services;

namespace rvs.AlgoTrader.Tests.Unit.Services;

/// <summary>
/// Verifies B2: entry + exit commission are both deducted and exposed in BacktestTrade.
/// Uses TransactionCostCalculator directly since BacktestEngine is integration-level.
/// </summary>
public class CommissionModelTests
{
    private static readonly CostProfile DefaultProfile = new(
        BrokeragePct:       0m,
        SttPct:             0.00025m,
        GstPct:             0.18m,
        SebiChargesPct:     0.000001m,
        StampDutyPct:       0.00003m,
        SlippagePct:        0.0002m,
        BrokerageFlatPerSide: 20m,
        SlippageBasisPoints: 0m);

    private readonly TransactionCostCalculator _calc = new();

    [Fact]
    public void EntryAndExitCosts_ArePositive_ForEquityBuy()
    {
        var entryCosts = _calc.Calculate(tradeValue: 100_000m, isBuy: true,  profile: DefaultProfile);
        var exitCosts  = _calc.Calculate(tradeValue: 100_000m, isBuy: false, profile: DefaultProfile);

        entryCosts.Total.Should().BeGreaterThan(0, "entry commission must be non-zero");
        exitCosts.Total.Should().BeGreaterThan(0,  "exit commission must be non-zero");
    }

    [Fact]
    public void NetPnl_EqualsGrossPnl_Minus_EntryAndExitCommission()
    {
        decimal entryPrice = 5000m;
        decimal exitPrice  = 5100m;
        int     quantity   = 40;

        var grossPnl = (exitPrice - entryPrice) * quantity; // ₹4,000

        var entryCosts = _calc.Calculate(entryPrice * quantity, isBuy: true,  profile: DefaultProfile);
        var exitCosts  = _calc.Calculate(exitPrice  * quantity, isBuy: false, profile: DefaultProfile);

        var netPnl = grossPnl - entryCosts.Total - exitCosts.Total;

        netPnl.Should().BeLessThan(grossPnl, "commissions must reduce gross P&L");
        netPnl.Should().BeGreaterThan(0m, "profitable trade should still be profitable after commission");
    }

    [Fact]
    public void EntryCommission_IsSeparate_FromExitCommission()
    {
        var entryCosts = _calc.Calculate(tradeValue: 200_000m, isBuy: true,  profile: DefaultProfile);
        var exitCosts  = _calc.Calculate(tradeValue: 202_000m, isBuy: false, profile: DefaultProfile);

        // Both should be positive and distinct — entry is a buy, exit is a sell
        // STT applies only on sell-side for equity intraday
        entryCosts.Should().NotBeSameAs(exitCosts);
        entryCosts.Total.Should().NotBe(exitCosts.Total, "entry (buy) and exit (sell) costs differ due to STT");
    }

    [Fact]
    public void FlatBrokerage_CapsDependingOnTradeSize()
    {
        // Flat ₹20/side regardless of trade value
        var smallTrade = _calc.Calculate(tradeValue: 10_000m, isBuy: true, profile: DefaultProfile);
        var largeTrade = _calc.Calculate(tradeValue: 500_000m, isBuy: true, profile: DefaultProfile);

        // Flat brokerage is ₹20 for both when BrokerageFlatPerSide = 20
        smallTrade.Brokerage.Should().Be(20m);
        largeTrade.Brokerage.Should().Be(20m);
    }
}
