using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Batch indicator calculations. All methods operate on full arrays (backtest / warm-up path).
/// For live incremental updates, use the IncrementalEmaIndicator / IncrementalAtrIndicator.
/// </summary>
public sealed class IndicatorService : IIndicatorService
{
    public decimal[] Sma(decimal[] closes, int period)
    {
        if (closes.Length < period) return [];
        var result = new decimal[closes.Length - period + 1];
        // O(n) sliding window — one pass, no inner loop
        decimal sum = 0;
        for (var i = 0; i < period; i++) sum += closes[i];
        result[0] = sum / period;
        for (var i = period; i < closes.Length; i++)
        {
            sum += closes[i] - closes[i - period];
            result[i - period + 1] = sum / period;
        }
        return result;
    }

    public decimal[] Ema(decimal[] closes, int period)
    {
        if (closes.Length == 0) return [];
        var result = new decimal[closes.Length];
        // Wilder's smoothing: k = 1/period for Wilder, 2/(period+1) for EMA
        var k = 2m / (period + 1);
        result[0] = closes[0];
        for (var i = 1; i < closes.Length; i++)
            result[i] = closes[i] * k + result[i - 1] * (1 - k);
        return result;
    }

    public decimal[] Atr(ClosedCandle[] candles, int period)
    {
        if (candles.Length < 2) return [];
        var trs = new decimal[candles.Length - 1];
        for (var i = 1; i < candles.Length; i++)
        {
            var h = candles[i].High;
            var l = candles[i].Low;
            var prevClose = candles[i - 1].Close;
            trs[i - 1] = Math.Max(h - l, Math.Max(Math.Abs(h - prevClose), Math.Abs(l - prevClose)));
        }

        // Wilder's smoothing ATR
        if (trs.Length < period) return [];
        var atr = new decimal[trs.Length - period + 1];
        var firstAtr = trs.Take(period).Average();
        atr[0] = firstAtr;
        for (var i = 1; i < atr.Length; i++)
            atr[i] = (atr[i - 1] * (period - 1) + trs[i + period - 1]) / period;

        return atr;
    }

    public decimal[] Vwap(ClosedCandle[] candles)
    {
        if (candles.Length == 0) return [];
        var result = new decimal[candles.Length];
        decimal cumVolume = 0, cumTpVolume = 0;
        foreach (var (i, c) in candles.Select((c, i) => (i, c)))
        {
            var tp = (c.High + c.Low + c.Close) / 3m;
            cumVolume += c.Volume;
            cumTpVolume += tp * c.Volume;
            result[i] = cumVolume > 0 ? cumTpVolume / cumVolume : tp;
        }
        return result;
    }

    public (decimal[] Upper, decimal[] Mid, decimal[] Lower) BollingerBands(decimal[] closes, int period, decimal stdDev)
    {
        if (closes.Length < period) return ([], [], []);
        var count = closes.Length - period + 1;
        var upper = new decimal[count];
        var mid   = new decimal[count];
        var lower = new decimal[count];

        // O(n) sliding window using sum and sum-of-squares.
        // variance = E[x²] - (E[x])² — avoids any per-bar slice allocation.
        decimal sum = 0, sumSq = 0;
        for (var i = 0; i < period; i++) { sum += closes[i]; sumSq += closes[i] * closes[i]; }
        for (var i = 0; i < count; i++)
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
}
