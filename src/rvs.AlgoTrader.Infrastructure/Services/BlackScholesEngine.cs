using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Stateless Black-Scholes engine for European-style options (NSE index options are European).
/// Uses the Abramowitz &amp; Stegun rational approximation for the cumulative normal distribution
/// (max error &lt; 7.5e-8) — sufficiently accurate for trading Greeks.
/// Thread-safe: no mutable state.
/// </summary>
public sealed class BlackScholesEngine : IBlackScholesEngine
{
    public GreeksSnapshot Compute(
        decimal spotPrice,
        decimal strikePrice,
        double timeToExpiryYears,
        double volatility,
        double riskFreeRate,
        bool isCall)
    {
        if (timeToExpiryYears <= 0)
            return new GreeksSnapshot(isCall ? 0m : -1m, 0m, 0m, 0m, 0m, 0m);

        double s = (double)spotPrice;
        double k = (double)strikePrice;
        double t = timeToExpiryYears;
        double v = volatility;
        double r = riskFreeRate;

        double sqrtT = Math.Sqrt(t);
        double d1 = (Math.Log(s / k) + (r + 0.5 * v * v) * t) / (v * sqrtT);
        double d2 = d1 - v * sqrtT;

        double nd1  = Nd(d1);
        double nd2  = Nd(d2);
        double npd1 = Nprime(d1);  // standard normal PDF at d1
        double disc = Math.Exp(-r * t);

        double price, delta, rho;

        if (isCall)
        {
            price = s * nd1 - k * disc * nd2;
            delta = nd1;
            rho   = k * t * disc * nd2 / 100.0;
        }
        else
        {
            price = k * disc * Nd(-d2) - s * Nd(-d1);
            delta = nd1 - 1.0;
            rho   = -k * t * disc * Nd(-d2) / 100.0;
        }

        double gamma = npd1 / (s * v * sqrtT);
        // Theta in ₹/day (divide by 365 for daily decay)
        double thetaAnnual = isCall
            ? -s * npd1 * v / (2.0 * sqrtT) - r * k * disc * nd2
            : -s * npd1 * v / (2.0 * sqrtT) + r * k * disc * Nd(-d2);
        double theta = thetaAnnual / 365.0;
        // Vega: ₹ change per 1% change in IV
        double vega = s * npd1 * sqrtT / 100.0;

        return new GreeksSnapshot(
            Delta:            Round(delta),
            Gamma:            Round(gamma),
            Theta:            Round(theta),
            Vega:             Round(vega),
            Rho:              Round(rho),
            TheoreticalPrice: Round(price));
    }

    // Cumulative normal distribution — Abramowitz & Stegun approximation 26.2.17
    private static double Nd(double x)
    {
        if (x < -8.0) return 0.0;
        if (x >  8.0) return 1.0;

        bool negative = x < 0;
        if (negative) x = -x;

        const double p  = 0.2316419;
        const double b1 = 0.319381530;
        const double b2 = -0.356563782;
        const double b3 = 1.781477937;
        const double b4 = -1.821255978;
        const double b5 = 1.330274429;

        double t = 1.0 / (1.0 + p * x);
        double poly = t * (b1 + t * (b2 + t * (b3 + t * (b4 + t * b5))));
        double result = 1.0 - Nprime(x) * poly;

        return negative ? 1.0 - result : result;
    }

    // Standard normal PDF
    private static double Nprime(double x)
        => Math.Exp(-0.5 * x * x) / Math.Sqrt(2.0 * Math.PI);

    private static decimal Round(double v) => (decimal)Math.Round(v, 6);
}
