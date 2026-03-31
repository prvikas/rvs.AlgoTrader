namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// Runs bootstrap Monte Carlo simulations over a sequence of trade P&amp;Ls to produce
/// confidence intervals for key risk metrics without requiring a new live backtest.
/// </summary>
public interface IMonteCarloSimulator
{
    /// <summary>
    /// Shuffles the supplied trade net P&amp;Ls N times and computes percentile distributions
    /// for final equity and maximum drawdown.
    /// </summary>
    MonteCarloSimulationResult Run(
        IReadOnlyList<decimal> netPnls, decimal initialCapital,
        int simulations = 1000, int? seed = null);
}

public record MonteCarloSimulationResult(
    decimal DrawdownP5,
    decimal DrawdownP50,
    decimal DrawdownP95,
    decimal EquityP5,
    decimal EquityP50,
    decimal EquityP95,
    decimal ProbabilityOfRuin);
