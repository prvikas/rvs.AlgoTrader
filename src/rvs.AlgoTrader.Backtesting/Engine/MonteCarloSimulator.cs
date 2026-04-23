using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Backtesting.Engine;

/// <summary>
/// Monte Carlo simulation on backtest trade sequence.
/// Shuffles trade order N times (bootstrap resampling) to compute confidence intervals
/// for MaxDrawdown and FinalEquity.
/// </summary>
public class MonteCarloSimulator(ILogger<MonteCarloSimulator> logger) : IMonteCarloSimulator
{
    // IMonteCarloSimulator implementation — operates on raw NetPnl values (from BacktestResultDto.Trades)
    public MonteCarloSimulationResult Run(
        IReadOnlyList<decimal> netPnls, decimal initialCapital, int simulations = 1000, int? seed = null)
    {
        if (netPnls.Count == 0)
            return new MonteCarloSimulationResult(0, 0, 0, 0, 0, 0, 0);

        var rng = seed.HasValue ? new Random(seed.Value) : new Random();
        logger.LogInformation("[MonteCarlo] Running {N} simulations on {T} trades", simulations, netPnls.Count);

        var maxDrawdowns  = new List<decimal>(simulations);
        var finalEquities = new List<decimal>(simulations);
        var pnlArr = netPnls.ToArray();

        for (int sim = 0; sim < simulations; sim++)
        {
            // Bootstrap-shuffle trade P&Ls (Fisher-Yates)
            var shuffled = pnlArr.ToArray();
            for (int i = shuffled.Length - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            var equity = initialCapital;
            var peak   = equity;
            var maxDd  = 0m;
            foreach (var pnl in shuffled)
            {
                equity += pnl;
                if (equity > peak) peak = equity;
                var dd = peak > 0 ? (peak - equity) / peak : 0m;
                if (dd > maxDd) maxDd = dd;
            }
            maxDrawdowns.Add(maxDd);
            finalEquities.Add(equity);
        }

        maxDrawdowns.Sort();
        finalEquities.Sort();

        // Probability of ruin: simulations where final equity < 50% of initial capital
        var ruin = simulations > 0
            ? (decimal)finalEquities.Count(e => e < initialCapital * 0.5m) / simulations
            : 0m;

        return new MonteCarloSimulationResult(
            DrawdownP5:        Percentile(maxDrawdowns, 5),
            DrawdownP50:       Percentile(maxDrawdowns, 50),
            DrawdownP95:       Percentile(maxDrawdowns, 95),
            EquityP5:          Percentile(finalEquities, 5),
            EquityP50:         Percentile(finalEquities, 50),
            EquityP95:         Percentile(finalEquities, 95),
            ProbabilityOfRuin: ruin);
    }

    // Domain-object overload: used internally by tooling that has BacktestTrade instances.
    // Runs a single combined simulation pass so MaxDrawdowns and FinalEquities are the real
    // per-simulation values (not stubs), and Sharpe uses the same N-1 sample variance as
    // BacktestEngine.ComputeGroupedSharpe.
    public MonteCarloResult RunFromTrades(IReadOnlyList<BacktestTrade> trades, decimal initialCapital, int simulations = 1000, int? seed = null)
    {
        if (trades.Count == 0)
            return new MonteCarloResult([], [], [], 0, 0, 0);

        logger.LogInformation("[MonteCarlo] Running {N} simulations on {T} trades (RunFromTrades)", simulations, trades.Count);

        var rng    = seed.HasValue ? new Random(seed.Value) : new Random();
        var pnlArr = trades.Select(t => t.NetPnl).ToArray();

        var maxDrawdowns  = new List<decimal>(simulations);
        var finalEquities = new List<decimal>(simulations);
        var sharpeRatios  = new List<decimal>(simulations);

        for (int sim = 0; sim < simulations; sim++)
        {
            // Fisher-Yates shuffle
            var shuffled = pnlArr.ToArray();
            for (int i = shuffled.Length - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            var equity  = initialCapital;
            var peak    = equity;
            var maxDd   = 0m;
            var returns = new double[shuffled.Length];

            for (int i = 0; i < shuffled.Length; i++)
            {
                returns[i] = (double)(shuffled[i] / Math.Max(1m, equity));
                equity += shuffled[i];
                if (equity > peak) peak = equity;
                var dd = peak > 0 ? (peak - equity) / peak : 0m;
                if (dd > maxDd) maxDd = dd;
            }
            maxDrawdowns.Add(maxDd);
            finalEquities.Add(equity);

            if (returns.Length > 1)
            {
                var avg = returns.Average();
                // N-1 sample variance — consistent with ComputeGroupedSharpe
                var std = Math.Sqrt(returns.Select(r => Math.Pow(r - avg, 2)).Sum() / (returns.Length - 1));
                sharpeRatios.Add(std == 0 ? 0m : (decimal)(avg / std * Math.Sqrt(252)));
            }
            else
            {
                sharpeRatios.Add(0m);
            }
        }

        maxDrawdowns.Sort();
        finalEquities.Sort();
        sharpeRatios.Sort();

        return new MonteCarloResult(
            maxDrawdowns, finalEquities, sharpeRatios,
            Percentile(maxDrawdowns, 5),
            Percentile(maxDrawdowns, 50),
            Percentile(maxDrawdowns, 95));
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
