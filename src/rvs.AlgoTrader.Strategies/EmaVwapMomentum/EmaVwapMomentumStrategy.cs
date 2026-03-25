using System.Text.Json;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.EmaVwapMomentum;

/// <summary>
/// EMA-VWAP Momentum Strategy — multi-indicator composite.
///
/// SIGNAL LOGIC:
/// ┌──────────────────────────────────────────────────────────────────────────┐
/// │  BUY conditions (ALL must be true):                                      │
/// │  1. Fast EMA crossed above Slow EMA on the current bar                   │
/// │  2. Close is ABOVE VWAP  (institutional bid support)                     │
/// │  3. Close is ABOVE the Bollinger Band midline (SMA) — confirmed trend    │
/// │  4. Volume is ≥ VolumeMultiple × average volume (real conviction)        │
/// │  5. ATR above MinAtrPct of close (sufficient volatility to trade)        │
/// │  6. [Optional] Option chain PCR &lt; PcrBullishThreshold (if UseOptionChain=true)
/// │                                                                          │
/// │  SELL / short conditions (mirror image — only when AllowShort=true):     │
/// │  1. Fast EMA crossed below Slow EMA                                       │
/// │  2. Close below VWAP                                                     │
/// │  3. Close below BB midline                                               │
/// │  4. Volume filter passes                                                 │
/// │  5. ATR filter passes                                                    │
/// │  6. [Optional] PCR &gt; PcrBearishThreshold                                │
/// │                                                                          │
/// │  STOP LOSS:  entry ± ATR × AtrStopMultiple                              │
/// │  TAKE PROFIT: entry ± risk × RiskRewardRatio                            │
/// └──────────────────────────────────────────────────────────────────────────┘
///
/// OPTION CHAIN FILTER (when UseOptionChain=true):
///   Reads StrategyContext.OptionChain pre-fetched by StrategyEvaluationQueue.
///   PCR &lt; PcrBullishThreshold → market biased bullish → only take BUY signals
///   PCR &gt; PcrBearishThreshold → market biased bearish → only take SELL signals
///   This prevents going long into strong resistance or short into strong support.
///
/// MULTIPLE INDICATORS USED:
///   EMA(fast) — trend direction (default 9-bar)
///   EMA(slow) — trend baseline (default 21-bar)
///   VWAP      — institutional reference price (volume-weighted)
///   Bollinger Bands (period=20, stdDev=2.0) — trend + volatility context
///   ATR(14)   — position sizing and stop placement
///   Volume    — conviction filter
///
/// RULE #18 COMPLIANCE:
///   No DB, Redis, or HTTP calls inside EvaluateAsync.
///   All data is pre-loaded into StrategyContext by the evaluation infrastructure.
/// </summary>
public class EmaVwapMomentumStrategy(EmaVwapMomentumConfig config) : IStrategy
{
    public string Name => "EmaVwapMomentum";

    public Task<SignalResult> EvaluateAsync(StrategyContext context, CancellationToken ct)
    {
        var candles = context.Candles;

        // Minimum data requirement: slow EMA period + BB period + ATR period
        var minRequired = Math.Max(config.SlowEmaPeriod, Math.Max(config.BbPeriod, config.AtrPeriod)) + 2;
        if (candles.Count < minRequired)
            return Task.FromResult(SignalResult.Skip(SkippedReason.InsufficientData,
                $"Need {minRequired} candles, have {candles.Count}"));

        // ── 1. Compute indicators ──────────────────────────────────────────

        var closes  = candles.Select(c => c.Close).ToArray();
        var volumes = candles.Select(c => (double)c.Volume).ToArray();

        var fastEma = ComputeEma(closes, config.FastEmaPeriod);
        var slowEma = ComputeEma(closes, config.SlowEmaPeriod);
        var vwap    = ComputeVwap(candles);
        var (bbUpper, bbMid, bbLower) = ComputeBollingerBands(closes, config.BbPeriod, config.BbStdDev);
        var atr     = ComputeAtr(candles, config.AtrPeriod);

        // All indicator arrays must have at least 2 values for crossover detection
        if (fastEma.Length < 2 || slowEma.Length < 2 || atr.Length < 2)
            return Task.FromResult(SignalResult.Skip(SkippedReason.InsufficientData, "Indicator warm-up not complete"));

        var current = candles[^1];

        // Current bar values
        var fastNow  = fastEma[^1];
        var fastPrev = fastEma[^2];
        var slowNow  = slowEma[^1];
        var slowPrev = slowEma[^2];
        var vwapNow  = vwap[^1];
        var bbMidNow = bbMid[^1];
        var atrNow   = atr[^1];

        // ── 2. Crossover detection ─────────────────────────────────────────

        // BUY crossover: fast crossed ABOVE slow this bar (golden cross)
        bool emaBullishCross = fastPrev <= slowPrev && fastNow > slowNow;
        // SELL crossover: fast crossed BELOW slow this bar (death cross)
        bool emaBearishCross = fastPrev >= slowPrev && fastNow < slowNow;

        // ── 3. Trend alignment filters ─────────────────────────────────────

        bool priceAboveVwap = current.Close > vwapNow;
        bool priceBelowVwap = current.Close < vwapNow;

        bool priceAboveBbMid = current.Close > bbMidNow;
        bool priceBelowBbMid = current.Close < bbMidNow;

        // ── 4. Volume filter ───────────────────────────────────────────────

        var avgVolume = volumes.TakeLast(config.SlowEmaPeriod).Average();
        bool volumeOk = current.Volume >= avgVolume * (double)config.VolumeMultiple;

        // ── 5. ATR volatility floor ────────────────────────────────────────

        // Minimum ATR as % of close — avoids trading in flat/rangebound markets
        var minAtrAbs = current.Close * config.MinAtrPct / 100m;
        bool atrOk = atrNow >= minAtrAbs;

        if (!atrOk)
            return Task.FromResult(SignalResult.Skip(SkippedReason.FilterFailed,
                $"ATR {atrNow:F2} below minimum {minAtrAbs:F2} — market too flat to trade"));

        // ── 6. Option chain filter (optional) ─────────────────────────────

        bool ocBullishBias = true;
        bool ocBearishBias = true;

        if (config.UseOptionChain && context.OptionChain != null)
        {
            var pcr = context.OptionChain.PutCallRatioOI;
            ocBullishBias = pcr < config.PcrBullishThreshold;  // low PCR = calls dominate = bullish
            ocBearishBias = pcr > config.PcrBearishThreshold;  // high PCR = puts dominate = bearish

            // Also respect OI walls: don't buy into a strong CE wall or sell through a PE wall
            var ceWall     = context.OptionChain.NearestCeResistance;
            var peWall     = context.OptionChain.NearestPeSupport;
            var nearCeWall = ceWall > 0 && current.Close >= ceWall * (1 - config.OiWallBufferPct / 100m);
            var nearPeWall = peWall > 0 && current.Close <= peWall * (1 + config.OiWallBufferPct / 100m);

            if (nearCeWall) ocBullishBias = false;  // Price near CE resistance — don't buy
            if (nearPeWall) ocBearishBias = false;  // Price near PE support — don't short
        }

        // ── 7. Generate signal ─────────────────────────────────────────────

        // BUY signal: EMA golden cross + above VWAP + above BB midline + volume + OC bias
        if (emaBullishCross && priceAboveVwap && priceAboveBbMid && volumeOk && ocBullishBias)
        {
            var sl = current.Close - atrNow * config.AtrStopMultiple;
            var risk = current.Close - sl;
            var tp = current.Close + risk * config.RiskRewardRatio;

            var diagnostics = new
            {
                FastEma        = Math.Round(fastNow, 2),
                SlowEma        = Math.Round(slowNow, 2),
                Vwap           = Math.Round(vwapNow, 2),
                BbMid          = Math.Round(bbMidNow, 2),
                Atr            = Math.Round(atrNow, 2),
                VolumeRatio    = Math.Round(current.Volume / avgVolume, 2),
                PcrAtSignal    = context.OptionChain?.PutCallRatioOI,
                MaxPain        = context.OptionChain?.MaxPainStrike,
                CeResistance   = context.OptionChain?.NearestCeResistance
            };

            return Task.FromResult(SignalResult.Buy(
                entryPrice: current.Close,
                stopLoss:   Math.Max(sl, current.Low - atrNow * 0.5m), // never below bar low
                takeProfit: tp,
                reason: $"EMA golden cross [{fastNow:F0}>{slowNow:F0}], above VWAP {vwapNow:F0}, " +
                        $"above BB mid {bbMidNow:F0}, Vol×{current.Volume / avgVolume:F1}",
                diagnosticsJson: diagnostics));
        }

        // SELL / short signal
        if (config.AllowShort && emaBearishCross && priceBelowVwap && priceBelowBbMid && volumeOk && ocBearishBias)
        {
            var sl = current.Close + atrNow * config.AtrStopMultiple;
            var risk = sl - current.Close;
            var tp = current.Close - risk * config.RiskRewardRatio;

            var diagnostics = new
            {
                FastEma        = Math.Round(fastNow, 2),
                SlowEma        = Math.Round(slowNow, 2),
                Vwap           = Math.Round(vwapNow, 2),
                BbMid          = Math.Round(bbMidNow, 2),
                Atr            = Math.Round(atrNow, 2),
                VolumeRatio    = Math.Round(current.Volume / avgVolume, 2),
                PcrAtSignal    = context.OptionChain?.PutCallRatioOI,
                MaxPain        = context.OptionChain?.MaxPainStrike,
                PeSupport      = context.OptionChain?.NearestPeSupport
            };

            return Task.FromResult(SignalResult.Sell(
                entryPrice: current.Close,
                stopLoss:   Math.Min(sl, current.High + atrNow * 0.5m), // never above bar high
                takeProfit: tp,
                reason: $"EMA death cross [{fastNow:F0}<{slowNow:F0}], below VWAP {vwapNow:F0}, " +
                        $"below BB mid {bbMidNow:F0}, Vol×{current.Volume / avgVolume:F1}",
                diagnosticsJson: diagnostics));
        }

        // Compute reason for HOLD to aid diagnostics
        var holdReason = BuildHoldReason(emaBullishCross, emaBearishCross, priceAboveVwap,
            priceAboveBbMid, volumeOk, config.AllowShort);

        return Task.FromResult(SignalResult.Hold(holdReason));
    }

    // ── Private: indicator calculations ───────────────────────────────────

    /// <summary>EMA using standard k = 2/(period+1) smoothing.</summary>
    private static decimal[] ComputeEma(decimal[] closes, int period)
    {
        if (closes.Length < period) return [];
        var result = new decimal[closes.Length];
        // Seed: SMA of first N bars
        result[period - 1] = closes.Take(period).Average();
        var k = 2m / (period + 1);
        for (int i = period; i < closes.Length; i++)
            result[i] = closes[i] * k + result[i - 1] * (1 - k);
        // Return only the populated slice (from seed index forward)
        return result.Skip(period - 1).ToArray();
    }

    /// <summary>Session VWAP using typical price (H+L+C)/3.</summary>
    private static decimal[] ComputeVwap(IReadOnlyList<ClosedCandle> candles)
    {
        if (candles.Count == 0) return [];
        var result = new decimal[candles.Count];
        decimal cumPV = 0, cumVol = 0;
        for (int i = 0; i < candles.Count; i++)
        {
            var c = candles[i];
            var tp = (c.High + c.Low + c.Close) / 3m;
            cumPV  += tp * c.Volume;
            cumVol += c.Volume;
            result[i] = cumVol > 0 ? cumPV / cumVol : c.Close;
        }
        return result;
    }

    /// <summary>Bollinger Bands: SMA ± stdDev × σ.</summary>
    private static (decimal[] Upper, decimal[] Mid, decimal[] Lower) ComputeBollingerBands(
        decimal[] closes, int period, decimal stdDev)
    {
        if (closes.Length < period) return ([], [], []);
        var count = closes.Length - period + 1;
        var upper = new decimal[count];
        var mid   = new decimal[count];
        var lower = new decimal[count];

        for (int i = 0; i < count; i++)
        {
            var slice = closes.AsSpan(i, period);
            var avg   = slice.ToArray().Average();
            var variance = slice.ToArray().Average(x => (x - avg) * (x - avg));
            var sd    = (decimal)Math.Sqrt((double)variance);
            mid[i]   = avg;
            upper[i] = avg + stdDev * sd;
            lower[i] = avg - stdDev * sd;
        }
        return (upper, mid, lower);
    }

    /// <summary>ATR using Wilder's smoothing.</summary>
    private static decimal[] ComputeAtr(IReadOnlyList<ClosedCandle> candles, int period)
    {
        if (candles.Count < 2) return [];
        var trs = new decimal[candles.Count - 1];
        for (int i = 1; i < candles.Count; i++)
        {
            var h  = candles[i].High;
            var l  = candles[i].Low;
            var pc = candles[i - 1].Close;
            trs[i - 1] = Math.Max(h - l, Math.Max(Math.Abs(h - pc), Math.Abs(l - pc)));
        }
        if (trs.Length < period) return [];

        var atr = new decimal[trs.Length - period + 1];
        atr[0] = trs.Take(period).Average();
        for (int i = 1; i < atr.Length; i++)
            atr[i] = (atr[i - 1] * (period - 1) + trs[i + period - 1]) / period;
        return atr;
    }

    private static string BuildHoldReason(
        bool emaBullish, bool emaBearish, bool aboveVwap, bool aboveBbMid, bool volumeOk, bool allowShort)
    {
        if (!emaBullish && !emaBearish)
            return "No EMA crossover on this bar";
        if (emaBullish && !aboveVwap)
            return "Bullish EMA cross but price below VWAP — not confirmed";
        if (emaBullish && !aboveBbMid)
            return "Bullish EMA cross but price below BB midline — weak trend";
        if ((emaBullish || (allowShort && emaBearish)) && !volumeOk)
            return "EMA crossover without volume confirmation — low conviction";
        return "No qualifying signal on this bar";
    }
}

/// <summary>
/// Configuration for EmaVwapMomentumStrategy.
/// All parameters stored in strategy_instances.parameters_json (CLAUDE.md Rule #20).
/// Editable via UI without restart.
/// </summary>
public class EmaVwapMomentumConfig
{
    // ── EMA settings ──────────────────────────────────────────────────────
    public int     FastEmaPeriod      { get; set; } = 9;
    public int     SlowEmaPeriod      { get; set; } = 21;

    // ── Bollinger Bands ───────────────────────────────────────────────────
    public int     BbPeriod           { get; set; } = 20;
    public decimal BbStdDev           { get; set; } = 2.0m;

    // ── ATR settings ──────────────────────────────────────────────────────
    public int     AtrPeriod          { get; set; } = 14;
    public decimal AtrStopMultiple    { get; set; } = 1.5m;  // SL = entry ± ATR × this
    public decimal MinAtrPct          { get; set; } = 0.3m;  // Min ATR as % of close (e.g. 0.3% for NIFTY ≈ 60pts)

    // ── Volume filter ─────────────────────────────────────────────────────
    public decimal VolumeMultiple     { get; set; } = 1.5m;  // Signal volume ≥ avg × this

    // ── Risk management ───────────────────────────────────────────────────
    public decimal RiskRewardRatio    { get; set; } = 2.0m;  // TP = risk × this
    public bool    AllowShort         { get; set; } = false;

    // ── Option chain integration ──────────────────────────────────────────
    public bool    UseOptionChain          { get; set; } = false;
    /// <summary>PCR below this → bullish bias (CE writing dominant).</summary>
    public decimal PcrBullishThreshold     { get; set; } = 0.8m;
    /// <summary>PCR above this → bearish bias (PE writing dominant).</summary>
    public decimal PcrBearishThreshold     { get; set; } = 1.2m;
    /// <summary>Don't trade within this % of a major OI wall strike.</summary>
    public decimal OiWallBufferPct         { get; set; } = 0.3m;

    public static EmaVwapMomentumConfig FromJson(string json)
        => JsonSerializer.Deserialize<EmaVwapMomentumConfig>(json,
               new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
           ?? new EmaVwapMomentumConfig();
}
