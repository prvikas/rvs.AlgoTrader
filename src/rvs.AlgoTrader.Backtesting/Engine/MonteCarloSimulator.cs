using Microsoft.Extensions.Logging;

namespace rvs.AlgoTrader.Backtesting.Engine;

/// <summary>
/// Monte Carlo simulation on backtest trade sequence.
/// Shuffles trade order N times to compute confidence intervals for key metrics.
/// Outputs: P5/P50/P95 for MaxDrawdown, FinalEquity, and Sharpe.
/// </summary>
public class MonteCarloSimulator(ILogger<MonteCarloSimulator> logger, int? seed = null)
{
    private readonly Random _rng = seed.HasValue ? new Random(seed.Value) : new Random();

    public MonteCarloResult Run(IReadOnlyList<BacktestTrade> trades, decimal initialCapital, int simulations = 1000)
    {
        if (trades.Count == 0)
            return new MonteCarloResult([], [], [], 0, 0, 0);

        logger.LogInformation("[MonteCarlo] Running {N} simulations on {T} trades", simulations, trades.Count);

        var maxDrawdowns = new List<decimal>();
        var finalEquities = new List<decimal>();
        var sharpeRatios = new List<decimal>();
        var tradePnls = trades.Select(t => t.NetPnl).ToArray();

        for (int sim = 0; sim < simulations; sim++)
        {
            // Shuffle trade sequence
            var shuffled = tradePnls.ToArray();
            for (int i = shuffled.Length - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            // Compute equity curve
            var equity = initialCapital;
            var peak = equity;
            var maxDd = 0m;
            var returns = new List<double>();

            foreach (var pnl in shuffled)
            {
                var prevEquity = equity;
                equity += pnl;
                returns.Add((double)(pnl / prevEquity));
                if (equity > peak) peak = equity;
                var dd = (peak - equity) / peak;
                if (dd > maxDd) maxDd = dd;
            }

            maxDrawdowns.Add(maxDd);
            finalEquities.Add(equity);

            var avgRet = returns.Average();
            var std = Math.Sqrt(returns.Select(r => Math.Pow(r - avgRet, 2)).Average());
            var sharpe = std == 0 ? 0 : (decimal)(avgRet / std * Math.Sqrt(252));
            sharpeRatios.Add(sharpe);
        }

        maxDrawdowns.Sort();
        finalEquities.Sort();
        sharpeRatios.Sort();

        return new MonteCarloResult(
            maxDrawdowns, finalEquities, sharpeRatios,
            Percentile(maxDrawdowns, 5), Percentile(maxDrawdowns, 50), Percentile(maxDrawdowns, 95));
    }

    private static decimal Percentile(List<decimal> sorted, int p)
    {
        var idx = (int)Math.Ceiling(sorted.Count * p / 100.0) - 1;
        return sorted[Math.Max(0, Math.Min(idx, sorted.Count - 1))];
    }
}

public record MonteCarloResult(
    IReadOnlyList<decimal> MaxDrawdowns,
    IReadOnlyList<decimal> FinalEquities,
    IReadOnlyList<decimal> SharpeRatios,
    decimal DrawdownP5,
    decimal DrawdownP50,
    decimal DrawdownP95);
