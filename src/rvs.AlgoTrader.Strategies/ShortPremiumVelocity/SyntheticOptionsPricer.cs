using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

/// <summary>
/// Black-Scholes-Merton synthetic option pricer with vol-skew and slippage overlays.
/// Used for Backtest and ForwardTest modes where live option chains are unavailable.
/// Pure computation — no DB or network I/O. IClock used only for afternoon-session detection.
///
/// IV adjustment: adjustedIV = baseIV + SkewSlopePerStrike × (K − S)/S × 100
/// Base slippage: SlippageFraction × 2 × BSM_sigma × spot × sqrt(DTE/252)
/// Afternoon (≥ AfternoonSlippageStartHour:StartMinute IST): multiply by AfternoonSlippageMultiplier
/// </summary>
public sealed class SyntheticOptionsPricer(
    IClock                          clock,
    ILogger<SyntheticOptionsPricer> log)
    : ISyntheticOptionsPricer
{
    // IST offset (+5:30)
    private static readonly DateTimeZone Ist =
        DateTimeZone.ForOffset(Offset.FromHoursAndMinutes(5, 30));

    public SyntheticOptionPrice Price(
        string                     underlying,
        decimal                    spot,
        decimal                    strike,
        int                        dte,
        OptionType                 optionType,
        decimal                    iv,
        ShortPremiumVelocityConfig config)
    {
        if (spot <= 0 || strike <= 0)
        {
            log.LogWarning("SyntheticOptionsPricer: invalid spot={Spot} or strike={Strike} for {Sym}",
                spot, strike, underlying);
            return new SyntheticOptionPrice(0m, 0m, 0m, 0m, 0m, 0m, 0m);
        }

        // ── Vol-skew adjustment ───────────────────────────────────────────────
        decimal moneyness   = (strike - spot) / spot;
        decimal adjustedIv  = iv + config.SkewSlopePerStrike * moneyness * 100m;
        adjustedIv = Math.Max(adjustedIv, 0.01m); // minimum 1% IV floor

        // ── Time to expiry in years (NSE 252-day calendar) ───────────────────
        double  t      = Math.Max(dte, 0) / 252.0;
        double  sqrtT  = Math.Sqrt(t);
        double  sigma  = (double)adjustedIv / 100.0;
        double  r      = (double)config.RiskFreeRate;
        double  s      = (double)spot;
        double  k      = (double)strike;

        // ── BSM d1 / d2 ──────────────────────────────────────────────────────
        double d1, d2;
        if (t <= 0 || sigma <= 0)
        {
            // At/past expiry: intrinsic value only
            double intrinsic = optionType == OptionType.Call
                ? Math.Max(s - k, 0)
                : Math.Max(k - s, 0);
            return new SyntheticOptionPrice(
                (decimal)intrinsic, optionType == OptionType.Call ? 1m : -1m,
                0m, 0m, 0m, (decimal)intrinsic, 0m);
        }

        d1 = (Math.Log(s / k) + (r + 0.5 * sigma * sigma) * t) / (sigma * sqrtT);
        d2 = d1 - sigma * sqrtT;

        // ── Option price ──────────────────────────────────────────────────────
        double price;
        if (optionType == OptionType.Call)
            price = s * Nd(d1) - k * Math.Exp(-r * t) * Nd(d2);
        else
            price = k * Math.Exp(-r * t) * Nd(-d2) - s * Nd(-d1);

        // ── Greeks ────────────────────────────────────────────────────────────
        double nd1   = Nd(d1);
        double nd2   = Nd(d2);
        double npd1  = Npdf(d1);

        double delta = optionType == OptionType.Call ? nd1 : nd1 - 1.0;
        double gamma = npd1 / (s * sigma * sqrtT);
        // Theta: daily decay (divide annual by 252)
        double theta = optionType == OptionType.Call
            ? (-(s * npd1 * sigma) / (2 * sqrtT) - r * k * Math.Exp(-r * t) * nd2) / 252.0
            : (-(s * npd1 * sigma) / (2 * sqrtT) + r * k * Math.Exp(-r * t) * Nd(-d2)) / 252.0;
        double vega = s * npd1 * sqrtT / 100.0; // per 1% IV move

        // ── Slippage ──────────────────────────────────────────────────────────
        // Base: SlippageFraction × 2 × sigma × spot × sqrt(DTE/252)
        decimal baseSlippage = config.SlippageFraction
            * 2m * adjustedIv / 100m * spot
            * (decimal)Math.Sqrt(Math.Max(dte, 0) / 252.0);

        bool isAfternoon = IsAfternoonSession(config);
        if (isAfternoon)
            baseSlippage *= config.AfternoonSlippageMultiplier;

        decimal simFill = (decimal)price + baseSlippage; // for short legs caller adjusts sign

        return new SyntheticOptionPrice(
            TheoreticalPrice:  Math.Round((decimal)price,    4),
            Delta:             Math.Round((decimal)delta,    4),
            Gamma:             Math.Round((decimal)gamma,    6),
            Theta:             Math.Round((decimal)theta,    4),
            Vega:              Math.Round((decimal)vega,     4),
            SimulatedFillPrice: Math.Round(simFill,          4),
            SlippageApplied:   Math.Round(Math.Abs(simFill - (decimal)price), 4));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool IsAfternoonSession(ShortPremiumVelocityConfig config)
    {
        var now  = clock.NowInstant().InZone(Ist);
        int hour = now.Hour, minute = now.Minute;
        return hour > config.AfternoonSlippageStartHour ||
               (hour == config.AfternoonSlippageStartHour && minute >= config.AfternoonSlippageStartMinute);
    }

    /// <summary>Cumulative standard normal distribution N(x).</summary>
    private static double Nd(double x)
    {
        // Abramowitz & Stegun approximation 26.2.17 — max error 7.5e-8
        if (x < -8) return 0;
        if (x >  8) return 1;
        const double p  =  0.2316419;
        const double b1 =  0.319381530;
        const double b2 = -0.356563782;
        const double b3 =  1.781477937;
        const double b4 = -1.821255978;
        const double b5 =  1.330274429;
        double t = 1.0 / (1.0 + p * Math.Abs(x));
        double poly = t * (b1 + t * (b2 + t * (b3 + t * (b4 + t * b5))));
        double cdf  = 1.0 - Npdf(x) * poly;
        return x >= 0 ? cdf : 1.0 - cdf;
    }

    /// <summary>Standard normal PDF n(x).</summary>
    private static double Npdf(double x)
        => Math.Exp(-0.5 * x * x) / Math.Sqrt(2.0 * Math.PI);
}
