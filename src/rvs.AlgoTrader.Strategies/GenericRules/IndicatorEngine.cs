using NodaTime;
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

    public decimal GetField(string? field)
        => string.IsNullOrEmpty(field) ? Current
           : Fields.TryGetValue(field, out var v) ? v : Current;

    public decimal GetPreviousField(string? field)
        => string.IsNullOrEmpty(field) ? Previous
           : string.IsNullOrEmpty(field) ? Previous
           : Current;   // fallback — crossover on primary only
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
        IReadOnlyList<IndicatorDef> indicators)
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
                var computed = Compute(ind, candles, closes);
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
        decimal[] closes)
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
            _                => null   // unknown type — silently skip
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
                ["upper"]  = upper[^1],
                ["middle"] = mid[^1],
                ["lower"]  = lower[^1],
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
                ["macd"]      = macdLine[^1],
                ["signal"]    = signalLine[^1],
                ["histogram"] = histogram[^1],
            },
        };
    }

    // ── VWAP ─────────────────────────────────────────────────────────────────

    private static ComputedIndicator? ComputeVwapIndicator(IReadOnlyList<ClosedCandle> candles)
    {
        if (candles.Count < 2) return null;
        bool isDaily = candles[0].OpenTime.ToInstant().InZone(Ist).Date !=
                       candles[1].OpenTime.ToInstant().InZone(Ist).Date;
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
        // Wilder smooth
        decimal smTr = 0, smPlus = 0, smMinus = 0;
        for (int i = 0; i < period; i++) { smTr += tr[i]; smPlus += plusDm[i]; smMinus += minusDm[i]; }
        var adxCount = n - 2 * period;
        if (adxCount < 1) return null;
        var adxArr = new decimal[adxCount];
        decimal prevDx = 0;
        for (int i = period; i < n - 1; i++)
        {
            smTr = smTr - smTr / period + tr[i];
            smPlus  = smPlus  - smPlus  / period + plusDm[i];
            smMinus = smMinus - smMinus / period + minusDm[i];
            var plusDi  = smTr > 0 ? 100m * smPlus  / smTr : 0m;
            var minusDi = smTr > 0 ? 100m * smMinus / smTr : 0m;
            var dxDenom = plusDi + minusDi;
            var dx = dxDenom > 0 ? 100m * Math.Abs(plusDi - minusDi) / dxDenom : 0m;
            var idx = i - period;
            if (idx == 0) { adxArr[0] = dx; }
            else if (idx < adxCount) { adxArr[idx] = (adxArr[idx - 1] * (period - 1) + dx) / period; }
        }
        if (adxArr.Length < 2) return null;
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
                ["k"] = kArr[^1],
                ["d"] = dArr[^1],
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
                ["upper"]  = upper[^1],
                ["middle"] = mid[^1],
                ["lower"]  = lower[^1],
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
            bullish = candles[candleIdx].Close > upperBand || (!bullish == false && candles[candleIdx].Close > lowerBand) ? true
                    : candles[candleIdx].Close < lowerBand ? false : bullish;
            result[i]    = bullish ? lowerBand : upperBand;
            direction[i] = bullish ? 1m : -1m;
        }
        if (result.Length < 2) return null;
        return new ComputedIndicator
        {
            Current  = result[^1],
            Previous = result[^2],
            History  = result,
            Fields   = new Dictionary<string, decimal>
            {
                ["value"]     = result[^1],
                ["direction"] = direction[^1],
            },
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static decimal[] BuildCloses(IReadOnlyList<ClosedCandle> candles)
    {
        var arr = new decimal[candles.Count];
        for (int i = 0; i < candles.Count; i++) arr[i] = candles[i].Close;
        return arr;
    }
}
