using NodaTime;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.GenericRules;

// ─────────────────────────────────────────────────────────────────────────────
// ComputedIndicator — result of computing one indicator for the current bar.
// Current  = value at [^1] (the latest closed candle).
// Previous = value at [^2] (needed for crossover detection).
// Fields   = sub-values for multi-output indicators (BB, MACD, Stoch, etc.).
// History  = all computed values (for window aggregations, percentile, slope).
// ─────────────────────────────────────────────────────────────────────────────

public class ComputedIndicator
{
    public decimal Current  { get; init; }
    public decimal Previous { get; init; }

    /// <summary>
    /// Named sub-values for multi-output indicators.
    /// BollingerBands: "upper", "middle", "lower"
    /// MACD:           "macd", "signal", "histogram"
    /// Stochastics:    "k", "d"
    /// </summary>
    public IReadOnlyDictionary<string, decimal> Fields { get; init; }
        = new Dictionary<string, decimal>();

    /// <summary>
    /// All computed values in bar order (oldest → newest).
    /// Used by window aggregation and slope evaluators.
    /// Length may be shorter than candle count (indicator warmup).
    /// </summary>
    public decimal[] History { get; init; } = [];

    //public decimal GetField(string? field)
    //    => string.IsNullOrEmpty(field) ? Current
    //       : Fields.TryGetValue(field, out var v) ? v : Current;

    public decimal GetField(string? field)
    {
        if (string.IsNullOrEmpty(field)) return Current;
        // Try exact match first, then strip everything after " —" (UI display suffix)
        if (Fields.TryGetValue(field, out var v)) return v;
        var baseKey = field.Split(" —")[0].Trim().ToLowerInvariant();
        return Fields.TryGetValue(baseKey, out var v2) ? v2 : Current;
    }

    //public decimal GetPreviousField(string? field)
    //    => string.IsNullOrEmpty(field) ? Previous
    //       : string.IsNullOrEmpty(field) ? Previous
    //       : Current;   // fallback — crossover on primary only
    
    public decimal GetPreviousField(string? field)
    {
        if (string.IsNullOrEmpty(field)) return Previous;
        var baseKey = field.Split(" —")[0].Trim().ToLowerInvariant();
        if (Fields.TryGetValue("prev_" + baseKey, out var v)) return v;
        return Previous;  // graceful fallback to primary previous value
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// IndicatorEngine — computes all indicators defined in a GenericRulesConfig
// for the candle window supplied by StrategyContext.
// All methods are static and allocation-minimising.
// ─────────────────────────────────────────────────────────────────────────────

public static class IndicatorEngine
{
    private static readonly DateTimeZone Ist =
        DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Computes all indicators in the config for the given candle window.
    /// Returns a dictionary keyed by IndicatorDef.Id.
    /// Indicators that fail to compute (e.g. insufficient data) are omitted.
    /// </summary>
    public static Dictionary<string, ComputedIndicator> ComputeAll(
        IReadOnlyList<ClosedCandle> candles,
        IReadOnlyList<IndicatorDef> indicators,
        StrategyContext? context = null)
    {
        var result = new Dictionary<string, ComputedIndicator>(
            StringComparer.OrdinalIgnoreCase);

        if (candles.Count < 2) return result;

        // Build array of closes once — reused across indicator calculations
        var closes  = BuildCloses(candles);

        foreach (var ind in indicators)
        {
            try
            {
                var computed = Compute(ind, candles, closes, context);
                if (computed != null)
                    result[ind.Id] = computed;
            }
            catch
            {
                // Don't abort the whole bar on a single indicator failure
            }
        }

        return result;
    }

    // ── Per-indicator dispatch ────────────────────────────────────────────────

    private static ComputedIndicator? Compute(
        IndicatorDef ind,
        IReadOnlyList<ClosedCandle> candles,
        decimal[] closes,
        StrategyContext? context = null)
    {
        return ind.Type.ToUpperInvariant() switch
        {
            "SMA"            => ComputeSmaIndicator(closes,  ind.GetInt("period", 20)),
            "EMA"            => ComputeEmaIndicator(closes,  ind.GetInt("period", 20)),
            "HULLMA"         => ComputeHullMaIndicator(closes, ind.GetInt("period", 20)),
            "RSI"            => ComputeRsiIndicator(closes,  ind.GetInt("period", 14)),
            "ATR"            => ComputeAtrIndicator(candles, ind.GetInt("period", 14)),
            "BOLLINGERBANDS" => ComputeBbIndicator(closes,
                                    ind.GetInt("period", 20),
                                    ind.GetDecimal("stdDev", 2.0m)),
            "MACD"           => ComputeMacdIndicator(closes,
                                    ind.GetInt("fastPeriod", 12),
                                    ind.GetInt("slowPeriod", 26),
                                    ind.GetInt("signalPeriod", 9)),
            "VWAP"           => ComputeVwapIndicator(candles),
            "CCI"            => ComputeCciIndicator(candles, ind.GetInt("period", 20)),
            "ADX"            => ComputeAdxIndicator(candles, ind.GetInt("period", 14)),
            "VOLUME"         => ComputeVolumeIndicator(candles, ind.GetInt("maPeriod", 20)),
            "STOCHASTICS"    => ComputeStochIndicator(candles,
                                    ind.GetInt("kPeriod", 14),
                                    ind.GetInt("dPeriod", 3)),
            "SESSIONFILTER"  => ComputeSessionFilter(candles,
                                    ind.GetString("startTime", "09:15"),
                                    ind.GetString("endTime",   "15:14")),
            "DAYOFWEEKFILTER"=> ComputeDayOfWeekFilter(candles,
                                    ind.GetString("days", "Mon,Tue,Wed,Thu,Fri")),
            "DONCHIANCHANNEL"=> ComputeDonchianIndicator(candles, ind.GetInt("period", 20)),
            "SUPERTREND"     => ComputeSupertrendIndicator(candles,
                                    ind.GetInt("period", 10),
                                    ind.GetDecimal("multiplier", 3m)),

            // ── Price-action / volatility / volume pattern indicators ─────────────
            "VOLUMESPIKE"      => ComputeVolumeSpikeIndicator(candles, ind.GetInt("maPeriod", 20), ind.GetDecimal("threshold", 2m)),
            "INSIDEBAR"        => ComputeInsideBarIndicator(candles),
            "ENGULFING"        => ComputeEngulfingIndicator(candles),
            "PREVHIGHLOWBREAK" => ComputePrevHighLowBreakIndicator(candles),
            "SWINGHIGHLOW"     => ComputeSwingHighLowIndicator(candles, ind.GetInt("lookback", 5)),
            "RANGEPERCENTILE"  => ComputeRangePercentileIndicator(candles, ind.GetInt("period", 50)),
            "ATRPERCENTILE"    => ComputeAtrPercentileIndicator(candles, ind.GetInt("atrPeriod", 14), ind.GetInt("period", 50)),

            // ── Options / market-context indicators ────────────────────────────
            // Values read from StrategyContext (pre-fetched by StrategyEvaluationQueue)
            // rather than computed from candles. Return null when context unavailable.
            "IVRANK"         => ComputeContextIndicator(context?.SymbolIvRank?.IvRank),
            "IVPERCENTILE"   => ComputeContextIndicator(context?.SymbolIvRank?.IvPercentile),
            "PCR"            => ComputeContextIndicator(context?.OptionChain?.PutCallRatioOI),
            "PCRCHANGE"      => ComputeContextIndicator(context?.OptionChain?.PutCallRatioChangeOI),
            "ATMIV"          => ComputeContextIndicator(context?.OptionChain?.AtmIv),
            "MAXPAIN"        => ComputeContextIndicator(context?.OptionChain?.MaxPainStrike),

            _                => null   // unknown type — silently skip
        };
    }

    // ── Context-backed indicator (single point-in-time value from StrategyContext) ──

    private static ComputedIndicator? ComputeContextIndicator(decimal? value)
    {
        if (value is null) return null;
        return new ComputedIndicator
        {
            Current  = value.Value,
            Previous = value.Value,
            History  = [value.Value],
        };
    }

    // ── SMA ───────────────────────────────────────────────────────────────────

    private static ComputedIndicator? ComputeSmaIndicator(decimal[] closes, int period)
    {
        var arr = ComputeSma(closes, period);
        if (arr.Length < 2) return null;
        return new ComputedIndicator
        {
            Current  = arr[^1],
            Previous = arr[^2],
            History  = arr,
        };
    }

    internal static decimal[] ComputeSma(decimal[] closes, int period)
    {
        if (closes.Length < period) return [];
        var count = closes.Length - period + 1;
        var result = new decimal[count];
        decimal sum = 0;
        for (int i = 0; i < period; i++) sum += closes[i];
        result[0] = sum / period;
        for (int i = 1; i < count; i++)
        {
            sum += closes[i + period - 1] - closes[i - 1];
            result[i] = sum / period;
        }
        return result;
    }

    // ── EMA ───────────────────────────────────────────────────────────────────

    private static ComputedIndicator? ComputeEmaIndicator(decimal[] closes, int period)
    {
        var arr = ComputeEma(closes, period);
        if (arr.Length < 2) return null;
        return new ComputedIndicator { Current = arr[^1], Previous = arr[^2], History = arr };
    }

    internal static decimal[] ComputeEma(decimal[] closes, int period)
    {
        if (closes.Length < period) return [];
        var count = closes.Length - period + 1;
        var result = new decimal[count];
        decimal seed = 0;
        for (int i = 0; i < period; i++) seed += closes[i];
        result[0] = seed / period;
        var k = 2m / (period + 1);
        for (int i = 1; i < count; i++)
            result[i] = closes[i + period - 1] * k + result[i - 1] * (1 - k);
        return result;
    }

    // ── Hull MA ───────────────────────────────────────────────────────────────

    private static ComputedIndicator? ComputeHullMaIndicator(decimal[] closes, int period)
    {
        // HullMA = WMA(2×WMA(n/2) − WMA(n), sqrt(n))
        var half = Math.Max(1, period / 2);
        var sqrtP = Math.Max(2, (int)Math.Round(Math.Sqrt(period)));
        var wma1 = ComputeWma(closes, half);
        var wma2 = ComputeWma(closes, period);
        if (wma1.Length < 2 || wma2.Length < 2) return null;
        // Align arrays
        var offset = wma2.Length - wma1.Length;
        var diffLen = wma1.Length;
        var diff = new decimal[diffLen];
        for (int i = 0; i < diffLen; i++)
            diff[i] = 2m * wma1[i] - wma2[i + offset];
        var hull = ComputeWma(diff, sqrtP);
        if (hull.Length < 2) return null;
        return new ComputedIndicator { Current = hull[^1], Previous = hull[^2], History = hull };
    }

    private static decimal[] ComputeWma(decimal[] data, int period)
    {
        if (data.Length < period) return [];
        var count  = data.Length - period + 1;
        var result = new decimal[count];
        decimal denom = period * (period + 1) / 2m;
        for (int i = 0; i < count; i++)
        {
            decimal sum = 0;
            for (int j = 0; j < period; j++)
                sum += data[i + j] * (j + 1);
            result[i] = sum / denom;
        }
        return result;
    }

    // ── RSI ───────────────────────────────────────────────────────────────────

    private static ComputedIndicator? ComputeRsiIndicator(decimal[] closes, int period)
    {
        var arr = ComputeRsi(closes, period);
        if (arr.Length < 2) return null;
        return new ComputedIndicator { Current = arr[^1], Previous = arr[^2], History = arr };
    }

    internal static decimal[] ComputeRsi(decimal[] closes, int period)
    {
        if (closes.Length < period + 1) return [];
        // Wilder smoothed RSI
        var result = new decimal[closes.Length - period];
        decimal avgGain = 0, avgLoss = 0;
        for (int i = 1; i <= period; i++)
        {
            var change = closes[i] - closes[i - 1];
            if (change > 0) avgGain += change; else avgLoss -= change;
        }
        avgGain /= period;
        avgLoss /= period;
        result[0] = avgLoss == 0 ? 100m : 100m - 100m / (1m + avgGain / avgLoss);
        for (int i = period + 1; i < closes.Length; i++)
        {
            var change = closes[i] - closes[i - 1];
            var gain = change > 0 ? change : 0m;
            var loss = change < 0 ? -change : 0m;
            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
            result[i - period] = avgLoss == 0 ? 100m : 100m - 100m / (1m + avgGain / avgLoss);
        }
        return result;
    }

    // ── ATR ───────────────────────────────────────────────────────────────────

    private static ComputedIndicator? ComputeAtrIndicator(IReadOnlyList<ClosedCandle> candles, int period)
    {
        var arr = ComputeAtr(candles, period);
        if (arr.Length < 2) return null;
        return new ComputedIndicator { Current = arr[^1], Previous = arr[^2], History = arr };
    }

    internal static decimal[] ComputeAtr(IReadOnlyList<ClosedCandle> candles, int period)
    {
        if (candles.Count < 2) return [];
        var trs = new decimal[candles.Count - 1];
        for (int i = 1; i < candles.Count; i++)
        {
            var h = candles[i].High; var l = candles[i].Low; var pc = candles[i - 1].Close;
            trs[i - 1] = Math.Max(h - l, Math.Max(Math.Abs(h - pc), Math.Abs(l - pc)));
        }
        if (trs.Length < period) return [];
        var atr = new decimal[trs.Length - period + 1];
        atr[0] = trs.Take(period).Average();
        for (int i = 1; i < atr.Length; i++)
            atr[i] = (atr[i - 1] * (period - 1) + trs[i + period - 1]) / period;
        return atr;
    }

    // ── Bollinger Bands ───────────────────────────────────────────────────────

    private static ComputedIndicator? ComputeBbIndicator(decimal[] closes, int period, decimal stdDev)
    {
        var (upper, mid, lower) = ComputeBb(closes, period, stdDev);
        if (mid.Length < 2) return null;
        return new ComputedIndicator
        {
            Current  = mid[^1],
            Previous = mid[^2],
            History  = mid,
            Fields   = new Dictionary<string, decimal>
            {
                ["upper"]       = upper[^1],
                ["middle"]      = mid[^1],
                ["lower"]       = lower[^1],
                ["prev_upper"]  = upper[^2],
                ["prev_middle"] = mid[^2],
                ["prev_lower"]  = lower[^2],
            },
        };
    }

    internal static (decimal[] Upper, decimal[] Mid, decimal[] Lower) ComputeBb(
        decimal[] closes, int period, decimal stdDev)
    {
        if (closes.Length < period) return ([], [], []);
        var count = closes.Length - period + 1;
        var upper = new decimal[count]; var mid = new decimal[count]; var lower = new decimal[count];
        decimal sum = 0, sumSq = 0;
        for (int i = 0; i < period; i++) { sum += closes[i]; sumSq += closes[i] * closes[i]; }
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                var add = closes[i + period - 1]; var rem = closes[i - 1];
                sum += add - rem; sumSq += add * add - rem * rem;
            }
            var avg = sum / period;
            var variance = sumSq / period - avg * avg;
            if (variance < 0m) variance = 0m;
            var sd = (decimal)Math.Sqrt((double)variance);
            mid[i] = avg; upper[i] = avg + stdDev * sd; lower[i] = avg - stdDev * sd;
        }
        return (upper, mid, lower);
    }

    // ── MACD ─────────────────────────────────────────────────────────────────

    private static ComputedIndicator? ComputeMacdIndicator(
        decimal[] closes, int fast, int slow, int signal)
    {
        var fastEma = ComputeEma(closes, fast);
        var slowEma = ComputeEma(closes, slow);
        if (fastEma.Length < 2 || slowEma.Length < 2) return null;
        // Align: slowEma is shorter; trim fastEma from the front
        var offset = fastEma.Length - slowEma.Length;
        var macdLen = slowEma.Length;
        var macdLine = new decimal[macdLen];
        for (int i = 0; i < macdLen; i++)
            macdLine[i] = fastEma[i + offset] - slowEma[i];
        var signalLine = ComputeEma(macdLine, signal);
        if (signalLine.Length < 2) return null;
        var sigOffset = macdLine.Length - signalLine.Length;
        var histLen = signalLine.Length;
        var histogram = new decimal[histLen];
        for (int i = 0; i < histLen; i++)
            histogram[i] = macdLine[i + sigOffset] - signalLine[i];
        return new ComputedIndicator
        {
            Current  = macdLine[^1],
            Previous = macdLine[^2],
            History  = macdLine,
            Fields   = new Dictionary<string, decimal>
            {
                ["macd"]           = macdLine[^1],
                ["signal"]         = signalLine[^1],
                ["histogram"]      = histogram[^1],
                ["prev_macd"]      = macdLine[^2],
                ["prev_signal"]    = signalLine[^2],
                ["prev_histogram"] = histogram[^2],
            },
        };
    }

    // ── VWAP ─────────────────────────────────────────────────────────────────

    private static ComputedIndicator? ComputeVwapIndicator(IReadOnlyList<ClosedCandle> candles)
    {
        if (candles.Count < 2) return null;
        // Daily candles have no intraday time component (midnight UTC or 00:00 local).
        // Comparing adjacent dates is fragile when the slice starts mid-session.
        var firstTime = candles[0].OpenTime.ToInstant().InZone(Ist).TimeOfDay;
        bool isDaily = firstTime == LocalTime.Midnight;
        var result = new decimal[candles.Count];
        decimal cumPV = 0, cumVol = 0;
        var sessionDate = candles[0].OpenTime.ToInstant().InZone(Ist).Date;
        for (int i = 0; i < candles.Count; i++)
        {
            var c = candles[i];
            if (!isDaily)
            {
                var d = c.OpenTime.ToInstant().InZone(Ist).Date;
                if (d != sessionDate) { cumPV = 0; cumVol = 0; sessionDate = d; }
            }
            var tp = (c.High + c.Low + c.Close) / 3m;
            cumPV += tp * c.Volume; cumVol += c.Volume;
            result[i] = cumVol > 0 ? cumPV / cumVol : c.Close;
        }
        return new ComputedIndicator
        {
            Current  = result[^1],
            Previous = result[^2],
            History  = result,
        };
    }

    // ── CCI ──────────────────────────────────────────────────────────────────

    private static ComputedIndicator? ComputeCciIndicator(IReadOnlyList<ClosedCandle> candles, int period)
    {
        if (candles.Count < period + 1) return null;
        var count = candles.Count - period + 1;
        var result = new decimal[count];
        for (int i = 0; i < count; i++)
        {
            decimal sumTp = 0;
            for (int j = i; j < i + period; j++)
                sumTp += (candles[j].High + candles[j].Low + candles[j].Close) / 3m;
            var mean = sumTp / period;
            decimal madSum = 0;
            for (int j = i; j < i + period; j++)
                madSum += Math.Abs((candles[j].High + candles[j].Low + candles[j].Close) / 3m - mean);
            var mad = madSum / period;
            var tp = (candles[i + period - 1].High + candles[i + period - 1].Low + candles[i + period - 1].Close) / 3m;
            result[i] = mad == 0 ? 0m : (tp - mean) / (0.015m * mad);
        }
        return new ComputedIndicator { Current = result[^1], Previous = result[^2], History = result };
    }

    // ── ADX ──────────────────────────────────────────────────────────────────

    private static ComputedIndicator? ComputeAdxIndicator(IReadOnlyList<ClosedCandle> candles, int period)
    {
        if (candles.Count < period * 2 + 1) return null;
        // Compute +DI, -DI, DX, ADX
        var n = candles.Count;
        var plusDm  = new decimal[n - 1];
        var minusDm = new decimal[n - 1];
        var tr      = new decimal[n - 1];
        for (int i = 1; i < n; i++)
        {
            var h = candles[i].High; var l = candles[i].Low;
            var ph = candles[i - 1].High; var pl = candles[i - 1].Low; var pc = candles[i - 1].Close;
            plusDm[i  - 1] = h - ph > pl - l && h - ph > 0 ? h - ph : 0m;
            minusDm[i - 1] = pl - l > h - ph && pl - l > 0 ? pl - l : 0m;
            tr[i - 1] = Math.Max(h - l, Math.Max(Math.Abs(h - pc), Math.Abs(l - pc)));
        }
        // Wilder smooth: collect all DX values first, then seed ADX from avg of first `period` DX values.
        // Canonical Wilder: first ADX = avg(DX[0..period-1]), subsequent = (prev*(period-1)+DX[i])/period.
        decimal smTr = 0, smPlus = 0, smMinus = 0;
        for (int i = 0; i < period; i++) { smTr += tr[i]; smPlus += plusDm[i]; smMinus += minusDm[i]; }
        var dxCount = n - 1 - period; // one DX per Wilder-smoothed step (i=period..n-2)
        if (dxCount < period) return null;
        var dxArr = new decimal[dxCount];
        for (int i = period; i < n - 1; i++)
        {
            smTr    = smTr    - smTr    / period + tr[i];
            smPlus  = smPlus  - smPlus  / period + plusDm[i];
            smMinus = smMinus - smMinus / period + minusDm[i];
            var plusDi  = smTr > 0 ? 100m * smPlus  / smTr : 0m;
            var minusDi = smTr > 0 ? 100m * smMinus / smTr : 0m;
            var dxDenom = plusDi + minusDi;
            dxArr[i - period] = dxDenom > 0 ? 100m * Math.Abs(plusDi - minusDi) / dxDenom : 0m;
        }
        var adxCount = dxCount - period + 1;
        if (adxCount < 2) return null;
        var adxArr = new decimal[adxCount];
        adxArr[0] = dxArr[..period].Average(); // canonical seed
        for (int i = 1; i < adxCount; i++)
            adxArr[i] = (adxArr[i - 1] * (period - 1) + dxArr[i + period - 1]) / period;
        return new ComputedIndicator { Current = adxArr[^1], Previous = adxArr[^2], History = adxArr };
    }

    // ── Volume ────────────────────────────────────────────────────────────────

    private static ComputedIndicator ComputeVolumeIndicator(IReadOnlyList<ClosedCandle> candles, int maPeriod)
    {
        var vols = new decimal[candles.Count];
        for (int i = 0; i < candles.Count; i++) vols[i] = candles[i].Volume;
        var ma = ComputeSma(vols, Math.Min(maPeriod, candles.Count));
        return new ComputedIndicator
        {
            Current  = vols[^1],
            Previous = candles.Count >= 2 ? vols[^2] : vols[^1],
            History  = vols,
            Fields   = new Dictionary<string, decimal>
            {
                ["volume"] = vols[^1],
                ["ma"]     = ma.Length > 0 ? ma[^1] : vols[^1],
            },
        };
    }

    // ── Stochastics ───────────────────────────────────────────────────────────

    private static ComputedIndicator? ComputeStochIndicator(
        IReadOnlyList<ClosedCandle> candles, int kPeriod, int dPeriod)
    {
        if (candles.Count < kPeriod + dPeriod) return null;
        var kLen = candles.Count - kPeriod + 1;
        var kArr = new decimal[kLen];
        for (int i = 0; i < kLen; i++)
        {
            decimal lo = candles[i].Low, hi = candles[i].High;
            for (int j = i + 1; j < i + kPeriod; j++)
            { lo = Math.Min(lo, candles[j].Low); hi = Math.Max(hi, candles[j].High); }
            var range = hi - lo;
            kArr[i] = range > 0 ? (candles[i + kPeriod - 1].Close - lo) / range * 100m : 50m;
        }
        var dArr = ComputeSma(kArr, dPeriod);
        if (dArr.Length < 2 || kArr.Length < 2) return null;
        return new ComputedIndicator
        {
            Current  = kArr[^1],
            Previous = kArr[^2],
            History  = kArr,
            Fields   = new Dictionary<string, decimal>
            {
                ["k"]      = kArr[^1],
                ["d"]      = dArr[^1],
                ["prev_k"] = kArr[^2],
                ["prev_d"] = dArr.Length >= 2 ? dArr[^2] : dArr[^1],
            },
        };
    }

    // ── SessionFilter ─────────────────────────────────────────────────────────
    // Returns 1 if current bar is INSIDE the session window, 0 if outside.

    private static ComputedIndicator ComputeSessionFilter(
        IReadOnlyList<ClosedCandle> candles, string startTime, string endTime)
    {
        var current = candles[^1];
        var barIst  = current.OpenTime.ToInstant().InZone(Ist).LocalDateTime;
        var barMins = barIst.Hour * 60 + barIst.Minute;
        var startMins = ParseTime(startTime);
        var endMins   = ParseTime(endTime);
        var inSession = barMins >= startMins && barMins <= endMins ? 1m : 0m;
        return new ComputedIndicator { Current = inSession, Previous = inSession, History = [inSession] };
    }

    private static int ParseTime(string t)
    {
        var parts = t.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m))
            return h * 60 + m;
        return 0;
    }

    // ── DayOfWeekFilter ───────────────────────────────────────────────────────
    // Returns 1 if current bar's day is in the allowed list, 0 otherwise.

    private static ComputedIndicator ComputeDayOfWeekFilter(
        IReadOnlyList<ClosedCandle> candles, string days)
    {
        var current = candles[^1];
        var dow     = current.OpenTime.ToInstant().InZone(Ist).DayOfWeek;
        var allowed = days.Split(',').Select(d => d.Trim().ToLower());
        var dayStr  = dow.ToString()[..3].ToLower();
        var ok = allowed.Any(d => d.StartsWith(dayStr)) ? 1m : 0m;
        return new ComputedIndicator { Current = ok, Previous = ok, History = [ok] };
    }

    // ── Donchian Channel ──────────────────────────────────────────────────────

    private static ComputedIndicator? ComputeDonchianIndicator(
        IReadOnlyList<ClosedCandle> candles, int period)
    {
        if (candles.Count < period + 1) return null;
        var n = candles.Count - period + 1;
        var upper = new decimal[n]; var lower = new decimal[n];
        for (int i = 0; i < n; i++)
        {
            decimal hi = candles[i].High, lo = candles[i].Low;
            for (int j = i + 1; j < i + period; j++)
            { hi = Math.Max(hi, candles[j].High); lo = Math.Min(lo, candles[j].Low); }
            upper[i] = hi; lower[i] = lo;
        }
        var mid = new decimal[n];
        for (int i = 0; i < n; i++) mid[i] = (upper[i] + lower[i]) / 2m;
        if (mid.Length < 2) return null;
        return new ComputedIndicator
        {
            Current  = mid[^1],
            Previous = mid[^2],
            History  = mid,
            Fields   = new Dictionary<string, decimal>
            {
                ["upper"]       = upper[^1],
                ["middle"]      = mid[^1],
                ["lower"]       = lower[^1],
                ["prev_upper"]  = upper[^2],
                ["prev_middle"] = mid[^2],
                ["prev_lower"]  = lower[^2],
            },
        };
    }

    // ── SuperTrend ────────────────────────────────────────────────────────────

    private static ComputedIndicator? ComputeSupertrendIndicator(
        IReadOnlyList<ClosedCandle> candles, int period, decimal multiplier)
    {
        var atr = ComputeAtr(candles, period);
        if (atr.Length < 2) return null;
        // Align atr to candles (ATR starts at index `period`)
        var atrOffset = candles.Count - atr.Length;
        var result = new decimal[atr.Length];
        var direction = new decimal[atr.Length]; // 1 = bullish, -1 = bearish
        decimal upperBand = 0, lowerBand = 0;
        bool bullish = true;
        for (int i = 0; i < atr.Length; i++)
        {
            var candleIdx = i + atrOffset;
            var hl2   = (candles[candleIdx].High + candles[candleIdx].Low) / 2m;
            var rawUpper = hl2 + multiplier * atr[i];
            var rawLower = hl2 - multiplier * atr[i];
            upperBand = i == 0 ? rawUpper : (rawUpper < upperBand || candles[candleIdx - 1].Close > upperBand ? rawUpper : upperBand);
            lowerBand = i == 0 ? rawLower : (rawLower > lowerBand || candles[candleIdx - 1].Close < lowerBand ? rawLower : lowerBand);
            //bullish = candles[candleIdx].Close > upperBand || (!bullish == false && candles[candleIdx].Close > lowerBand) ? true
            //        : candles[candleIdx].Close < lowerBand ? false : bullish;

            bullish = (candles[candleIdx].Close > upperBand || (bullish && candles[candleIdx].Close > lowerBand))
                ? true
                : (candles[candleIdx].Close < lowerBand ? false : bullish);

            result[i]    = bullish ? lowerBand : upperBand;
            direction[i] = bullish ? 1m : -1m;
        }
        if (result.Length < 2) return null;
        return new ComputedIndicator
        {
            Current  = result[^1],
            Previous = result[^2],
            History  = result,
            Fields = new Dictionary<string, decimal>
            {
                ["value"] = result[^1],
                ["direction"] = direction[^1],
                ["prev_value"] = result[^2],
                ["prev_direction"] = direction[^2],   // required by GetPreviousField for crossover
            },
        };
    }

    // ── VolumeSpike ───────────────────────────────────────────────────────────
    // Primary value = current volume / MA(volume, maPeriod). Fields: ratio, isSpike (1/0).

    private static ComputedIndicator? ComputeVolumeSpikeIndicator(
        IReadOnlyList<ClosedCandle> candles, int maPeriod, decimal threshold)
    {
        if (candles.Count < maPeriod + 1) return null;
        var vols = new decimal[candles.Count];
        for (int i = 0; i < candles.Count; i++) vols[i] = candles[i].Volume;
        var ma = ComputeSma(vols, maPeriod);
        if (ma.Length < 2) return null;

        // Build ratio history aligned to ma
        var ratioArr = new decimal[ma.Length];
        var offset = vols.Length - ma.Length;
        for (int i = 0; i < ma.Length; i++)
            ratioArr[i] = ma[i] > 0 ? vols[i + offset] / ma[i] : 0m;

        var curRatio  = ratioArr[^1];
        var prevRatio = ratioArr[^2];
        return new ComputedIndicator
        {
            Current  = curRatio,
            Previous = prevRatio,
            History  = ratioArr,
            Fields   = new Dictionary<string, decimal>
            {
                ["ratio"]   = curRatio,
                ["isSpike"] = curRatio >= threshold ? 1m : 0m,
            },
        };
    }

    // ── InsideBar ─────────────────────────────────────────────────────────────
    // Returns 1 when current bar's range is entirely within the previous bar's range, else 0.

    private static ComputedIndicator? ComputeInsideBarIndicator(IReadOnlyList<ClosedCandle> candles)
    {
        if (candles.Count < 2) return null;
        var result = new decimal[candles.Count - 1];
        for (int i = 1; i < candles.Count; i++)
        {
            var cur  = candles[i];
            var prev = candles[i - 1];
            result[i - 1] = cur.High <= prev.High && cur.Low >= prev.Low ? 1m : 0m;
        }
        if (result.Length < 2) return null;
        return new ComputedIndicator { Current = result[^1], Previous = result[^2], History = result };
    }

    // ── Engulfing ─────────────────────────────────────────────────────────────
    // Bullish engulfing: current bullish body completely covers previous bearish body → +1
    // Bearish engulfing: current bearish body completely covers previous bullish body → -1
    // Otherwise → 0. Fields: bullish (0/1), bearish (0/1).

    private static ComputedIndicator? ComputeEngulfingIndicator(IReadOnlyList<ClosedCandle> candles)
    {
        if (candles.Count < 2) return null;
        var result = new decimal[candles.Count - 1];
        for (int i = 1; i < candles.Count; i++)
        {
            var cur  = candles[i];
            var prev = candles[i - 1];
            bool curBull  = cur.Close  > cur.Open;
            bool prevBear = prev.Close < prev.Open;
            bool curBear  = cur.Close  < cur.Open;
            bool prevBull = prev.Close > prev.Open;
            // Body engulfment: current body starts below prev body low and ends above prev body high
            if (curBull && prevBear && cur.Open <= Math.Min(prev.Open, prev.Close) && cur.Close >= Math.Max(prev.Open, prev.Close))
                result[i - 1] = 1m;   // bullish engulfing
            else if (curBear && prevBull && cur.Open >= Math.Max(prev.Open, prev.Close) && cur.Close <= Math.Min(prev.Open, prev.Close))
                result[i - 1] = -1m;  // bearish engulfing
            else
                result[i - 1] = 0m;
        }
        if (result.Length < 2) return null;
        var cur1 = result[^1]; var prev1 = result[^2];
        return new ComputedIndicator
        {
            Current  = cur1,
            Previous = prev1,
            History  = result,
            Fields   = new Dictionary<string, decimal>
            {
                ["bullish"] = cur1 > 0 ? 1m : 0m,
                ["bearish"] = cur1 < 0 ? 1m : 0m,
            },
        };
    }

    // ── PrevHighLowBreak ──────────────────────────────────────────────────────
    // Returns +1 if close broke above previous bar's high, -1 if broke below previous bar's low, else 0.
    // Fields: breakHigh (0/1), breakLow (0/1).

    private static ComputedIndicator? ComputePrevHighLowBreakIndicator(IReadOnlyList<ClosedCandle> candles)
    {
        if (candles.Count < 2) return null;
        var result = new decimal[candles.Count - 1];
        for (int i = 1; i < candles.Count; i++)
        {
            var close    = candles[i].Close;
            var prevHigh = candles[i - 1].High;
            var prevLow  = candles[i - 1].Low;
            if (close > prevHigh)       result[i - 1] =  1m;
            else if (close < prevLow)   result[i - 1] = -1m;
            else                        result[i - 1] =  0m;
        }
        if (result.Length < 2) return null;
        var cur1 = result[^1]; var prev1 = result[^2];
        return new ComputedIndicator
        {
            Current  = cur1,
            Previous = prev1,
            History  = result,
            Fields   = new Dictionary<string, decimal>
            {
                ["breakHigh"] = cur1 > 0 ? 1m : 0m,
                ["breakLow"]  = cur1 < 0 ? 1m : 0m,
            },
        };
    }

    // ── SwingHighLow ──────────────────────────────────────────────────────────
    // Identifies the most recent swing high and low within the lookback window.
    // Primary = most recent swing high (or high of lookback if no swing). Fields: swingHigh, swingLow.
    // A swing high is a bar whose High is the highest in [bar-lookback, bar+lookback]; simplified here
    // to use a one-sided lookback (most recent bar can be a swing if it's the highest N bars back).

    private static ComputedIndicator? ComputeSwingHighLowIndicator(
        IReadOnlyList<ClosedCandle> candles, int lookback)
    {
        if (candles.Count < lookback + 1) return null;

        // Build rolling lookback-period high and low arrays (one-sided: highest/lowest over last N bars).
        var n = candles.Count - lookback + 1;
        var swingHighArr = new decimal[n];
        var swingLowArr  = new decimal[n];
        for (int i = 0; i < n; i++)
        {
            decimal hi = candles[i].High, lo = candles[i].Low;
            for (int j = i + 1; j < i + lookback; j++)
            { hi = Math.Max(hi, candles[j].High); lo = Math.Min(lo, candles[j].Low); }
            swingHighArr[i] = hi;
            swingLowArr[i]  = lo;
        }

        if (swingHighArr.Length < 2) return null;
        return new ComputedIndicator
        {
            Current  = swingHighArr[^1],
            Previous = swingHighArr[^2],
            History  = swingHighArr,
            Fields   = new Dictionary<string, decimal>
            {
                ["swingHigh"]      = swingHighArr[^1],
                ["swingLow"]       = swingLowArr[^1],
                ["prev_swingHigh"] = swingHighArr[^2],
                ["prev_swingLow"]  = swingLowArr[^2],
            },
        };
    }

    // ── RangePercentile ───────────────────────────────────────────────────────
    // Percentile rank (0–100) of the current bar's High-Low range over the lookback period.

    private static ComputedIndicator? ComputeRangePercentileIndicator(
        IReadOnlyList<ClosedCandle> candles, int period)
    {
        if (candles.Count < period + 1) return null;
        var ranges = new decimal[candles.Count];
        for (int i = 0; i < candles.Count; i++) ranges[i] = candles[i].High - candles[i].Low;

        var count = candles.Count - period + 1;
        var result = new decimal[count];
        for (int i = 0; i < count; i++)
        {
            var curRange = ranges[i + period - 1];
            var window   = ranges[i..(i + period)];
            var below    = window.Count(r => r < curRange);
            result[i] = (decimal)below / period * 100m;
        }
        if (result.Length < 2) return null;
        return new ComputedIndicator { Current = result[^1], Previous = result[^2], History = result };
    }

    // ── ATRPercentile ─────────────────────────────────────────────────────────
    // Percentile rank (0–100) of the current ATR value over the lookback period.

    private static ComputedIndicator? ComputeAtrPercentileIndicator(
        IReadOnlyList<ClosedCandle> candles, int atrPeriod, int period)
    {
        var atr = ComputeAtr(candles, atrPeriod);
        if (atr.Length < period + 1) return null;

        var count  = atr.Length - period + 1;
        var result = new decimal[count];
        for (int i = 0; i < count; i++)
        {
            var curAtr = atr[i + period - 1];
            var window = atr[i..(i + period)];
            var below  = window.Count(v => v < curAtr);
            result[i]  = (decimal)below / period * 100m;
        }
        if (result.Length < 2) return null;
        return new ComputedIndicator { Current = result[^1], Previous = result[^2], History = result };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static decimal[] BuildCloses(IReadOnlyList<ClosedCandle> candles)
    {
        var arr = new decimal[candles.Count];
        for (int i = 0; i < candles.Count; i++) arr[i] = candles[i].Close;
        return arr;
    }
}
