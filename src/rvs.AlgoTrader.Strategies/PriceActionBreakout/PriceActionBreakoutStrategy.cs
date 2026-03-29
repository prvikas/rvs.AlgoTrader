using System.Text.Json;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.PriceActionBreakout;

/// <summary>
/// Price Action Breakout Strategy.
/// Detects breakout from a consolidation range (N-bar high/low).
/// Entry: close above N-bar high (BUY) or below N-bar low (SELL).
/// Filter: ATR must be above MinAtrMultiple * avg ATR (avoid low-volatility breakouts).
/// Filter: Volume must be above VolumeMultiple * avg volume on breakout bar.
/// Stop Loss: ATR-based below/above breakout candle.
/// Take Profit: Risk:Reward ratio applied to ATR stop.
/// </summary>
public class PriceActionBreakoutStrategy(PriceActionBreakoutConfig config) : IStrategy
{
    public string Name => "PriceActionBreakout";

    public Task<SignalResult> EvaluateAsync(StrategyContext context, CancellationToken ct)
    {
        var candles = context.Candles;

        // Need at least LookbackBars + AtrPeriod + 1 candles, plus TrendEMA warmup
        var required = Math.Max(config.LookbackBars + config.AtrPeriod + 1,
                                config.TrendEmaPeriod > 0 ? config.TrendEmaPeriod + 2 : 0);
        if (candles.Count < required)
            return Task.FromResult(SignalResult.Skip(SkippedReason.InsufficientData, "Insufficient candles for evaluation"));

        var current = candles[^1];

        // ── Trend EMA for direction filter ────────────────────────────────
        decimal[]? trendEma = null;
        if (config.TrendEmaPeriod > 0)
        {
            // Build closes without LINQ to avoid IEnumerable overhead
            var cls = new decimal[candles.Count];
            for (int ci = 0; ci < candles.Count; ci++) cls[ci] = candles[ci].Close;
            trendEma = ComputeEmaFull(cls, config.TrendEmaPeriod);
        }

        // Calculate N-bar range + avg volume over prior lookback bars (no list allocation)
        var lookbackEnd   = candles.Count - 1; // exclude current bar
        var lookbackStart = Math.Max(0, lookbackEnd - config.LookbackBars);
        var rangeHigh = decimal.MinValue;
        var rangeLow  = decimal.MaxValue;
        double volSum = 0;
        for (int i = lookbackStart; i < lookbackEnd; i++)
        {
            if (candles[i].High > rangeHigh) rangeHigh = candles[i].High;
            if (candles[i].Low  < rangeLow)  rangeLow  = candles[i].Low;
            volSum += (double)candles[i].Volume;
        }
        var lookbackCount = lookbackEnd - lookbackStart;
        var avgVolume = lookbackCount > 0 ? volSum / lookbackCount : 1d;

        // ATR calculation
        var atrValues = CalculateAtr(candles, config.AtrPeriod);
        if (atrValues.Count == 0)
            return Task.FromResult(SignalResult.Skip(SkippedReason.InsufficientData, "ATR calculation failed"));

        var currentAtr = atrValues[^1];
        // Avg ATR over last AtrPeriod values — TakeLast on List<T> is O(1) count, no allocation
        var avgAtr = atrValues.TakeLast(config.AtrPeriod).Average();

        var volumeOk = current.Volume >= avgVolume * (double)config.VolumeMultiple;

        // ATR volatility filter
        var atrOk = currentAtr >= avgAtr * config.MinAtrMultiple;

        // Consolidation check: range should not be excessively wide
        var rangeAtrRatio = (rangeHigh - rangeLow) / (currentAtr == 0 ? 1 : currentAtr);
        var consolidationOk = rangeAtrRatio <= config.MaxRangeAtrMultiple;

        // Indicator snapshot for chart overlay
        var indicators = new Dictionary<string, decimal>
        {
            ["rangeHigh"] = rangeHigh,
            ["rangeLow"]  = rangeLow,
            ["atr"]       = currentAtr,
            ["avgAtr"]    = avgAtr,
        };

        if (!atrOk)
            return Task.FromResult(SignalResult.Skip(SkippedReason.FilterFailed, "ATR below threshold"));

        if (!consolidationOk)
            return Task.FromResult(SignalResult.Skip(SkippedReason.FilterFailed, "Range too wide for consolidation"));

        // ── Volume contraction during consolidation (optional) ────────────
        // Professional filter: in a healthy VCP-like setup, volume should dry up
        // during the base. Compare second half of lookback to first half.
        if (config.RequireVolumeContraction && lookbackCount >= 4)
        {
            int half = lookbackCount / 2;
            double firstHalfVol = 0, secondHalfVol = 0;
            for (int i = lookbackStart;        i < lookbackStart + half; i++) firstHalfVol  += (double)candles[i].Volume;
            for (int i = lookbackStart + half; i < lookbackEnd;          i++) secondHalfVol += (double)candles[i].Volume;
            firstHalfVol  /= half;
            secondHalfVol /= (lookbackCount - half);
            // Allow 10% tolerance — volume drying up is the signal, not perfect symmetry
            if (secondHalfVol >= firstHalfVol * 0.9)
                return Task.FromResult(SignalResult.Hold(
                    "Volume not contracting in consolidation — base is not forming cleanly",
                    indicatorValues: indicators));
        }

        // Trend alignment: is price above or below the trend EMA?
        bool trendUp   = trendEma == null || trendEma[^1] == 0m || current.Close > trendEma[^1];
        bool trendDown = trendEma == null || trendEma[^1] == 0m || current.Close < trendEma[^1];

        // ── BUY breakout: close above N-bar high ─────────────────────────
        if (current.Close > rangeHigh && volumeOk && trendUp)
        {
            // Entry extension filter: reject if close is already too far above breakout.
            // Chasing extended breakouts destroys R:R — the stop is still at range low
            // but you've paid far above the breakout level.
            if (config.MaxEntryExtensionAtr > 0 && (current.Close - rangeHigh) > currentAtr * config.MaxEntryExtensionAtr)
                return Task.FromResult(SignalResult.Hold(
                    $"Entry too extended: {current.Close - rangeHigh:F2}pts above breakout (max {currentAtr * config.MaxEntryExtensionAtr:F2}pts)",
                    indicatorValues: indicators));

            // Structural stop: below range support (natural invalidation level).
            // Falls back to ATR-based stop when range is very narrow (rangeLow is near close).
            var rangeLowStop = rangeLow - currentAtr * 0.15m;
            var atrStop      = current.Close - currentAtr * config.AtrStopMultiple;
            var stopLoss     = Math.Max(rangeLowStop, Math.Max(atrStop, current.Low - currentAtr * 0.5m));

            // TP from actual risk — maintains the configured RRR regardless of stop method
            var risk       = current.Close - stopLoss;
            var takeProfit = current.Close + risk * config.RiskRewardRatio;

            return Task.FromResult(SignalResult.Buy(
                entryPrice: current.Close,
                stopLoss:   stopLoss,
                takeProfit: takeProfit,
                reason: $"Breakout above {rangeHigh:F2}, SL at range support {stopLoss:F2}, " +
                        $"R={risk:F2} ATR={currentAtr:F2} Vol×{current.Volume / avgVolume:F1}",
                candleTimestamp:  current.OpenTime,
                indicatorValues:  indicators));
        }

        // ── SELL breakout: close below N-bar low ─────────────────────────
        if (current.Close < rangeLow && volumeOk && config.AllowShort && trendDown)
        {
            if (config.MaxEntryExtensionAtr > 0 && (rangeLow - current.Close) > currentAtr * config.MaxEntryExtensionAtr)
                return Task.FromResult(SignalResult.Hold(
                    $"Entry too extended: {rangeLow - current.Close:F2}pts below breakdown (max {currentAtr * config.MaxEntryExtensionAtr:F2}pts)",
                    indicatorValues: indicators));

            var rangeHighStop = rangeHigh + currentAtr * 0.15m;
            var atrStop       = current.Close + currentAtr * config.AtrStopMultiple;
            var stopLoss      = Math.Min(rangeHighStop, Math.Min(atrStop, current.High + currentAtr * 0.5m));

            var risk       = stopLoss - current.Close;
            var takeProfit = current.Close - risk * config.RiskRewardRatio;

            return Task.FromResult(SignalResult.Sell(
                entryPrice: current.Close,
                stopLoss:   stopLoss,
                takeProfit: takeProfit,
                reason: $"Breakdown below {rangeLow:F2}, SL at range resistance {stopLoss:F2}, " +
                        $"R={risk:F2} ATR={currentAtr:F2}",
                candleTimestamp:  current.OpenTime,
                indicatorValues:  indicators));
        }

        return Task.FromResult(SignalResult.Hold($"Price within range [{rangeLow:F2}, {rangeHigh:F2}]", indicatorValues: indicators));
    }

    /// <summary>Full-length EMA aligned by index (result[i] = EMA at candle i, 0 while warming up).</summary>
    private static decimal[] ComputeEmaFull(decimal[] closes, int period)
    {
        var result = new decimal[closes.Length];
        if (closes.Length < period) return result;
        decimal seed = 0m;
        for (int i = 0; i < period; i++) seed += closes[i];
        result[period - 1] = seed / period;
        var k = 2m / (period + 1);
        for (int i = period; i < closes.Length; i++)
            result[i] = closes[i] * k + result[i - 1] * (1 - k);
        return result;
    }

    private static List<decimal> CalculateAtr(IReadOnlyList<ClosedCandle> candles, int period)
    {
        var trValues = new List<decimal>();
        for (int i = 1; i < candles.Count; i++)
        {
            var high = candles[i].High;
            var low = candles[i].Low;
            var prevClose = candles[i - 1].Close;
            var tr = Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));
            trValues.Add(tr);
        }

        var atrValues = new List<decimal>();
        if (trValues.Count < period) return atrValues;

        // First ATR = simple average
        var firstAtr = trValues.Take(period).Average();
        atrValues.Add(firstAtr);

        // Subsequent ATRs = Wilder's smoothing
        var prev = firstAtr;
        for (int i = period; i < trValues.Count; i++)
        {
            var atr = (prev * (period - 1) + trValues[i]) / period;
            atrValues.Add(atr);
            prev = atr;
        }

        return atrValues;
    }
}

public class PriceActionBreakoutConfig
{
    public int     LookbackBars        { get; set; } = 20;   // N-bar consolidation range
    public int     AtrPeriod           { get; set; } = 14;

    /// <summary>
    /// ATR stop multiplier. Raised from 1.5 → 2.0 so the trade survives the
    /// normal post-breakout retest before continuing in the breakout direction.
    /// </summary>
    public decimal AtrStopMultiple     { get; set; } = 2.0m;

    public decimal RiskRewardRatio     { get; set; } = 2.0m;

    /// <summary>
    /// Volume confirmation threshold. Raised from 1.5 → 2.0: genuine breakouts
    /// are accompanied by a surge in volume; 2× average filters weak breakouts
    /// that often turn into bull/bear traps.
    /// </summary>
    public decimal VolumeMultiple      { get; set; } = 2.0m;

    public decimal MinAtrMultiple      { get; set; } = 0.8m;

    /// <summary>
    /// Maximum range-to-ATR ratio. Reduced from 5.0 → 3.5: very wide ranges
    /// produce huge stop distances and poor actual R:R — keep consolidations tight.
    /// </summary>
    public decimal MaxRangeAtrMultiple { get; set; } = 3.5m;

    /// <summary>
    /// Trend EMA period. Breakouts are only taken when price is on the correct
    /// side of this EMA (up-breakouts require close > EMA; down-breakouts require
    /// close &lt; EMA). Set to 0 to disable the trend filter.
    /// Default 50: a classic medium-term trend indicator.
    /// </summary>
    public int     TrendEmaPeriod      { get; set; } = 50;

    public bool    AllowShort          { get; set; } = false;

    /// <summary>
    /// Maximum distance from the range boundary to current close, as ATR multiples.
    /// Rejects extended entries where the breakout candle has already moved far past
    /// the range edge — these have poor R:R because the stop is still at range support.
    /// Default 1.0 ATR. Set to 0 to disable.
    /// </summary>
    public decimal MaxEntryExtensionAtr { get; set; } = 1.0m;

    /// <summary>
    /// Require volume to be contracting during the consolidation (second half of lookback
    /// has lower avg volume than first half). Classic VCP characteristic — dry volume
    /// during the base signals smart-money absorption. Default false.
    /// </summary>
    public bool    RequireVolumeContraction { get; set; } = false;

    public static PriceActionBreakoutConfig FromJson(string json)
        => JsonSerializer.Deserialize<PriceActionBreakoutConfig>(json) ?? new();

    public static IReadOnlyList<StrategyParamDef> GetSchema() =>
    [
        new("LookbackBars",        "Lookback Bars",          "int",     20,    Min: 5,    Max: 100,  Hint: "Bars to measure the consolidation range"),
        new("AtrPeriod",           "ATR Period",             "int",     14,    Min: 3,    Max: 50),
        new("AtrStopMultiple",     "ATR Stop Multiple",      "decimal", 2.0m,  Min: 0.5m, Max: 5.0m, Step: 0.5m, Hint: "SL = entry ± ATR × this"),
        new("RiskRewardRatio",     "Risk:Reward Ratio",      "decimal", 2.0m,  Min: 1.0m, Max: 10.0m, Step: 0.5m),
        new("VolumeMultiple",      "Volume Multiple",        "decimal", 2.0m,  Min: 1.0m, Max: 5.0m,  Step: 0.5m, Hint: "Volume must be ≥ this × average for confirmation"),
        new("MinAtrMultiple",      "Min ATR Multiple",       "decimal", 0.8m,  Min: 0.0m, Max: 3.0m,  Step: 0.1m, Hint: "Skip if current ATR < this × average ATR"),
        new("MaxRangeAtrMultiple", "Max Range/ATR Multiple", "decimal", 3.5m,  Min: 1.0m, Max: 10.0m, Step: 0.5m, Hint: "Reject wide consolidations (range > this × ATR)"),
        new("TrendEmaPeriod",      "Trend EMA Period",       "int",     50,    Min: 0,    Max: 200,  Hint: "Only trade in EMA direction. Set to 0 to disable"),
        new("AllowShort",               "Allow Short Trades",        "bool",    false, Hint: "Enable SELL signals on downside breakouts"),
        new("MaxEntryExtensionAtr",     "Max Entry Extension (ATR)", "decimal", 1.0m,  Min: 0.0m, Max: 5.0m, Step: 0.25m, Hint: "Reject if close is > this × ATR past the range boundary. 0 = disabled"),
        new("RequireVolumeContraction", "Require Volume Contraction","bool",    false, Hint: "Second-half of lookback must have lower avg volume than first half (VCP characteristic)"),
    ];
}
