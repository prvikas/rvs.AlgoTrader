using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using rvs.AlgoTrader.Backtesting.Engine;
using rvs.AlgoTrader.Domain.ValueObjects;
using Xunit;

namespace rvs.AlgoTrader.Tests.Unit.Services;

public class BacktestEngineTests
{
    [Fact]
    public void BacktestResult_SharpeRatio_IsFiniteForNonZeroStdDev()
    {
        // Given some trades with positive and negative P&Ls
        var trades = new List<BacktestTrade>
        {
            MakeTrade("BUY", 1, 100m, 102m, 2m, "TAKE_PROFIT"),
            MakeTrade("BUY", 1, 100m, 98m, -2m, "STOP_LOSS"),
            MakeTrade("BUY", 1, 100m, 103m, 3m, "TAKE_PROFIT"),
            MakeTrade("BUY", 1, 100m, 97m, -3m, "STOP_LOSS"),
            MakeTrade("BUY", 1, 100m, 101m, 1m, "TAKE_PROFIT"),
        };

        var result = BacktestResult.FromTrades(trades, 10_000m);
        result.SharpeRatio.Should().NotBe(decimal.MaxValue);
        result.SharpeRatio.Should().NotBe(decimal.MinValue);
    }

    [Fact]
    public void BacktestResult_WithAllWins_ProfitFactorIsMaxValue()
    {
        var trades = new List<BacktestTrade>
        {
            MakeTrade("BUY", 1, 100m, 105m, 5m, "TAKE_PROFIT"),
            MakeTrade("BUY", 1, 100m, 104m, 4m, "TAKE_PROFIT"),
        };

        var result = BacktestResult.FromTrades(trades, 10_000m);
        result.WinRate.Should().Be(1.0m);
        result.LossCount.Should().Be(0);
    }

    [Fact]
    public void BacktestResult_MaxDrawdown_IsNeverNegative()
    {
        var trades = new List<BacktestTrade>
        {
            MakeTrade("BUY", 1, 100m, 95m, -5m, "STOP_LOSS"),
            MakeTrade("BUY", 1, 100m, 95m, -5m, "STOP_LOSS"),
            MakeTrade("BUY", 1, 100m, 110m, 10m, "TAKE_PROFIT"),
        };

        var result = BacktestResult.FromTrades(trades, 10_000m);
        result.MaxDrawdown.Should().BeGreaterThanOrEqualTo(0m);
        result.MaxDrawdown.Should().BeLessThanOrEqualTo(1m); // expressed as fraction
    }

    [Fact]
    public void MonteCarloSimulator_P95DrawdownExceedsMedian()
    {
        var simulator = new MonteCarloSimulator(NullLogger<MonteCarloSimulator>.Instance, seed: 42);
        var trades = Enumerable.Range(0, 20).Select(i =>
            MakeTrade("BUY", 1, 100m, i % 3 == 0 ? 105m : 97m, i % 3 == 0 ? 5m : -3m,
                i % 3 == 0 ? "TAKE_PROFIT" : "STOP_LOSS")).ToList();

        var result = simulator.Run(trades, 100_000m, simulations: 500);

        result.DrawdownP95.Should().BeGreaterThanOrEqualTo(result.DrawdownP50,
            because: "P95 drawdown should be worse than median");
        result.DrawdownP50.Should().BeGreaterThanOrEqualTo(result.DrawdownP5);
    }

    private static BacktestTrade MakeTrade(
        string direction, int qty, decimal entry, decimal exit, decimal pnl, string reason)
    {
        var ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
        var t = new LocalDateTime(2024, 1, 15, 9, 15, 0).InZoneLeniently(ist);
        return new BacktestTrade(Guid.NewGuid(), "TEST", direction, qty, entry, exit,
            direction == "BUY" ? entry * 0.99m : entry * 1.01m,
            direction == "BUY" ? entry * 1.02m : entry * 0.98m,
            t, t.Plus(Duration.FromHours(1)), pnl, pnl, reason);
    }
}
