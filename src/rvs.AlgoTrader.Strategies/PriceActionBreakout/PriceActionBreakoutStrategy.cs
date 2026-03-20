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

        // Need at least LookbackBars + AtrPeriod + 1 candles
        var required = config.LookbackBars + config.AtrPeriod + 1;
        if (candles.Count < required)
            return Task.FromResult(SignalResult.Skip(SkippedReason.InsufficientData, "Insufficient candles for evaluation"));

        var current = candles[^1];
        var prior = candles.SkipLast(1).ToList(); // all but last

        // Calculate N-bar range (excluding current candle)
        var lookback = prior.TakeLast(config.LookbackBars).ToList();
        var rangeHigh = lookback.Max(c => c.High);
        var rangeLow = lookback.Min(c => c.Low);

        // ATR calculation
        var atrValues = CalculateAtr(candles, config.AtrPeriod);
        if (atrValues.Count == 0)
            return Task.FromResult(SignalResult.Skip(SkippedReason.InsufficientData, "ATR calculation failed"));

        var currentAtr = atrValues[^1];
        var avgAtr = atrValues.TakeLast(config.AtrPeriod).Average();

        // Volume filter
        var avgVolume = prior.TakeLast(config.LookbackBars).Average(c => (double)c.Volume);
        var volumeOk = current.Volume >= avgVolume * (double)config.VolumeMultiple;

        // ATR volatility filter
        var atrOk = currentAtr >= avgAtr * config.MinAtrMultiple;

        // Consolidation check: range should not be excessively wide
        var rangeAtrRatio = (rangeHigh - rangeLow) / (currentAtr == 0 ? 1 : currentAtr);
        var consolidationOk = rangeAtrRatio <= config.MaxRangeAtrMultiple;

        if (!atrOk)
            return Task.FromResult(SignalResult.Skip(SkippedReason.FilterFailed, "ATR below threshold"));

        if (!consolidationOk)
            return Task.FromResult(SignalResult.Skip(SkippedReason.FilterFailed, "Range too wide for consolidation"));

        // BUY breakout: close above N-bar high
        if (current.Close > rangeHigh && volumeOk)
        {
            var stopLoss = current.Close - currentAtr * config.AtrStopMultiple;
            var takeProfit = current.Close + currentAtr * config.AtrStopMultiple * config.RiskRewardRatio;

            return Task.FromResult(SignalResult.Buy(
                entryPrice: current.Close,
                stopLoss: Math.Max(stopLoss, current.Low - currentAtr * 0.5m),
                takeProfit: takeProfit,
                reason: $"Breakout above {rangeHigh:F2} (range high), ATR={currentAtr:F2}, Vol×{current.Volume / avgVolume:F1}",
                candleTimestamp: current.OpenTime));
        }

        // SELL breakout: close below N-bar low
        if (current.Close < rangeLow && volumeOk && config.AllowShort)
        {
            var stopLoss = current.Close + currentAtr * config.AtrStopMultiple;
            var takeProfit = current.Close - currentAtr * config.AtrStopMultiple * config.RiskRewardRatio;

            return Task.FromResult(SignalResult.Sell(
                entryPrice: current.Close,
                stopLoss: Math.Min(stopLoss, current.High + currentAtr * 0.5m),
                takeProfit: takeProfit,
                reason: $"Breakout below {rangeLow:F2} (range low), ATR={currentAtr:F2}",
                candleTimestamp: current.OpenTime));
        }

        return Task.FromResult(SignalResult.Hold($"Price within range [{rangeLow:F2}, {rangeHigh:F2}]"));
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
    public int LookbackBars { get; set; } = 20;        // N-bar range for breakout
    public int AtrPeriod { get; set; } = 14;           // ATR period
    public decimal AtrStopMultiple { get; set; } = 1.5m; // Stop = entry ± ATR × this
    public decimal RiskRewardRatio { get; set; } = 2.0m; // TP = stop distance × this
    public decimal VolumeMultiple { get; set; } = 1.5m;  // Breakout vol must be > avg × this
    public decimal MinAtrMultiple { get; set; } = 0.8m;  // Current ATR must be > avg ATR × this
    public decimal MaxRangeAtrMultiple { get; set; } = 5.0m; // Max range/ATR ratio
    public bool AllowShort { get; set; } = false;       // Allow short signals (MIS only)

    public static PriceActionBreakoutConfig FromJson(string json)
        => JsonSerializer.Deserialize<PriceActionBreakoutConfig>(json) ?? new();
}
