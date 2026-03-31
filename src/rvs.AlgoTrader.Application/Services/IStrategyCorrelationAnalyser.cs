namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// Analyses correlations between strategy return series and constructs an optimal portfolio.
/// Input: daily return vectors from BacktestResult or live trade history.
/// </summary>
public interface IStrategyCorrelationAnalyser
{
    /// <summary>
    /// Computes Pearson correlation coefficients between every pair of strategy return series.
    /// Flags pairs whose |correlation| exceeds <paramref name="highCorrThreshold"/>.
    /// </summary>
    CorrelationMatrix ComputeMatrix(
        IReadOnlyList<StrategyReturnSeries> series,
        double highCorrThreshold = 0.7);

    /// <summary>
    /// Runs a Monte Carlo efficient frontier simulation to find optimal portfolio weights.
    /// Returns the portfolio with the highest Sharpe ratio among <paramref name="portfolioCount"/> random samples.
    /// </summary>
    PortfolioConstructionResult ConstructPortfolio(
        IReadOnlyList<StrategyReturnSeries> series,
        int portfolioCount = 10_000,
        double riskFreeRate = 0.065); // 6.5% — approximate Indian 10Y G-sec yield
}

/// <summary>Daily return series for one strategy, derived from backtest or live P&amp;L.</summary>
public record StrategyReturnSeries(
    Guid   StrategyInstanceId,
    string StrategyName,
    // Daily return as a fraction (0.01 = 1%). Ordered chronologically.
    IReadOnlyList<double> DailyReturns);

/// <summary>
/// Full pairwise Pearson correlation matrix for a set of strategies.
/// </summary>
public record CorrelationMatrix(
    IReadOnlyList<string>            StrategyNames,
    // Symmetric matrix — Coefficients[i][j] is the Pearson r between strategy i and j.
    IReadOnlyList<IReadOnlyList<double>> Coefficients,
    // Pairs with |r| >= threshold, ordered by descending |r|.
    IReadOnlyList<HighCorrelationPair>   HighCorrelationPairs);

public record HighCorrelationPair(
    string StrategyA,
    string StrategyB,
    double Correlation);

/// <summary>
/// Result of portfolio construction: optimal weights + aggregate risk/return metrics.
/// </summary>
public record PortfolioConstructionResult(
    // Strategy name → optimal weight (0–1, sums to 1).
    IReadOnlyDictionary<string, double> OptimalWeights,
    double PortfolioSharpe,
    double PortfolioAnnualReturn,
    double PortfolioAnnualVolatility,
    double MaxDrawdown,
    // Diversification ratio: weighted-average individual volatility / portfolio volatility. Values > 1 indicate meaningful diversification benefit.
    double DiversificationRatio,
    // All sampled portfolios — use for efficient frontier chart (return vs volatility).
    IReadOnlyList<MonteCarloPortfolio> EfficientFrontierSamples);

public record MonteCarloPortfolio(
    IReadOnlyDictionary<string, double> Weights,
    double AnnualReturn,
    double AnnualVolatility,
    double SharpeRatio);
