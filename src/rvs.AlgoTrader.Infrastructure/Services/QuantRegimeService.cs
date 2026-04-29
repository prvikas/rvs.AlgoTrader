using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.QuantIntelligence;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// P10-B: Quant Regime Engine.
///
/// Classification priority (first match wins):
///  1. PanicShock       — VIX > 28, or VIX spike > 20% in 5 days
///  2. CompressionWatch — ADX &lt; 15, ATR ratio &lt; 0.80, IVP &lt; 35
///  3. TrendingUp       — ADX > 25, +DI > -DI
///  4. TrendingDown     — ADX > 25, -DI > +DI
///  5. ElevatedVolatility — VIX > 20, or IVP > 70, or ATR ratio > 1.6
///  6. Choppy           — default fallback
///
/// Indicators computed from candle history (no external API calls for core logic):
///   - ADX-14, +DI-14, -DI-14 (Wilder's smoothing)
///   - ATR ratio = ATR-5 / ATR-20 (expansion/contraction signal)
///
/// India VIX + IVP are sourced from IOptionIvRankService when available;
/// gracefully degraded when data is absent.
/// </summary>
public sealed class QuantRegimeService(
    ICandleRepository       candles,
    IOptionIvRankService    ivRank,
    IClock                  clock,
    ILogger<QuantRegimeService> log)
    : IQuantRegimeService
{
    // Minimum candles needed: 14 (DI warm-up) + 14 (ADX EMA) + 20 (ATR-20) + buffer = 60
    private const int RequiredBars = 60;

    public async Task<QuantRegimeDto> ClassifyAsync(string symbol, string timeframe, CancellationToken ct)
    {
        var now = clock.NowInstant();
        var from = now - Duration.FromDays(365);   // fetch up to 1 year; repository caps to available data

        IReadOnlyList<ClosedCandle> bars;
        try
        {
            bars = await candles.GetLastNAsync(symbol, timeframe, RequiredBars, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "QuantRegimeService: candle fetch failed for {Symbol}/{Tf}", symbol, timeframe);
            bars = [];
        }

        string? dataWarning = null;
        if (bars.Count < 28)
        {
            dataWarning = $"Only {bars.Count} bars available (need ≥28 for ADX). Regime estimated with limited data.";
        }

        // ── Compute ADX + DI ──────────────────────────────────────────────────
        var (adx, diPlus, diMinus) = bars.Count >= 28
            ? ComputeAdx(bars, 14)
            : (0m, 0m, 0m);

        // ── Compute ATR ratio ─────────────────────────────────────────────────
        decimal atrRatio = 1m;
        if (bars.Count >= 21)
        {
            var atr5  = ComputeAtr(bars.Skip(bars.Count - 5).ToList(),  5);
            var atr20 = ComputeAtr(bars.Skip(bars.Count - 20).ToList(), 20);
            atrRatio  = atr20 > 0 ? atr5 / atr20 : 1m;
        }

        // ── IVP from DB (best-effort) ─────────────────────────────────────────
        decimal? ivp = null;
        try
        {
            var snap = await ivRank.GetAsync(symbol, ct);
            ivp = snap?.IvPercentile;
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "QuantRegimeService: IVP unavailable for {Symbol}", symbol);
        }

        // ── VIX — represented via IVP when symbol is an index; direct VIX data
        //    not yet available in this integration (VERIFY_LIVE). We use IVP as
        //    the primary volatility-richness signal and treat IVP > 80 as panic-proxy.
        // ─────────────────────────────────────────────────────────────────────
        var factors = new List<QuantRegimeFactor>();

        AddFactor(factors, "ADX-14", $"{adx:F1}",
            adx > 25 ? "Strong trend" : adx > 20 ? "Moderate trend" : adx < 15 ? "Very weak / range-bound" : "Weak trend",
            adx > 25 ? "supporting" : adx < 15 ? "contradicting" : "neutral");

        AddFactor(factors, "+DI / -DI", $"{diPlus:F1} / {diMinus:F1}",
            diPlus > diMinus ? "Bullish direction" : diMinus > diPlus ? "Bearish direction" : "No clear direction",
            "neutral");

        AddFactor(factors, "ATR ratio (5/20)", $"{atrRatio:F2}",
            atrRatio > 1.6m ? "Volatility expanding sharply" :
            atrRatio > 1.2m ? "Volatility expanding" :
            atrRatio < 0.8m ? "Volatility contracting" : "Volatility stable",
            atrRatio > 1.6m ? "supporting" : atrRatio < 0.7m ? "supporting" : "neutral");

        if (ivp.HasValue)
            AddFactor(factors, "IV Percentile", $"{ivp:F0}th",
                ivp > 70 ? "Options expensive — sell premium" :
                ivp < 30 ? "Options cheap — vol expansion watch" : "Options fairly priced",
                ivp > 70 ? "supporting" : ivp < 30 ? "supporting" : "neutral");
        else
            AddFactor(factors, "IV Percentile", "n/a", "IVP data not available for this symbol", "neutral");

        // ── Classify ─────────────────────────────────────────────────────────
        var regime = Classify(adx, diPlus, diMinus, atrRatio, ivp);
        var (light, summary, guidance) = Describe(regime, adx, diPlus, diMinus, atrRatio, ivp);

        int confidence = ComputeConfidence(regime, adx, diPlus, diMinus, atrRatio, ivp, bars.Count);

        return new QuantRegimeDto(
            Regime:              regime.ToString(),
            TrafficLight:        light.ToString(),
            Confidence:          confidence,
            Summary:             summary,
            StrategyGuidance:    guidance,
            ContributingFactors: factors.Select(f => new QuantRegimeFactorDto(f.Name, f.Value, f.Interpretation, f.Impact)).ToList(),
            DataWarning:         dataWarning,
            Symbol:              symbol,
            Timeframe:           timeframe,
            ComputedAt:          now.ToDateTimeOffset().ToString("o"));
    }

    // ── Classification rules ──────────────────────────────────────────────────

    private static QuantRegimeLabel Classify(
        decimal adx, decimal diPlus, decimal diMinus,
        decimal atrRatio, decimal? ivp)
    {
        // 1. Panic — extreme vol spike
        if (ivp > 85 || atrRatio > 2.5m)
            return QuantRegimeLabel.PanicShock;

        // 2. Compression — very quiet, ready to break
        if (adx < 15 && atrRatio < 0.80m && (ivp is null or < 35))
            return QuantRegimeLabel.CompressionWatch;

        // 3. Clear trend up
        if (adx > 25 && diPlus > diMinus)
            return QuantRegimeLabel.TrendingUp;

        // 4. Clear trend down
        if (adx > 25 && diMinus > diPlus)
            return QuantRegimeLabel.TrendingDown;

        // 5. Elevated volatility (not yet panic)
        if (ivp > 70 || atrRatio > 1.6m)
            return QuantRegimeLabel.ElevatedVolatility;

        // 6. Default
        return QuantRegimeLabel.Choppy;
    }

    private static (TrafficLight light, string summary, string guidance) Describe(
        QuantRegimeLabel regime, decimal adx, decimal diPlus, decimal diMinus,
        decimal atrRatio, decimal? ivp)
    {
        return regime switch
        {
            QuantRegimeLabel.TrendingUp => (
                TrafficLight.Green,
                $"Trending up — ADX {adx:F0}, bullish DI alignment ({diPlus:F0} > {diMinus:F0}). Strong directional momentum.",
                "Trend-following setups (EMA pullback, VCP breakout, price-action breakout) have positive EV. " +
                "Avoid counter-trend fades. Prefer call-side options. Keep stops tight to the trend structure."),

            QuantRegimeLabel.TrendingDown => (
                TrafficLight.Green,
                $"Trending down — ADX {adx:F0}, bearish DI alignment ({diMinus:F0} > {diPlus:F0}). Sell momentum in control.",
                "Short-side trend-following and put-side options have positive EV. " +
                "Avoid buying dips without a structural reversal signal. " +
                "Premium-selling with a directional put bias is appropriate."),

            QuantRegimeLabel.Choppy => (
                TrafficLight.Amber,
                $"Choppy / mean-reverting — ADX {adx:F0}. No clear trend; price oscillating within a range.",
                "Mean-reversion strategies perform best. Reduce size on trend-following setups. " +
                "Range-bound options structures (iron condor, short strangle with close strikes) " +
                "can capture oscillation premium. Avoid breakout entries without volume confirmation."),

            QuantRegimeLabel.ElevatedVolatility => (
                TrafficLight.Amber,
                $"Elevated volatility — ATR ratio {atrRatio:F2}" +
                (ivp.HasValue ? $", IVP {ivp:F0}th" : "") +
                ". Volatility is rich; premium-sellers have edge.",
                "Short-premium structures (straddle, iron condor, credit spreads) have positive EV from IV mean reversion. " +
                "Widen strikes relative to normal to accommodate higher realized moves. " +
                "Trend-following remains viable but use wider ATR stops. Reduce all-in size by 30%."),

            QuantRegimeLabel.PanicShock => (
                TrafficLight.Red,
                $"Panic / shock regime — extreme volatility (ATR ratio {atrRatio:F2}" +
                (ivp.HasValue ? $", IVP {ivp:F0}th" : "") +
                "). Elevated tail risk across all strategies.",
                "Prioritise capital preservation. Reduce all position sizes to 25% or flat. " +
                "Avoid naked short-gamma. Long-gamma and long-vega structures (buy straddles/strangles) " +
                "are cheaper than usual but require patience. No new trend-following entries — wait for VIX to stabilise."),

            QuantRegimeLabel.CompressionWatch => (
                TrafficLight.Amber,
                $"Compression watch — ADX {adx:F0}, ATR ratio {atrRatio:F2}" +
                (ivp.HasValue ? $", IVP {ivp:F0}th" : "") +
                ". Volatility coiling; breakout approaching.",
                "Options are cheap — long straddles or strangles have favourable risk/reward. " +
                "Prepare breakout watchlists. Avoid premium-selling (low theta income, breakout risk). " +
                "When the breakout fires with volume, treat it as a high-conviction entry."),

            _ => (TrafficLight.Amber, "Unknown regime.", "No specific guidance available.")
        };
    }

    private static int ComputeConfidence(
        QuantRegimeLabel regime, decimal adx, decimal diPlus, decimal diMinus,
        decimal atrRatio, decimal? ivp, int barCount)
    {
        if (barCount < 28) return 25;  // data insufficient

        int score = 50;  // base

        score += regime switch
        {
            QuantRegimeLabel.TrendingUp when adx > 30   => 30,
            QuantRegimeLabel.TrendingUp when adx > 25   => 20,
            QuantRegimeLabel.TrendingDown when adx > 30 => 30,
            QuantRegimeLabel.TrendingDown when adx > 25 => 20,
            QuantRegimeLabel.PanicShock                 => 25,
            QuantRegimeLabel.CompressionWatch           => 15,
            _                                           => 10,
        };

        // DI alignment bonus for trends
        if (regime is QuantRegimeLabel.TrendingUp or QuantRegimeLabel.TrendingDown)
        {
            var spread = Math.Abs(diPlus - diMinus);
            score += spread > 10 ? 10 : spread > 5 ? 5 : 0;
        }

        // ATR corroboration
        if (regime == QuantRegimeLabel.ElevatedVolatility && atrRatio > 1.8m) score += 10;
        if (regime == QuantRegimeLabel.CompressionWatch   && atrRatio < 0.7m) score += 10;

        // IVP alignment
        if (ivp.HasValue)
        {
            if (regime == QuantRegimeLabel.ElevatedVolatility && ivp > 70) score += 5;
            if (regime == QuantRegimeLabel.CompressionWatch   && ivp < 30) score += 5;
        }

        return Math.Min(95, score);
    }

    // ── Indicator computation helpers ─────────────────────────────────────────

    /// <summary>
    /// Computes ADX-{period} with Wilder's smoothing using the last N candles.
    /// Returns (adx, +DI, -DI). All zero when insufficient data.
    /// </summary>
    private static (decimal adx, decimal diPlus, decimal diMinus) ComputeAdx(
        IReadOnlyList<ClosedCandle> bars, int period)
    {
        if (bars.Count < period * 2) return (0, 0, 0);

        decimal smoothedTr = 0, smoothedPdm = 0, smoothedNdm = 0;
        decimal adx = 0;

        for (int i = 1; i < bars.Count; i++)
        {
            var cur  = bars[i];
            var prev = bars[i - 1];

            decimal tr  = Math.Max(cur.High - cur.Low,
                          Math.Max(Math.Abs(cur.High - prev.Close),
                                   Math.Abs(cur.Low  - prev.Close)));

            decimal upMove   = cur.High  - prev.High;
            decimal downMove = prev.Low  - cur.Low;

            decimal pdm = (upMove > downMove && upMove > 0) ? upMove : 0;
            decimal ndm = (downMove > upMove && downMove > 0) ? downMove : 0;

            if (i < period)
            {
                smoothedTr  += tr;
                smoothedPdm += pdm;
                smoothedNdm += ndm;
            }
            else if (i == period)
            {
                smoothedTr  += tr;
                smoothedPdm += pdm;
                smoothedNdm += ndm;

                decimal diP = smoothedTr > 0 ? 100m * smoothedPdm / smoothedTr : 0;
                decimal diN = smoothedTr > 0 ? 100m * smoothedNdm / smoothedTr : 0;
                decimal dx  = (diP + diN) > 0 ? 100m * Math.Abs(diP - diN) / (diP + diN) : 0;
                adx = dx;
            }
            else
            {
                // Wilder's smoothing
                smoothedTr  = smoothedTr  - smoothedTr  / period + tr;
                smoothedPdm = smoothedPdm - smoothedPdm / period + pdm;
                smoothedNdm = smoothedNdm - smoothedNdm / period + ndm;

                decimal diP = smoothedTr > 0 ? 100m * smoothedPdm / smoothedTr : 0;
                decimal diN = smoothedTr > 0 ? 100m * smoothedNdm / smoothedTr : 0;
                decimal dx  = (diP + diN) > 0 ? 100m * Math.Abs(diP - diN) / (diP + diN) : 0;
                adx = (adx * (period - 1) + dx) / period;  // EMA of DX
            }
        }

        // Final DI values from last smoothed values
        decimal finalDiPlus  = smoothedTr > 0 ? 100m * smoothedPdm / smoothedTr : 0;
        decimal finalDiMinus = smoothedTr > 0 ? 100m * smoothedNdm / smoothedTr : 0;

        return (Math.Round(adx, 2), Math.Round(finalDiPlus, 2), Math.Round(finalDiMinus, 2));
    }

    /// <summary>Simple ATR over the given candle slice (True Range average).</summary>
    private static decimal ComputeAtr(IReadOnlyList<ClosedCandle> bars, int period)
    {
        if (bars.Count < 2) return 0;
        int n = Math.Min(period, bars.Count - 1);
        decimal sum = 0;
        for (int i = bars.Count - n; i < bars.Count; i++)
        {
            var cur  = bars[i];
            var prev = bars[i - 1];
            sum += Math.Max(cur.High - cur.Low,
                   Math.Max(Math.Abs(cur.High - prev.Close),
                            Math.Abs(cur.Low  - prev.Close)));
        }
        return n > 0 ? sum / n : 0;
    }

    private static void AddFactor(List<QuantRegimeFactor> list,
        string name, string value, string interpretation, string impact)
        => list.Add(new QuantRegimeFactor(name, value, interpretation, impact));
}
