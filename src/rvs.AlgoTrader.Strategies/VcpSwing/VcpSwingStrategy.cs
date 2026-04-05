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
    public int MinWarmupBars
    {
        get
        {
            int base_ = config.Sma200Period + config.PivotBars + 5;
            if (config.TrendTemplateEnabled)
                base_ = Math.Max(base_, config.Sma150Period + config.PivotBars + 5);
            if (config.FiftyTwoWeekHighFilterEnabled || config.FiftyTwoWeekLowFilterEnabled)
                base_ = Math.Max(base_, 252 + config.PivotBars + 2);
            return base_;
        }
    }

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

        // ── Market breadth filter (STRAT-001 spec: % above SMA) ───────────
        if (config.BreadthFilterEnabled && context.BreadthPct200Sma.HasValue)
        {
            if (context.BreadthPct200Sma.Value < config.MinBreadthPct200Sma)
                return Task.FromResult(SignalResult.Hold(
                    $"Market breadth {context.BreadthPct200Sma:F1}% < {config.MinBreadthPct200Sma}% — broad market weak, no VCP entries"));
        }

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

        // ── Minervini Trend Template ───────────────────────────────────────
        if (config.TrendTemplateEnabled)
        {
            if (candles.Count >= config.Sma150Period + 2)
            {
                decimal sma150 = closes.TakeLast(config.Sma150Period).Average();

                // Condition 1: price > SMA150
                if (current.Close <= sma150)
                    return Task.FromResult(SignalResult.Hold(
                        $"Trend Template: price {current.Close:F2} ≤ SMA150 {sma150:F2}"));

                // Condition 2: SMA150 > SMA200
                if (config.RequireSma150AboveSma200 && sma150 <= sma200)
                    return Task.FromResult(SignalResult.Hold(
                        $"Trend Template: SMA150 {sma150:F2} ≤ SMA200 {sma200:F2} — no alignment"));
            }

            if (candles.Count >= config.Sma50Period + 2)
            {
                decimal sma50  = closes.TakeLast(config.Sma50Period).Average();
                decimal sma150 = candles.Count >= config.Sma150Period + 2
                    ? closes.TakeLast(config.Sma150Period).Average() : sma200;

                // Condition 3: SMA50 > SMA150
                if (config.RequireSma50AboveSma150 && sma50 <= sma150)
                    return Task.FromResult(SignalResult.Hold(
                        $"Trend Template: SMA50 {sma50:F2} ≤ SMA150 {sma150:F2}"));

                // Condition 4: price > SMA50
                if (config.RequirePriceAboveSma50 && current.Close <= sma50)
                    return Task.FromResult(SignalResult.Hold(
                        $"Trend Template: price {current.Close:F2} ≤ SMA50 {sma50:F2}"));
            }
        }

        // ── 52-week high / low filters ─────────────────────────────────────
        int barsFor52W = Math.Min(252, candles.Count);
        decimal high52w = highs.TakeLast(barsFor52W).Max();
        decimal low52w  = lows.TakeLast(barsFor52W).Min();

        if (config.FiftyTwoWeekHighFilterEnabled && barsFor52W >= 50)
        {
            decimal pctFromHigh = (high52w - current.Close) / (high52w == 0 ? 1 : high52w) * 100m;
            if (pctFromHigh > config.MaxPctFrom52WeekHigh)
                return Task.FromResult(SignalResult.Hold(
                    $"Price is {pctFromHigh:F1}% below 52-week high (max allowed: {config.MaxPctFrom52WeekHigh}%)"));
        }

        if (config.FiftyTwoWeekLowFilterEnabled && barsFor52W >= 50)
        {
            decimal pctAboveLow = low52w == 0 ? 0 : (current.Close - low52w) / low52w * 100m;
            if (pctAboveLow < config.MinPctAbove52WeekLow)
                return Task.FromResult(SignalResult.Hold(
                    $"Price is only {pctAboveLow:F1}% above 52-week low (min required: {config.MinPctAbove52WeekLow}%)"));
        }

        // ── Price momentum (RS proxy) ──────────────────────────────────────
        if (config.RsFilterEnabled && candles.Count >= 252)
        {
            decimal priceNow = current.Close;
            decimal price12MAgo = closes[candles.Count - 252];
            decimal roc12M = price12MAgo == 0 ? 0 : (priceNow - price12MAgo) / price12MAgo * 100m;
            if (roc12M < config.MinRsRatingPct)
                return Task.FromResult(SignalResult.Hold(
                    $"12-month price ROC {roc12M:F1}% below minimum RS threshold {config.MinRsRatingPct}%"));
        }

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

        if (config.TrendTemplateEnabled && candles.Count >= config.Sma150Period + 2)
            indicators["sma150"] = closes.TakeLast(config.Sma150Period).Average();
        if (config.TrendTemplateEnabled && candles.Count >= config.Sma50Period + 2)
            indicators["sma50"] = closes.TakeLast(config.Sma50Period).Average();

        // ── Entry: breakout above resistance + volume ──────────────────────
        double avgVol = (double)candles.TakeLast(config.LookbackBars)
                                       .Average(c => (decimal)c.Volume);
        bool volumeOk = current.Volume >= avgVol * (double)config.BreakoutVolumeMultiple;

        // ── Volume dry-up check (base health gate — applied before both entries) ──
        if (config.VolumeDryUpEnabled && config.VolumeDryUpBars > 0)
        {
            int dryBars = Math.Min(config.VolumeDryUpBars, candles.Count - 1);
            double recentAvgVol = (double)candles.TakeLast(dryBars).Average(c => (decimal)c.Volume);
            double thresholdVol = avgVol * (double)(config.VolumeDryUpThresholdPct / 100m);
            if (recentAvgVol > thresholdVol)
                return Task.FromResult(SignalResult.Hold(
                    $"Volume not drying up: recent {recentAvgVol:F0} > threshold {thresholdVol:F0} ({config.VolumeDryUpThresholdPct}% of avg)",
                    indicatorValues: indicators));
        }

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
    // ── VCP Setup ──────────────────────────────────────────────────────────────
    public int     Sma200Period            { get; set; } = 200;
    public decimal MinSma200SlopePct       { get; set; } = 0.0m;
    public int     LookbackBars            { get; set; } = 60;
    public int     PivotBars               { get; set; } = 3;
    public int     MinContractions         { get; set; } = 2;
    public int     AtrPeriod               { get; set; } = 14;
    public decimal EntryBufferPct          { get; set; } = 0.5m;
    public decimal BreakoutVolumeMultiple  { get; set; } = 1.5m;
    public decimal RiskRewardRatio         { get; set; } = 2.0m;

    // ── Market Breadth ─────────────────────────────────────────────────────────
    public bool    BreadthFilterEnabled  { get; set; } = true;
    public decimal MinBreadthPct200Sma   { get; set; } = 40m;

    // ── Trend Template (Minervini) ─────────────────────────────────────────────
    /// <summary>Apply Minervini Trend Template MA alignment checks.</summary>
    public bool    TrendTemplateEnabled       { get; set; } = true;
    public int     Sma150Period               { get; set; } = 150;
    public int     Sma50Period                { get; set; } = 50;
    /// <summary>Require SMA150 > SMA200 (aligned uptrend).</summary>
    public bool    RequireSma150AboveSma200   { get; set; } = true;
    /// <summary>Require SMA50 > SMA150 (short-term trend leading).</summary>
    public bool    RequireSma50AboveSma150    { get; set; } = true;
    /// <summary>Require price > SMA50.</summary>
    public bool    RequirePriceAboveSma50     { get; set; } = true;

    // ── Price Structure ────────────────────────────────────────────────────────
    /// <summary>Price must be within MaxPctFrom52WeekHigh of its 52-week high.</summary>
    public bool    FiftyTwoWeekHighFilterEnabled { get; set; } = true;
    /// <summary>Max % below 52-week high. Minervini uses 25%.</summary>
    public decimal MaxPctFrom52WeekHigh          { get; set; } = 25m;
    /// <summary>Price must be at least MinPctAbove52WeekLow above its 52-week low.</summary>
    public bool    FiftyTwoWeekLowFilterEnabled  { get; set; } = true;
    /// <summary>Min % above 52-week low. Minervini uses 30%.</summary>
    public decimal MinPctAbove52WeekLow          { get; set; } = 30m;

    // ── Relative Strength ─────────────────────────────────────────────────────
    /// <summary>Filter on 12-month price ROC as a RS proxy.</summary>
    public bool    RsFilterEnabled   { get; set; } = false;
    /// <summary>Minimum 12-month price rate-of-change % (RS proxy — not broker RS rating).</summary>
    public decimal MinRsRatingPct    { get; set; } = 20m;

    // ── Volume Dry-Up ─────────────────────────────────────────────────────────
    /// <summary>Require volume to contract inside the base before entry.</summary>
    public bool    VolumeDryUpEnabled          { get; set; } = false;
    /// <summary>Number of recent bars to average for the dry-up check.</summary>
    public int     VolumeDryUpBars             { get; set; } = 5;
    /// <summary>Recent avg volume must be below this % of the lookback average.</summary>
    public decimal VolumeDryUpThresholdPct     { get; set; } = 60m;

    public static VcpSwingConfig FromJson(string json)
        => JsonSerializer.Deserialize<VcpSwingConfig>(json) ?? new();

    public static IReadOnlyList<StrategyParamDef> GetSchema() =>
    [
        // ── VCP Setup ────────────────────────────────────────────────────────
        new("Sma200Period",           "SMA 200 Period",             "int",     200,   Min: 100,  Max: 300,                       Section: "VCP Setup"),
        new("MinSma200SlopePct",      "Min SMA200 Slope %",         "decimal", 0.0m,  Min: -1m,  Max: 2m,   Step: 0.05m,
            Hint: "SMA200 slope over 5 bars (%)",                                                                                Section: "VCP Setup"),
        new("LookbackBars",           "Lookback Bars",              "int",     60,    Min: 20,   Max: 200,
            Hint: "Bars to search for VCP structure",                                                                            Section: "VCP Setup"),
        new("PivotBars",              "Pivot Bars Each Side",       "int",     3,     Min: 1,    Max: 10,                        Section: "VCP Setup"),
        new("MinContractions",        "Min Contractions",           "int",     2,     Min: 1,    Max: 5,
            Hint: "Minimum VCP tightening cycles",                                                                               Section: "VCP Setup"),
        new("AtrPeriod",              "ATR Period",                 "int",     14,    Min: 5,    Max: 50,                        Section: "VCP Setup"),
        new("EntryBufferPct",         "Entry Buffer %",             "decimal", 0.5m,  Min: 0.1m, Max: 3.0m, Step: 0.1m,         Section: "VCP Setup"),
        new("BreakoutVolumeMultiple", "Breakout Volume Multiple",   "decimal", 1.5m,  Min: 1.0m, Max: 5.0m, Step: 0.5m,         Section: "VCP Setup"),
        new("RiskRewardRatio",        "Risk:Reward Ratio",          "decimal", 2.0m,  Min: 1.0m, Max: 10.0m, Step: 0.5m,        Section: "VCP Setup"),

        // ── Market Breadth ───────────────────────────────────────────────────
        new("BreadthFilterEnabled",   "Market Breadth Filter",      "bool",    true,
            Hint: "Skip entries when fewer than MinBreadthPct200Sma% of NSE stocks are above SMA200",                            Section: "Market Breadth"),
        new("MinBreadthPct200Sma",    "Min Breadth % (above SMA200)","decimal",40m,   Min: 20m,  Max: 80m,  Step: 5m,
            Hint: "% of NSE stocks above 200-day SMA required before entries are allowed",                                      Section: "Market Breadth", EnabledBy: "BreadthFilterEnabled"),

        // ── Trend Template (Minervini) ───────────────────────────────────────
        new("TrendTemplateEnabled",       "Trend Template (Minervini)", "bool",  true,
            Hint: "Enforce MA alignment: price>SMA50>SMA150>SMA200",                                                             Section: "Trend Template"),
        new("Sma150Period",               "SMA 150 Period",          "int",     150,   Min: 100, Max: 200,                       Section: "Trend Template", EnabledBy: "TrendTemplateEnabled"),
        new("Sma50Period",                "SMA 50 Period",           "int",     50,    Min: 20,  Max: 100,                       Section: "Trend Template", EnabledBy: "TrendTemplateEnabled"),
        new("RequireSma150AboveSma200",   "SMA150 > SMA200",         "bool",    true,
            Hint: "SMA150 must be above SMA200 (long-term uptrend)",                                                             Section: "Trend Template", EnabledBy: "TrendTemplateEnabled"),
        new("RequireSma50AboveSma150",    "SMA50 > SMA150",          "bool",    true,
            Hint: "SMA50 must be above SMA150 (intermediate trend leading)",                                                     Section: "Trend Template", EnabledBy: "TrendTemplateEnabled"),
        new("RequirePriceAboveSma50",     "Price > SMA50",           "bool",    true,
            Hint: "Current price must be above 50-day SMA",                                                                     Section: "Trend Template", EnabledBy: "TrendTemplateEnabled"),

        // ── Price Structure ──────────────────────────────────────────────────
        new("FiftyTwoWeekHighFilterEnabled", "Within X% of 52-Week High", "bool", true,
            Hint: "Stock must be near its 52-week high (leader behaviour). Minervini uses 25%.",                                 Section: "Price Structure"),
        new("MaxPctFrom52WeekHigh",          "Max % Below 52-Week High", "decimal", 25m, Min: 5m, Max: 50m, Step: 5m,
            Hint: "Maximum % the price can be below its 52-week high",                                                          Section: "Price Structure", EnabledBy: "FiftyTwoWeekHighFilterEnabled"),
        new("FiftyTwoWeekLowFilterEnabled",  "Above 52-Week Low by X%",  "bool",    true,
            Hint: "Price must have recovered significantly from its 52-week low. Minervini uses 30%.",                           Section: "Price Structure"),
        new("MinPctAbove52WeekLow",          "Min % Above 52-Week Low",  "decimal", 30m, Min: 10m, Max: 100m, Step: 5m,
            Hint: "Minimum % the price must be above its 52-week low",                                                          Section: "Price Structure", EnabledBy: "FiftyTwoWeekLowFilterEnabled"),

        // ── Relative Strength ────────────────────────────────────────────────
        new("RsFilterEnabled",   "Relative Strength Filter", "bool",    false,
            Hint: "Require minimum 12-month price ROC (proxy for RS rating). Needs 252 bars of history.",                        Section: "Relative Strength"),
        new("MinRsRatingPct",    "Min 12M Price ROC %",      "decimal", 20m,   Min: 0m,  Max: 200m, Step: 5m,
            Hint: "12-month price rate-of-change must be ≥ this value (strong stocks outperform)",                              Section: "Relative Strength", EnabledBy: "RsFilterEnabled"),

        // ── Volume Dry-Up ────────────────────────────────────────────────────
        new("VolumeDryUpEnabled",       "Volume Dry-Up Required", "bool",    false,
            Hint: "Volume inside the base must contract before entry is allowed",                                                Section: "Volume Dry-Up"),
        new("VolumeDryUpBars",          "Dry-Up Lookback Bars",   "int",     5,     Min: 2, Max: 20,
            Hint: "Recent bars to average for volume dry-up check",                                                             Section: "Volume Dry-Up", EnabledBy: "VolumeDryUpEnabled"),
        new("VolumeDryUpThresholdPct",  "Dry-Up Threshold %",     "decimal", 60m,   Min: 20m, Max: 90m, Step: 5m,
            Hint: "Recent avg volume must be below this % of the lookback average volume",                                      Section: "Volume Dry-Up", EnabledBy: "VolumeDryUpEnabled"),
    ];
}
