using System.Text.Json;
using NodaTime;
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
    // ClosedCandle.OpenTime is ZonedDateTime in UTC (constructed with DateTimeZone.Utc in
    // CandleRepository.ToClosedCandle). All IST-sensitive operations must re-zone to IST
    // explicitly — never use .LocalDateTime or .Date directly on an Instant/ZonedDateTime.
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    public string Name => "EmaVwapMomentum";
    public int MinWarmupBars => Math.Max(config.SlowEmaPeriod, Math.Max(config.BbPeriod, config.AtrPeriod)) + 5;

    public Task<SignalResult> EvaluateAsync(StrategyContext context, CancellationToken ct)
    {
        var candles = context.Candles;

        // Minimum data requirement: slow EMA period + BB period + ATR period
        var minRequired = Math.Max(config.SlowEmaPeriod, Math.Max(config.BbPeriod, config.AtrPeriod)) + 2;
        if (candles.Count < minRequired)
            return Task.FromResult(SignalResult.Skip(SkippedReason.InsufficientData,
                $"Need {minRequired} candles, have {candles.Count}"));

        // ── 1. Compute indicators ──────────────────────────────────────────

        // Build closes array without LINQ
        var closes = new decimal[candles.Count];
        for (int ci = 0; ci < candles.Count; ci++) closes[ci] = candles[ci].Close;

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
        var fastNow    = fastEma[^1];
        var fastPrev   = fastEma[^2];
        var slowNow    = slowEma[^1];
        var slowPrev   = slowEma[^2];
        var vwapNow    = vwap[^1];
        var bbMidNow   = bbMid[^1];
        var bbUpperNow = bbUpper.Length > 0 ? bbUpper[^1] : 0m;
        var bbLowerNow = bbLower.Length > 0 ? bbLower[^1] : 0m;
        var atrNow     = atr[^1];

        // Indicator snapshot for chart overlay — attached to every return path
        var indicators = new Dictionary<string, decimal>
        {
            [$"ema{config.FastEmaPeriod}"] = fastNow,
            [$"ema{config.SlowEmaPeriod}"] = slowNow,
            ["vwap"]    = vwapNow,
            ["bbUpper"] = bbUpperNow,
            ["bbMid"]   = bbMidNow,
            ["bbLower"] = bbLowerNow,
            ["atr"]     = atrNow,
        };

        // ── 2. Session detection & intraday time filters ──────────────────
        // For daily charts todayStart stays at 0 — logic is harmless.
        var today = current.OpenTime.ToInstant().InZone(Ist).Date;
        int todayStart = candles.Count - 1;
        while (todayStart > 0 && candles[todayStart - 1].OpenTime.ToInstant().InZone(Ist).Date == today)
            todayStart--;
        int barsIntoSession = candles.Count - 1 - todayStart;

        // Skip first N bars — opening price discovery is high-noise
        if (config.SessionStartBars > 0 && barsIntoSession < config.SessionStartBars)
            return Task.FromResult(SignalResult.Hold(
                $"Opening range — skipping first {config.SessionStartBars} bars",
                indicatorValues: indicators));

        // No-trade cutoff: avoid new entries near session close
        // NoTradeAfterMinutes: minutes from midnight (e.g. 900 = 15:00 IST)
        if (config.NoTradeAfterMinutes > 0)
        {
            var barIst     = current.OpenTime.ToInstant().InZone(Ist).LocalDateTime;
            var barMinutes = barIst.Hour * 60 + barIst.Minute;
            if (barMinutes >= config.NoTradeAfterMinutes)
                return Task.FromResult(SignalResult.Hold(
                    $"Past no-trade cutoff ({config.NoTradeAfterMinutes / 60}:{config.NoTradeAfterMinutes % 60:D2} IST)",
                    indicatorValues: indicators));
        }

        // ── 3. Crossover detection ─────────────────────────────────────────

        // BUY crossover: fast crossed ABOVE slow this bar (golden cross)
        bool emaBullishCross = fastPrev <= slowPrev && fastNow > slowNow;
        // SELL crossover: fast crossed BELOW slow this bar (death cross)
        bool emaBearishCross = fastPrev >= slowPrev && fastNow < slowNow;

        // ── 4. Trend alignment filters ─────────────────────────────────────

        bool priceAboveVwap = current.Close > vwapNow;
        bool priceBelowVwap = current.Close < vwapNow;

        bool priceAboveBbMid = current.Close > bbMidNow;
        bool priceBelowBbMid = current.Close < bbMidNow;

        // ── 5. Session-aware volume filter ────────────────────────────────
        // Use today's bars (capped at SlowEmaPeriod) — prevents multi-day avg
        // from masking intraday volume surges near the open.
        var volStart = Math.Max(todayStart, candles.Count - config.SlowEmaPeriod);
        double volSum = 0;
        int volCount = 0;
        for (int vi = volStart; vi < candles.Count; vi++) { volSum += (double)candles[vi].Volume; volCount++; }
        var avgVolume = volCount > 0 ? volSum / volCount : 1d;
        bool volumeOk = current.Volume >= avgVolume * (double)config.VolumeMultiple;

        // ── 6. ATR volatility floor ────────────────────────────────────────

        // Minimum ATR as % of close — avoids trading in flat/rangebound markets
        var minAtrAbs = current.Close * config.MinAtrPct / 100m;
        bool atrOk = atrNow >= minAtrAbs;

        if (!atrOk)
            return Task.FromResult(SignalResult.Skip(SkippedReason.FilterFailed,
                $"ATR {atrNow:F2} below minimum {minAtrAbs:F2} — market too flat to trade"));

        // ── 7. Option chain filter (optional) ─────────────────────────────

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

        // ── 8. Pullback-to-EMA confirmation (optional) ────────────────────
        //
        // A raw EMA crossover is lagging — entry is often at the worst price.
        // Requiring that the PREVIOUS bar's low touched (or nearly touched) the fast EMA
        // means we enter on a confirmed "bounce off EMA" rather than chasing the initial cross.
        // config.RequirePullbackToEma = false disables this check for strategies that want
        // to take every crossover without the bounce filter.

        bool prevBarBouncedFromEma = true;
        if (config.RequirePullbackToEma && candles.Count >= 2)
        {
            var prevBar    = candles[^2];
            var distToEma  = Math.Abs(prevBar.Low - fastPrev);  // how close prior bar came to fast EMA
            prevBarBouncedFromEma = distToEma <= atrNow * config.PullbackAtrFactor;
        }

        // ── 9. Generate signal ─────────────────────────────────────────────

        // BUY signal: EMA golden cross + above VWAP + above BB midline + volume + OC bias
        //             + optional: previous bar bounced off fast EMA (entry confirmation)
        if (emaBullishCross && priceAboveVwap && priceAboveBbMid && volumeOk && ocBullishBias
            && prevBarBouncedFromEma)
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
                diagnosticsJson: diagnostics,
                indicatorValues: indicators));
        }

        // SELL / short signal — same pullback confirmation on the upside
        bool prevBarBouncedFromEmaShort = true;
        if (config.RequirePullbackToEma && candles.Count >= 2)
        {
            var prevBar   = candles[^2];
            var distToEma = Math.Abs(prevBar.High - fastPrev);
            prevBarBouncedFromEmaShort = distToEma <= atrNow * config.PullbackAtrFactor;
        }

        if (config.AllowShort && emaBearishCross && priceBelowVwap && priceBelowBbMid && volumeOk && ocBearishBias
            && prevBarBouncedFromEmaShort)
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
                diagnosticsJson: diagnostics,
                indicatorValues: indicators));
        }

        // Compute reason for HOLD to aid diagnostics
        var holdReason = BuildHoldReason(emaBullishCross, emaBearishCross, priceAboveVwap,
            priceAboveBbMid, volumeOk, config.AllowShort);

        return Task.FromResult(SignalResult.Hold(holdReason, indicatorValues: indicators));
    }

    // ── Private: indicator calculations ───────────────────────────────────

    /// <summary>EMA using standard k = 2/(period+1) smoothing.</summary>
    private static decimal[] ComputeEma(decimal[] closes, int period)
    {
        if (closes.Length < period) return [];
        // Pre-allocate exactly the right size — avoids the over-alloc + Skip().ToArray() pattern
        var count  = closes.Length - period + 1;
        var result = new decimal[count];
        // Seed: manual sum avoids LINQ Take().Average() allocation
        decimal seed = 0;
        for (int i = 0; i < period; i++) seed += closes[i];
        result[0] = seed / period;
        var k = 2m / (period + 1);
        for (int i = 1; i < count; i++)
            result[i] = closes[i + period - 1] * k + result[i - 1] * (1 - k);
        return result;
    }

    /// <summary>
    /// Daily session VWAP using typical price (H+L+C)/3.
    /// Resets accumulator at each new trading day — without this, VWAP computed
    /// over multi-day candle history is meaningless for intraday signals.
    /// </summary>
    private static decimal[] ComputeVwap(IReadOnlyList<ClosedCandle> candles)
    {
        if (candles.Count == 0) return [];
        var result = new decimal[candles.Count];
        decimal cumPV = 0, cumVol = 0;
        var sessionDate = candles[0].OpenTime.ToInstant().InZone(Ist).Date;
        for (int i = 0; i < candles.Count; i++)
        {
            var c = candles[i];
            // Reset at each new IST session day
            var candleDate = c.OpenTime.ToInstant().InZone(Ist).Date;
            if (candleDate != sessionDate)
            {
                cumPV = 0;
                cumVol = 0;
                sessionDate = candleDate;
            }
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

        // O(n) sliding window using sum and sum-of-squares — no per-bar array allocation.
        // variance = E[x²] - (E[x])²
        decimal sum = 0, sumSq = 0;
        for (int i = 0; i < period; i++) { sum += closes[i]; sumSq += closes[i] * closes[i]; }
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                var add = closes[i + period - 1];
                var rem = closes[i - 1];
                sum   += add - rem;
                sumSq += add * add - rem * rem;
            }
            var avg      = sum / period;
            var variance = sumSq / period - avg * avg;
            if (variance < 0m) variance = 0m; // guard against floating-point drift
            var sd       = (decimal)Math.Sqrt((double)variance);
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
    /// <summary>
    /// ATR stop multiplier. Raised from 1.5 → 2.0 to give trades more room to breathe.
    /// Tight stops get hit on normal intraday noise before the trend develops.
    /// </summary>
    public decimal AtrStopMultiple    { get; set; } = 2.0m;
    public decimal MinAtrPct          { get; set; } = 0.3m;  // Min ATR as % of close

    // ── Volume filter ─────────────────────────────────────────────────────
    public decimal VolumeMultiple     { get; set; } = 1.5m;  // Signal volume ≥ avg × this

    // ── Risk management ───────────────────────────────────────────────────
    /// <summary>
    /// RRR raised from 2.0 → 2.5 to compensate for the wider ATR stop.
    /// With a 2× ATR stop the required reward must be proportionally larger.
    /// </summary>
    public decimal RiskRewardRatio    { get; set; } = 2.5m;
    public bool    AllowShort         { get; set; } = false;

    // ── Pullback-to-EMA filter ─────────────────────────────────────────────
    /// <summary>
    /// When true, only take signals where the previous bar's low (buy) or high (sell)
    /// came within PullbackAtrFactor × ATR of the fast EMA.
    /// Filters out "chasing" entries where price has already moved far from the EMA.
    ///
    /// E2 fix: default changed to false. On daily data, at the moment of a golden cross
    /// price has already risen 1–3 ATR above the fast EMA, so PullbackAtrFactor=0.5
    /// can never be satisfied — this produced 0 BUY signals on any daily backtest.
    /// Enable only on intraday timeframes where EMA bounce entries are meaningful.
    /// </summary>
    public bool    RequirePullbackToEma  { get; set; } = false;
    /// <summary>Max distance from fast EMA (as a multiple of ATR) that qualifies as a pullback.
    /// Raised from 0.5 to 2.0 — on intraday data 0.5 ATR is still very tight; 2.0 allows
    /// normal bar ranges while still filtering out extreme extended entries.</summary>
    public decimal PullbackAtrFactor     { get; set; } = 2.0m;

    // ── Session / time filters ────────────────────────────────────────────
    /// <summary>
    /// Skip the first N bars of each session (opening price discovery is noisy).
    /// Default 3 = skip 09:15, 09:20, 09:25 on a 5-min chart.
    /// Set to 0 to disable.
    /// </summary>
    public int     SessionStartBars       { get; set; } = 3;

    /// <summary>
    /// Do not open new positions at or after this time (minutes from midnight, IST).
    /// Default 900 = 15:00 IST. Prevents entries that cannot close before market end.
    /// Set to 0 to disable.
    /// </summary>
    public int     NoTradeAfterMinutes    { get; set; } = 900;

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

    public static IReadOnlyList<StrategyParamDef> GetSchema() =>
    [
        new("FastEmaPeriod",         "Fast EMA Period",        "int",     9,     Min: 3,    Max: 50),
        new("SlowEmaPeriod",         "Slow EMA Period",        "int",     21,    Min: 5,    Max: 200),
        new("BbPeriod",              "Bollinger Band Period",  "int",     20,    Min: 5,    Max: 50),
        new("BbStdDev",              "BB Std Deviation",       "decimal", 2.0m,  Min: 1.0m, Max: 4.0m,  Step: 0.5m),
        new("AtrPeriod",             "ATR Period",             "int",     14,    Min: 3,    Max: 50),
        new("AtrStopMultiple",       "ATR Stop Multiple",      "decimal", 2.0m,  Min: 0.5m, Max: 5.0m,  Step: 0.5m, Hint: "SL = entry ± ATR × this"),
        new("MinAtrPct",             "Min ATR % of Price",     "decimal", 0.3m,  Min: 0.0m, Max: 5.0m,  Step: 0.1m, Hint: "Skip signal if market is too flat"),
        new("VolumeMultiple",        "Volume Filter Multiple", "decimal", 1.5m,  Min: 1.0m, Max: 5.0m,  Step: 0.5m, Hint: "Volume must be ≥ this × avg to confirm signal"),
        new("RiskRewardRatio",       "Risk:Reward Ratio",      "decimal", 2.5m,  Min: 1.0m, Max: 10.0m, Step: 0.5m),
        new("AllowShort",            "Allow Short Trades",     "bool",    false),
        new("RequirePullbackToEma",  "Require Pullback to EMA","bool",    false, Hint: "Only enter when prior bar touched fast EMA (intraday use only — blocks all signals on daily data)"),
        new("PullbackAtrFactor",     "Pullback ATR Factor",    "decimal", 2.0m,  Min: 0.5m, Max: 5.0m,  Step: 0.5m, Hint: "Max distance from fast EMA as ATR multiple"),
        new("UseOptionChain",        "Option Chain PCR Filter","bool",    false, Hint: "Requires index instrument — uses PCR as directional bias filter"),
        new("PcrBullishThreshold",   "PCR Bullish Threshold",  "decimal", 0.8m,  Min: 0.3m, Max: 1.5m,  Step: 0.1m, Hint: "PCR < this → bullish bias → only allow BUY"),
        new("PcrBearishThreshold",   "PCR Bearish Threshold",  "decimal", 1.2m,  Min: 0.5m, Max: 3.0m,  Step: 0.1m, Hint: "PCR > this → bearish bias → only allow SELL"),
        new("OiWallBufferPct",       "OI Wall Buffer %",       "decimal", 0.3m,  Min: 0.1m, Max: 5.0m,  Step: 0.1m, Hint: "Suppress signal if price is within this % of a max-OI strike"),
        new("SessionStartBars",      "Session Start Buffer",   "int",     3,     Min: 0,    Max: 12,               Hint: "Skip first N bars each session (opening noise). 0 = disabled"),
        new("NoTradeAfterMinutes",   "No Trade After (min)",   "int",     900,   Min: 0,    Max: 1110,             Hint: "Minutes from midnight IST — e.g. 900 = 15:00. 0 = disabled"),
    ];
}
