using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Computes pairwise Pearson correlations and runs a Monte Carlo efficient frontier.
/// All computation is in-memory — no I/O; safe to call on any thread.
/// </summary>
public sealed class StrategyCorrelationAnalyser : IStrategyCorrelationAnalyser
{
    public CorrelationMatrix ComputeMatrix(
        IReadOnlyList<StrategyReturnSeries> series,
        double highCorrThreshold = 0.7)
    {
        var n = series.Count;
        var names = series.Select(s => s.StrategyName).ToList();

        // Build n×n coefficient matrix
        var coefficients = new double[n][];
        for (var i = 0; i < n; i++)
        {
            coefficients[i] = new double[n];
            for (var j = 0; j < n; j++)
            {
                coefficients[i][j] = i == j
                    ? 1.0
                    : Pearson(series[i].DailyReturns, series[j].DailyReturns);
            }
        }

        // Detect high-correlation pairs (upper triangle only to avoid duplicates)
        var highPairs = new List<HighCorrelationPair>();
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                if (Math.Abs(coefficients[i][j]) >= highCorrThreshold)
                    highPairs.Add(new HighCorrelationPair(names[i], names[j], coefficients[i][j]));
            }
        }
        highPairs.Sort((a, b) => Math.Abs(b.Correlation).CompareTo(Math.Abs(a.Correlation)));

        var rows = coefficients
            .Select(row => (IReadOnlyList<double>)row.ToList())
            .ToList();

        return new CorrelationMatrix(names, rows, highPairs);
    }

    public PortfolioConstructionResult ConstructPortfolio(
        IReadOnlyList<StrategyReturnSeries> series,
        int portfolioCount = 10_000,
        double riskFreeRate = 0.065)
    {
        var n = series.Count;
        var names = series.Select(s => s.StrategyName).ToList();

        // Pre-compute mean and stddev per strategy (annualised, 252 trading days)
        var means = series.Select(s => Mean(s.DailyReturns) * 252).ToArray();
        var vols  = series.Select(s => StdDev(s.DailyReturns) * Math.Sqrt(252)).ToArray();

        // Covariance matrix (annualised)
        var cov = BuildCovariance(series);

        var rng = new Random(42); // deterministic seed for reproducibility
        var samples = new List<MonteCarloPortfolio>(portfolioCount);

        for (var p = 0; p < portfolioCount; p++)
        {
            var weights = RandomWeights(n, rng);

            var portReturn = 0.0;
            for (var i = 0; i < n; i++)
                portReturn += weights[i] * means[i];

            var portVar = 0.0;
            for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                portVar += weights[i] * weights[j] * cov[i][j];

            var portVol   = Math.Sqrt(Math.Max(portVar, 0));
            var sharpe    = portVol > 0 ? (portReturn - riskFreeRate) / portVol : 0;
            var wDict     = names.Zip(weights, (nm, w) => (nm, w)).ToDictionary(t => t.nm, t => t.w);

            samples.Add(new MonteCarloPortfolio(wDict, portReturn, portVol, sharpe));
        }

        // Optimal = max Sharpe
        var optimal = samples.MaxBy(s => s.SharpeRatio)!;

        // Max drawdown — compute on equal-weight portfolio daily returns as a proxy
        var eqWeights = Enumerable.Repeat(1.0 / n, n).ToArray();
        var eqReturns = BuildPortfolioReturns(series, eqWeights);
        var maxDd = ComputeMaxDrawdown(eqReturns);

        // Diversification ratio
        var optWeightsArr = names.Select(nm => optimal.Weights[nm]).ToArray();
        var weightedAvgVol = optWeightsArr.Zip(vols, (w, v) => w * v).Sum();
        var optPortVar = 0.0;
        for (var i = 0; i < n; i++)
        for (var j = 0; j < n; j++)
            optPortVar += optWeightsArr[i] * optWeightsArr[j] * cov[i][j];
        var optPortVol = Math.Sqrt(Math.Max(optPortVar, 0));
        var divRatio = optPortVol > 0 ? weightedAvgVol / optPortVol : 1.0;

        return new PortfolioConstructionResult(
            OptimalWeights:           optimal.Weights,
            PortfolioSharpe:          optimal.SharpeRatio,
            PortfolioAnnualReturn:    optimal.AnnualReturn,
            PortfolioAnnualVolatility:optimal.AnnualVolatility,
            MaxDrawdown:              maxDd,
            DiversificationRatio:     divRatio,
            EfficientFrontierSamples: samples);
    }

    // ── Maths helpers ────────────────────────────────────────────────────────

    private static double Pearson(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var len = Math.Min(x.Count, y.Count);
        if (len < 2) return 0;

        var mx = x.Take(len).Average();
        var my = y.Take(len).Average();

        var num = 0.0; var dx2 = 0.0; var dy2 = 0.0;
        for (var i = 0; i < len; i++)
        {
            var dx = x[i] - mx;
            var dy = y[i] - my;
            num += dx * dy;
            dx2 += dx * dx;
            dy2 += dy * dy;
        }
        var denom = Math.Sqrt(dx2 * dy2);
        return denom == 0 ? 0 : num / denom;
    }

    private static double Mean(IReadOnlyList<double> v) => v.Count == 0 ? 0 : v.Average();

    private static double StdDev(IReadOnlyList<double> v)
    {
        if (v.Count < 2) return 0;
        var m = v.Average();
        var variance = v.Sum(x => (x - m) * (x - m)) / (v.Count - 1);
        return Math.Sqrt(variance);
    }

    private static double[][] BuildCovariance(IReadOnlyList<StrategyReturnSeries> series)
    {
        var n = series.Count;
        var cov = new double[n][];
        for (var i = 0; i < n; i++)
        {
            cov[i] = new double[n];
            for (var j = 0; j < n; j++)
            {
                var len = Math.Min(series[i].DailyReturns.Count, series[j].DailyReturns.Count);
                if (len < 2) continue;

                var mi = series[i].DailyReturns.Take(len).Average();
                var mj = series[j].DailyReturns.Take(len).Average();
                var sum = 0.0;
                for (var k = 0; k < len; k++)
                    sum += (series[i].DailyReturns[k] - mi) * (series[j].DailyReturns[k] - mj);
                // Annualised covariance (252 trading days)
                cov[i][j] = sum / (len - 1) * 252;
            }
        }
        return cov;
    }

    private static double[] RandomWeights(int n, Random rng)
    {
        var raw = new double[n];
        for (var i = 0; i < n; i++)
            raw[i] = -Math.Log(rng.NextDouble()); // exponential distribution trick for uniform on simplex
        var sum = raw.Sum();
        for (var i = 0; i < n; i++)
            raw[i] /= sum;
        return raw;
    }

    private static double[] BuildPortfolioReturns(IReadOnlyList<StrategyReturnSeries> series, double[] weights)
    {
        var len = series.Min(s => s.DailyReturns.Count);
        var result = new double[len];
        for (var t = 0; t < len; t++)
        {
            for (var i = 0; i < series.Count; i++)
                result[t] += weights[i] * series[i].DailyReturns[t];
        }
        return result;
    }

    private static double ComputeMaxDrawdown(double[] dailyReturns)
    {
        var peak = 1.0;
        var nav  = 1.0;
        var maxDd = 0.0;
        foreach (var r in dailyReturns)
        {
            nav  *= 1 + r;
            peak  = Math.Max(peak, nav);
            var dd = (peak - nav) / peak;
            maxDd = Math.Max(maxDd, dd);
        }
        return maxDd;
    }
}
