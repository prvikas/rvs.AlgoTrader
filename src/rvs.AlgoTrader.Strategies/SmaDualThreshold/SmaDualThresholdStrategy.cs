using System.Text.Json;
using NodaTime;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.SmaDualThreshold;

/// <summary>
/// SMA Dual-Threshold Strategy (PineScript port: "20 SMA Strategy with Threshold — Nitin Joshi")
///
/// SIGNAL LOGIC:
///   LONG  ENTRY: close &gt; SMA(FastPeriod)*(1+thresh) AND close &gt; SMA(SlowPeriod)*(1+thresh)
///   LONG  EXIT:  close &lt; SMA(FastPeriod)*(1-thresh) OR  close &lt; SMA(SlowPeriod)*(1-thresh)
///   SHORT ENTRY: close &lt; SMA(FastPeriod)*(1-thresh) AND close &lt; SMA(SlowPeriod)*(1-thresh)
///   SHORT EXIT:  close &gt; SMA(FastPeriod)*(1+thresh) OR  close &gt; SMA(SlowPeriod)*(1+thresh)
///
/// EXECUTION MODEL:
///   • All entries and exits execute at the OPEN of the next candle.
///     Achieved by using SignalResult.ExitLong / ExitShort (strategy-driven exits)
///     and FillModel.NextBarOpen for entries — matching PineScript's default fill model.
///   • 15:15 IST candle is SKIPPED for both entry and exit (ignoreCandle flag).
///
/// PARAMETERS:
///   FastPeriod    — SMA fast period  (default 20)
///   SlowPeriod    — SMA slow period  (default 50)
///   ThresholdPct  — threshold as %   (default 0.1 → 0.1%)
///   AllowShort    — enable short trades (default true)
///   AtrPeriod     — ATR for SL/TP calculation (default 14)
///   AtrStopMult   — SL = entry ± ATR × this (default 3.0; wide to not override signal exits)
///   RiskRewardRatio — TP = entry ± risk × this (default 99; effectively no TP — exit by signal)
///
/// RISK NOTE:
///   This strategy exits exclusively on indicator conditions, not fixed SL/TP.
///   AtrStopMult is set to 3.0 by default to act as a disaster stop only.
///   RiskRewardRatio defaults to 99 (no meaningful TP) — let signal-based exit run.
/// </summary>
public class SmaDualThresholdStrategy(SmaDualThresholdConfig config) : IStrategy
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    public string Name => "SmaDualThreshold";

    // Warmup needs max(slow period, atr period) + buffer
    public int MinWarmupBars => Math.Max(config.SlowPeriod, config.AtrPeriod) + 5;

    public Task<SignalResult> EvaluateAsync(StrategyContext context, CancellationToken ct)
    {
        var candles = context.Candles;
        var minRequired = Math.Max(config.SlowPeriod, config.AtrPeriod) + 2;
        if (candles.Count < minRequired)
            return Task.FromResult(SignalResult.Skip(SkippedReason.InsufficientData,
                $"Need {minRequired} candles, have {candles.Count}"));

        var current = candles[^1];

        // ── 15:15 IST skip ───────────────────────────────────────────────────
        // The 15:15 candle is the last candle of the NSE session (partial / auction).
        // PineScript ignores this candle for both entry and exit.
        var barIst    = current.OpenTime.ToInstant().InZone(Ist).LocalDateTime;
        var barHour   = barIst.Hour;
        var barMinute = barIst.Minute;
        if (barHour == 15 && barMinute == 15)
            return Task.FromResult(SignalResult.Hold("15:15 IST candle skipped"));

        // ── Compute indicators ───────────────────────────────────────────────
        var closes = new decimal[candles.Count];
        for (int i = 0; i < candles.Count; i++) closes[i] = candles[i].Close;

        var smaFast = ComputeSma(closes, config.FastPeriod);
        var smaSlow = ComputeSma(closes, config.SlowPeriod);
        var atr     = ComputeAtr(candles, config.AtrPeriod);

        if (smaFast.Length == 0 || smaSlow.Length == 0 || atr.Length == 0)
            return Task.FromResult(SignalResult.Skip(SkippedReason.InsufficientData, "SMA warmup not complete"));

        var smaFastNow = smaFast[^1];
        var smaSlowNow = smaSlow[^1];
        var atrNow     = atr[^1];
        var close      = current.Close;

        // ── Threshold bands ──────────────────────────────────────────────────
        var threshMult  = config.ThresholdPct / 100m;
        var fastUpper   = smaFastNow * (1m + threshMult);  // fast SMA + threshold
        var fastLower   = smaFastNow * (1m - threshMult);  // fast SMA − threshold
        var slowUpper   = smaSlowNow * (1m + threshMult);  // slow SMA + threshold
        var slowLower   = smaSlowNow * (1m - threshMult);  // slow SMA − threshold

        var indicators = new Dictionary<string, decimal>
        {
            [$"sma{config.FastPeriod}"]       = smaFastNow,
            [$"sma{config.SlowPeriod}"]       = smaSlowNow,
            [$"sma{config.FastPeriod}Upper"]  = fastUpper,
            [$"sma{config.FastPeriod}Lower"]  = fastLower,
            [$"sma{config.SlowPeriod}Upper"]  = slowUpper,
            [$"sma{config.SlowPeriod}Lower"]  = slowLower,
            ["atr"] = atrNow,
        };

        // ── Entry / exit conditions ──────────────────────────────────────────
        bool longEntry  = close > fastUpper && close > slowUpper;
        bool longExit   = close < fastLower || close < slowLower;   // OR — either SMA breach exits
        bool shortEntry = close < fastLower && close < slowLower;
        bool shortExit  = close > fastUpper || close > slowUpper;   // OR — either SMA breach exits

        // ── Position-aware evaluation ────────────────────────────────────────
        // BacktestEngine passes CurrentPosition when re-evaluating for exit signals.
        // "LONG"  → check exit condition only; avoid flipping logic.
        // "SHORT" → check exit condition only.
        // null    → check entry conditions.
        var position = context.CurrentPosition;

        if (position == "LONG")
        {
            // We are long — check exit condition
            if (longExit)
                return Task.FromResult(SignalResult.ExitLong(
                    $"Long exit: close {close:F2} crossed below SMA{config.FastPeriod} lower {fastLower:F2} " +
                    $"or SMA{config.SlowPeriod} lower {slowLower:F2}",
                    indicators));

            return Task.FromResult(SignalResult.Hold(
                $"Long: holding — close {close:F2} still above SMA bands [F:{fastLower:F2} S:{slowLower:F2}]",
                indicatorValues: indicators));
        }

        if (position == "SHORT")
        {
            // We are short — check exit condition
            if (shortExit)
                return Task.FromResult(SignalResult.ExitShort(
                    $"Short exit: close {close:F2} crossed above SMA{config.FastPeriod} upper {fastUpper:F2} " +
                    $"or SMA{config.SlowPeriod} upper {slowUpper:F2}",
                    indicators));

            return Task.FromResult(SignalResult.Hold(
                $"Short: holding — close {close:F2} still below SMA bands [F:{fastUpper:F2} S:{slowUpper:F2}]",
                indicatorValues: indicators));
        }

        // ── No position: evaluate for entry ─────────────────────────────────

        if (longEntry)
        {
            // Set stop-loss below entry using ATR; TP set very high (exits driven by signal).
            // The "entry price" is the current close; actual fill is at next bar's open (FillModel).
            var sl = close - atrNow * config.AtrStopMult;
            var risk = close - sl;
            var tp   = close + risk * config.RiskRewardRatio;

            return Task.FromResult(SignalResult.Buy(
                entryPrice: close,
                stopLoss:   sl,
                takeProfit: tp,
                reason: $"Long: close {close:F2} above SMA{config.FastPeriod}+{config.ThresholdPct}% " +
                        $"({fastUpper:F2}) AND SMA{config.SlowPeriod}+{config.ThresholdPct}% ({slowUpper:F2})",
                indicatorValues: indicators));
        }

        if (config.AllowShort && shortEntry)
        {
            var sl = close + atrNow * config.AtrStopMult;
            var risk = sl - close;
            var tp   = close - risk * config.RiskRewardRatio;

            return Task.FromResult(SignalResult.Sell(
                entryPrice: close,
                stopLoss:   sl,
                takeProfit: tp,
                reason: $"Short: close {close:F2} below SMA{config.FastPeriod}-{config.ThresholdPct}% " +
                        $"({fastLower:F2}) AND SMA{config.SlowPeriod}-{config.ThresholdPct}% ({slowLower:F2})",
                indicatorValues: indicators));
        }

        // Neutral zone — close is between the fast and slow band boundaries
        return Task.FromResult(SignalResult.Hold(
            $"Neutral: close {close:F2} in SMA zone [S-lower:{slowLower:F2} F-upper:{fastUpper:F2}]",
            indicatorValues: indicators));
    }

    // ── Indicator calculations ─────────────────────────────────────────────

    /// <summary>Simple moving average (arithmetic mean over period).</summary>
    private static decimal[] ComputeSma(decimal[] closes, int period)
    {
        if (closes.Length < period) return [];
        var count  = closes.Length - period + 1;
        var result = new decimal[count];

        // Seed the first window
        decimal windowSum = 0;
        for (int i = 0; i < period; i++) windowSum += closes[i];
        result[0] = windowSum / period;

        // Slide the window: add new, remove old
        for (int i = 1; i < count; i++)
        {
            windowSum += closes[i + period - 1] - closes[i - 1];
            result[i]  = windowSum / period;
        }
        return result;
    }

    /// <summary>ATR using Wilder's smoothing (same implementation as EmaVwapMomentum).</summary>
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
}

/// <summary>
/// Configuration for SmaDualThresholdStrategy.
/// Stored in strategy_instances.parameters_json — editable via the UI without restart.
/// </summary>
public class SmaDualThresholdConfig
{
    /// <summary>Fast SMA period (default 20, i.e. SMA_20).</summary>
    public int     FastPeriod      { get; set; } = 20;

    /// <summary>Slow SMA period (default 50, i.e. SMA_50).</summary>
    public int     SlowPeriod      { get; set; } = 50;

    /// <summary>
    /// Threshold applied symmetrically to both SMAs as a percentage of the SMA value.
    /// Entry: close must be ABOVE sma × (1 + thresh) for both SMAs.
    /// Exit:  close must be BELOW sma × (1 − thresh) for either SMA.
    /// Default 0.1 = 0.1%.
    /// </summary>
    public decimal ThresholdPct    { get; set; } = 0.1m;

    /// <summary>Allow short trades (default true — strategy is long/short symmetric).</summary>
    public bool    AllowShort      { get; set; } = true;

    /// <summary>ATR period for stop-loss calculation (disaster stop only).</summary>
    public int     AtrPeriod       { get; set; } = 14;

    /// <summary>
    /// ATR stop multiplier. Default 3.0 — wide stop intended as a disaster stop only.
    /// The primary exit mechanism is the SMA-crossover signal, not the stop-loss.
    /// </summary>
    public decimal AtrStopMult     { get; set; } = 3.0m;

    /// <summary>
    /// Risk:Reward ratio for take-profit calculation. Default 99 (effectively no TP).
    /// Exits are driven by the SMA signal reversal, not a fixed TP level.
    /// Reduce to e.g. 2.5 if you want a fixed TP as well.
    /// </summary>
    public decimal RiskRewardRatio { get; set; } = 99m;

    public static SmaDualThresholdConfig FromJson(string json)
        => JsonSerializer.Deserialize<SmaDualThresholdConfig>(json,
               new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
           ?? new SmaDualThresholdConfig();

    public static IReadOnlyList<StrategyParamDef> GetSchema() =>
    [
        new("FastPeriod",      "Fast SMA Period",        "int",     20,    Min: 5,    Max: 200,              Hint: "Primary trend SMA (e.g. 20)"),
        new("SlowPeriod",      "Slow SMA Period",         "int",     50,    Min: 10,   Max: 500,              Hint: "Confirmation SMA (e.g. 50). Must be > FastPeriod"),
        new("ThresholdPct",    "Threshold %",             "decimal", 0.1m,  Min: 0.0m, Max: 5.0m,  Step: 0.05m, Hint: "% buffer around SMA for entry/exit (0.1 = 0.1%). Avoids false signals in choppy markets"),
        new("AllowShort",      "Allow Short Trades",      "bool",    true,                                    Hint: "Mirror the long signal on the short side"),
        new("AtrPeriod",       "ATR Period",              "int",     14,    Min: 5,    Max: 50,               Hint: "ATR lookback for disaster stop calculation"),
        new("AtrStopMult",     "ATR Stop Multiple",       "decimal", 3.0m,  Min: 1.0m, Max: 10.0m, Step: 0.5m, Hint: "Disaster stop only — primary exit is SMA signal reversal. Set wide (3–5×) to avoid premature stops"),
        new("RiskRewardRatio", "Risk:Reward Ratio",       "decimal", 99m,   Min: 1.0m, Max: 99m,   Step: 0.5m, Hint: "99 = no fixed TP (exits driven by signal). Set to 2.5 to add a fixed profit target"),
    ];
}
