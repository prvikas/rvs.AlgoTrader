using System.Text.Json;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.VcpSwing;

/// <summary>
/// STRAT-001: VCP (Volatility Contraction Pattern) Swing Strategy.
/// Spec: docs/STRATEGY_SPECS.md § STRAT-001
///
/// Filter:  price &gt; SMA200, SMA200 slope ≥ threshold.
/// Setup:   prior uptrend + ≥2 contractions with each depth &lt; previous (tightening base).
/// Entry:   (a) price near final contraction support (within EntryBufferPct), OR
///          (b) breakout above final contraction resistance with volume confirmation.
/// Stop:    below final contraction low.
/// Sizing:  70-80% initial; scale-in on EMA bounce handled externally by ScalingManager.
/// Exit:    close below TrendEmaFast (configurable, default 5-EMA).
/// </summary>
public class VcpSwingStrategy(VcpSwingConfig config) : IStrategy
{
    public string Name => "VcpSwing";

    public Task<SignalResult> EvaluateAsync(StrategyContext context, CancellationToken ct)
    {
        var candles = context.Candles;
        int required = Math.Max(config.Sma200Period + config.PivotBars + 2,
                                config.AtrPeriod + config.PivotBars + 2);
        if (candles.Count < required)
            return Task.FromResult(SignalResult.Skip(SkippedReason.InsufficientData,
                $"Need {required} candles, have {candles.Count}"));

        var closes = candles.Select(c => c.Close).ToArray();
        var highs   = candles.Select(c => c.High).ToArray();
        var lows    = candles.Select(c => c.Low).ToArray();
        var current = candles[^1];

        // ── SMA200 trend filter ────────────────────────────────────────────
        decimal sma200 = closes.TakeLast(config.Sma200Period).Average();
        if (current.Close <= sma200)
            return Task.FromResult(SignalResult.Hold("Price below SMA200 — no uptrend"));

        // SMA slope: compare last bar SMA200 to bar 5 days ago
        decimal sma200Prev = closes.Skip(candles.Count - config.Sma200Period - 5)
                                   .Take(config.Sma200Period).Average();
        decimal slope = (sma200 - sma200Prev) / (sma200Prev == 0 ? 1 : sma200Prev) * 100m;
        if (slope < config.MinSma200SlopePct)
            return Task.FromResult(SignalResult.Hold($"SMA200 slope {slope:F2}% below threshold {config.MinSma200SlopePct}%"));

        // ── ATR ────────────────────────────────────────────────────────────
        decimal atr = ComputeAtr(candles, config.AtrPeriod);
        if (atr == 0)
            return Task.FromResult(SignalResult.Skip(SkippedReason.InsufficientData, "ATR calculation failed"));

        // ── Pivot detection ────────────────────────────────────────────────
        // Only search within last LookbackBars (excluding current bar)
        int end   = candles.Count - 1;                         // current bar index (excluded from pivot search)
        int start = Math.Max(config.PivotBars, end - config.LookbackBars);

        var pivotHighs = new List<(int Idx, decimal Price)>();
        var pivotLows  = new List<(int Idx, decimal Price)>();

        for (int i = start; i < end - config.PivotBars; i++)
        {
            bool isHigh = true, isLow = true;
            for (int k = 1; k <= config.PivotBars; k++)
            {
                if (highs[i] < highs[i - k] || highs[i] < highs[i + k]) isHigh = false;
                if (lows[i]  > lows[i - k]  || lows[i]  > lows[i + k])  isLow  = false;
            }
            if (isHigh) pivotHighs.Add((i, highs[i]));
            if (isLow)  pivotLows.Add((i, lows[i]));
        }

        if (pivotHighs.Count < 2 || pivotLows.Count < 2)
            return Task.FromResult(SignalResult.Hold(
                $"Insufficient pivots for VCP: {pivotHighs.Count} highs, {pivotLows.Count} lows"));

        // ── Contraction analysis ───────────────────────────────────────────
        // Build alternating pairs: (swing high, subsequent swing low)
        var contractions = new List<(decimal High, decimal Low, decimal DepthPct)>();
        int hi = 0, lo = 0;
        while (hi < pivotHighs.Count && lo < pivotLows.Count)
        {
            var ph = pivotHighs[hi];
            // Find first pivot low after this pivot high
            while (lo < pivotLows.Count && pivotLows[lo].Idx <= ph.Idx) lo++;
            if (lo >= pivotLows.Count) break;

            var pl = pivotLows[lo];
            decimal depth = ph.Price == 0 ? 0 : (ph.Price - pl.Price) / ph.Price * 100m;
            contractions.Add((ph.Price, pl.Price, depth));
            hi++;
        }

        if (contractions.Count < config.MinContractions)
            return Task.FromResult(SignalResult.Hold(
                $"Only {contractions.Count} contractions found, need {config.MinContractions}"));

        // Verify each contraction is shallower than the previous
        bool tightening = true;
        for (int i = 1; i < contractions.Count; i++)
        {
            if (contractions[i].DepthPct >= contractions[i - 1].DepthPct * 0.95m)
            {
                tightening = false;
                break;
            }
        }
        if (!tightening)
            return Task.FromResult(SignalResult.Hold("Contractions not tightening — no VCP structure"));

        var last = contractions[^1];
        decimal resistance = pivotHighs[^1].Price;   // most recent swing high = breakout level
        decimal support    = last.Low;                // final contraction low
        decimal stopLoss   = support - atr * 0.15m;  // just below final contraction low

        var indicators = new Dictionary<string, decimal>
        {
            ["sma200"]     = sma200,
            ["resistance"] = resistance,
            ["support"]    = support,
            ["atr"]        = atr,
        };

        // ── Entry: breakout above resistance + volume ──────────────────────
        double avgVol = (double)candles.TakeLast(config.LookbackBars)
                                       .Average(c => (decimal)c.Volume);
        bool volumeOk = current.Volume >= avgVol * (double)config.BreakoutVolumeMultiple;

        if (current.Close > resistance && volumeOk)
        {
            var risk = current.Close - stopLoss;
            if (risk <= 0) return Task.FromResult(SignalResult.Hold("Zero/negative risk on breakout"));
            return Task.FromResult(SignalResult.Buy(
                entryPrice: current.Close,
                stopLoss:   stopLoss,
                takeProfit: current.Close + risk * config.RiskRewardRatio,
                reason:     $"VCP breakout above {resistance:F2}, depth={last.DepthPct:F1}%, Vol×{current.Volume / avgVol:F1}",
                indicatorValues: indicators));
        }

        // ── Entry: price near final contraction support ────────────────────
        decimal buffer = resistance * config.EntryBufferPct / 100m;
        if (current.Close >= support - buffer && current.Close <= support + buffer * 3)
        {
            var risk = current.Close - stopLoss;
            if (risk <= 0) return Task.FromResult(SignalResult.Hold("Zero/negative risk at support"));
            return Task.FromResult(SignalResult.Buy(
                entryPrice: current.Close,
                stopLoss:   stopLoss,
                takeProfit: current.Close + risk * config.RiskRewardRatio,
                reason:     $"VCP near support {support:F2}, depth={last.DepthPct:F1}%, {contractions.Count} contractions",
                indicatorValues: indicators));
        }

        return Task.FromResult(SignalResult.Hold(
            $"VCP base found but price {current.Close:F2} not at entry zone ({support:F2}±buffer or >{resistance:F2})",
            indicatorValues: indicators));
    }

    private static decimal ComputeAtr(IReadOnlyList<ClosedCandle> candles, int period)
    {
        if (candles.Count < period + 1) return 0;
        decimal sum = 0;
        int start = candles.Count - period;
        for (int i = start; i < candles.Count; i++)
        {
            var h = candles[i].High; var l = candles[i].Low; var pc = candles[i - 1].Close;
            sum += Math.Max(h - l, Math.Max(Math.Abs(h - pc), Math.Abs(l - pc)));
        }
        return sum / period;
    }
}

public class VcpSwingConfig
{
    public int     Sma200Period            { get; set; } = 200;
    public decimal MinSma200SlopePct       { get; set; } = 0.0m;  // % per 5 bars — 0 = flat ok
    public int     LookbackBars            { get; set; } = 60;
    public int     PivotBars               { get; set; } = 3;      // bars each side for pivot
    public int     MinContractions         { get; set; } = 2;
    public int     AtrPeriod               { get; set; } = 14;
    public decimal EntryBufferPct          { get; set; } = 0.5m;   // % of price around support
    public decimal BreakoutVolumeMultiple  { get; set; } = 1.5m;
    public decimal RiskRewardRatio         { get; set; } = 2.0m;

    public static VcpSwingConfig FromJson(string json)
        => JsonSerializer.Deserialize<VcpSwingConfig>(json) ?? new();

    public static IReadOnlyList<StrategyParamDef> GetSchema() =>
    [
        new("Sma200Period",           "SMA 200 Period",             "int",     200,   Min: 100,  Max: 300),
        new("MinSma200SlopePct",      "Min SMA200 Slope %",         "decimal", 0.0m,  Min: -1m,  Max: 2m,   Step: 0.05m, Hint: "SMA200 slope over 5 bars (%)"),
        new("LookbackBars",           "Lookback Bars",              "int",     60,    Min: 20,   Max: 200,  Hint: "Bars to search for VCP structure"),
        new("PivotBars",              "Pivot Bars Each Side",       "int",     3,     Min: 1,    Max: 10),
        new("MinContractions",        "Min Contractions",           "int",     2,     Min: 1,    Max: 5,    Hint: "Minimum VCP tightening cycles"),
        new("AtrPeriod",              "ATR Period",                 "int",     14,    Min: 5,    Max: 50),
        new("EntryBufferPct",         "Entry Buffer %",             "decimal", 0.5m,  Min: 0.1m, Max: 3.0m, Step: 0.1m),
        new("BreakoutVolumeMultiple", "Breakout Volume Multiple",   "decimal", 1.5m,  Min: 1.0m, Max: 5.0m, Step: 0.5m),
        new("RiskRewardRatio",        "Risk:Reward Ratio",          "decimal", 2.0m,  Min: 1.0m, Max: 10.0m, Step: 0.5m),
    ];
}
