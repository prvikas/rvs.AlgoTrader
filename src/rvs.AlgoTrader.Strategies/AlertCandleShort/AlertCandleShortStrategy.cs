using System.Text.Json;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.AlertCandleShort;

/// <summary>
/// Alert Candle Short Strategy — BankNifty / Nifty 50 (5-minute timeframe).
///
/// ┌──────────────────────────────────────────────────────────────────────────┐
/// │  RULES (exact as specified)                                              │
/// │                                                                          │
/// │  Instrument: BankNifty or Nifty 50 — 5-minute bars                     │
/// │  Indicator:  5-period EMA                                                │
/// │                                                                          │
/// │  ALERT CANDLE: A closed 5-min candle whose LOW does NOT touch the       │
/// │  5-EMA at all — i.e., candle.Low > EMA_at_that_bar (strictly above).   │
/// │  The entire candle body is floating above the EMA.                      │
/// │                                                                          │
/// │  ENTRY (Short only):                                                     │
/// │    When the candle immediately following the Alert Candle breaks          │
/// │    below the Alert Candle's low → SHORT at Alert Candle Low.            │
/// │    Order type: stop-sell at AlertCandle.Low.                            │
/// │                                                                          │
/// │  STOP LOSS:  Alert Candle High (fixed, not trailed)                     │
/// │  TAKE PROFIT: entry − risk × 3.0   (1:3 Risk-to-Reward minimum)        │
/// │  EOD EXIT:   15:15 IST — handled by force_exit_on_session_end=true      │
/// │              in schedule_json (not this strategy's responsibility)       │
/// │                                                                          │
/// │  ONE TRADE PER DAY RULE:                                                 │
/// │    Only the first Alert Candle + breakout trigger of the day is taken.  │
/// │    After that, all signals for the day → HOLD.                          │
/// │    If SL is hit, the position is closed externally by LiveExecutionEngine│
/// │    and this strategy naturally returns HOLD for all subsequent bars      │
/// │    (the breakout already exists in candle history → "already triggered"). │
/// └──────────────────────────────────────────────────────────────────────────┘
///
/// STATELESS DESIGN (Rule #18 compliant):
///   "One trade per day" is enforced purely from candle history — no state fields,
///   no Redis, no DB. The algorithm scans today's candles for the FIRST valid
///   Alert Candle + breakout pair. If that breakout bar is in the past (not the
///   most recent closed bar), the signal has already been emitted → HOLD.
///
/// RECOMMENDED schedule_json:
///   {
///     "days":                    ["MON","TUE","WED","THU","FRI"],
///     "session_start":           "09:20",
///     "session_stop":            "15:15",
///     "timezone":                "Asia/Kolkata",
///     "auto_resume_on_restart":  true,
///     "missed_session_behavior": "SKIP",
///     "force_exit_on_session_end": true       ← 15:15 EOD exit
///   }
/// </summary>
public class AlertCandleShortStrategy(AlertCandleShortConfig config) : IStrategy
{
    public string Name => "AlertCandleShort";

    // Warmup: the slower of the fast EMA or trend filter EMA must be seeded + buffer.
    // SessionStartBufferBars is an intraday skip (not a warmup requirement), so excluded here.
    public int MinWarmupBars =>
        Math.Max(config.EmaPeriod, config.TrendFilterPeriod > 0 ? config.TrendFilterPeriod : config.EmaPeriod) + 5;

    public Task<SignalResult> EvaluateAsync(StrategyContext context, CancellationToken ct)
    {
        var candles = context.Candles;

        // Minimum data: both the fast EMA and the trend EMA need enough bars to warm up.
        // TrendFilterPeriod=20 requires 20 candles before emaTrend[i] is non-zero.
        // Using only EmaPeriod+1 silently bypasses the trend filter for the first 20 bars.
        var minRequired = Math.Max(config.EmaPeriod, config.TrendFilterPeriod > 0 ? config.TrendFilterPeriod : 0) + 1;
        if (candles.Count < minRequired)
            return Task.FromResult(SignalResult.Skip(SkippedReason.InsufficientData,
                $"Need {minRequired} candles for EMA warm-up; have {candles.Count}"));

        // ── Step 1: Compute EMAs over the full candle array (aligned by index) ──

        var closes = new decimal[candles.Count];
        for (int ci = 0; ci < candles.Count; ci++) closes[ci] = candles[ci].Close;
        var ema      = ComputeEmaFull(closes, config.EmaPeriod);
        var emaTrend = config.TrendFilterPeriod > 0
            ? ComputeEmaFull(closes, config.TrendFilterPeriod)
            : null;

        // Indicator snapshot for chart overlay — computed once, attached to every return path
        var emaAtCurrentBar = ema[candles.Count - 1];
        var indicators = new Dictionary<string, decimal> { [$"ema{config.EmaPeriod}"] = emaAtCurrentBar };

        // ── Step 2: Identify today's candles (IST date of most recent bar) ───────

        // OpenTime is a ZonedDateTime already in IST (CLAUDE.md Rule #15 — IClock contracts)
        var today = candles[^1].OpenTime.Date; // NodaTime LocalDate in IST

        // Walk backwards to find where today started in the candle array
        int todayStart = candles.Count - 1;
        while (todayStart > 0 && candles[todayStart - 1].OpenTime.Date == today)
            todayStart--;

        int todayBarCount = candles.Count - todayStart;

        // Need at least 2 today's bars: the Alert Candle + the breakout bar
        if (todayBarCount < 2)
            return Task.FromResult(SignalResult.Hold(
                "Waiting — need at least 2 closed bars today to check Alert Candle pattern",
                indicatorValues: indicators));

        // ── Step 3: Session avg volume (for breakout confirmation filter) ──────

        double sessionVolSum = 0;
        int    sessionVolCount = 0;
        // Use bars from today up to (not including) the last bar
        for (int vi = todayStart; vi < candles.Count - 1; vi++)
        {
            sessionVolSum += (double)candles[vi].Volume;
            sessionVolCount++;
        }
        var sessionAvgVol = sessionVolCount > 0 ? sessionVolSum / sessionVolCount : 0d;

        // ── Step 4: Scan today's candles for the FIRST Alert Candle + breakout ──
        //
        // SHORT setup: entire candle floats ABOVE the fast EMA (low > EMA).
        //   Breakout bar: breaks below the alert candle's Low → SELL at alertCandle.Low.
        //   Stop: alertCandle.High | TP: entry − risk × RRR
        //
        // LONG setup (when AllowLong=true): entire candle floats BELOW the fast EMA (high < EMA).
        //   Breakout bar: breaks above the alert candle's High → BUY at alertCandle.High.
        //   Stop: alertCandle.Low | TP: entry + risk × RRR
        //
        // One-trade-per-day: the earliest pattern that fires wins; once any breakout bar is
        // in history the strategy returns HOLD for the rest of the day.

        int? shortAlertIdx = null, shortBreakoutIdx = null;
        int? longAlertIdx  = null, longBreakoutIdx  = null;
        // Track whether any alert+breakout pair already fired today (for one-trade-per-day).
        bool anyPastShortBreakout = false;
        bool anyPastLongBreakout  = false;

        int scanStart = todayStart + config.SessionStartBufferBars;

        // Scan ALL of today's candles (not just the first match) so a later alert candle whose
        // breakout bar IS the most recent closed bar is never missed because an earlier non-live
        // setup caused a premature break.
        // Rule: only record a live breakout (nextI == candles.Count - 1).
        //       If a past breakout is found (nextI < candles.Count - 1), track it for
        //       one-trade-per-day but keep scanning for a live setup.
        for (int i = scanStart; i < candles.Count - 1; i++)
        {
            if (ema[i] == 0m) continue;
            var candidate = candles[i];
            int nextI     = i + 1;

            // ── SHORT alert candle: low floats ABOVE the EMA ─────────────
            if (candidate.Low > ema[i])
            {
                // Trend filter: only short below the trend EMA
                bool trendOkShort = emaTrend == null || emaTrend[i] == 0m || candidate.Close < emaTrend[i];
                if (trendOkShort && candles[nextI].Low < candidate.Low)
                {
                    if (nextI == candles.Count - 1)
                    {
                        // Live breakout on the current bar — emit signal (if not already one-trade'd)
                        shortAlertIdx    = i;
                        shortBreakoutIdx = nextI;
                    }
                    else
                    {
                        // Past breakout — one-trade-per-day has already been used for shorts
                        anyPastShortBreakout = true;
                    }
                }
            }

            // ── LONG alert candle: high floats BELOW the EMA ─────────────
            if (config.AllowLong && candidate.High < ema[i])
            {
                // Trend filter: only long above the trend EMA
                bool trendOkLong = emaTrend == null || emaTrend[i] == 0m || candidate.Close > emaTrend[i];
                if (trendOkLong && candles[nextI].High > candidate.High)
                {
                    if (nextI == candles.Count - 1)
                    {
                        longAlertIdx    = i;
                        longBreakoutIdx = nextI;
                    }
                    else
                    {
                        anyPastLongBreakout = true;
                    }
                }
            }
        }

        // ── Step 5: No pattern found today → wait ────────────────────────────

        if (shortAlertIdx == null && longAlertIdx == null && !anyPastShortBreakout && !anyPastLongBreakout)
            return Task.FromResult(SignalResult.Hold(
                "No Alert Candle + breakout pattern found in today's session yet",
                indicatorValues: indicators));

        // ── Step 6: One-trade-per-day — did a breakout already fire earlier today? ────

        // A past breakout means the trade was already triggered (or would have been); suppress today.
        if (anyPastShortBreakout || anyPastLongBreakout)
            return Task.FromResult(SignalResult.Hold(
                "Alert Candle signal already triggered earlier today — one trade per day rule",
                indicatorValues: indicators));

        // No live pattern found but past scan exhausted
        if (shortAlertIdx == null && longAlertIdx == null)
            return Task.FromResult(SignalResult.Hold(
                "No Alert Candle + breakout pattern found in today's session yet",
                indicatorValues: indicators));

        // ── Step 7: Breakout bar is the current bar → generate signal ─────────

        // Determine which pattern fires on this bar (both are already confirmed live)
        bool shortFires = shortBreakoutIdx.HasValue;
        bool longFires  = longBreakoutIdx.HasValue;

        // If both fire simultaneously (rare — very wide bar), prefer neither (ambiguous)
        if (shortFires && longFires)
            return Task.FromResult(SignalResult.Hold(
                "Both long and short patterns fired on same bar — ambiguous, no trade",
                indicatorValues: indicators));

        // ── Breakout volume filter ────────────────────────────────────────────
        if (config.BreakoutVolumeMultiple > 0m && sessionAvgVol > 0)
        {
            var breakoutVol = (double)candles[^1].Volume;
            if (breakoutVol < sessionAvgVol * (double)config.BreakoutVolumeMultiple)
                return Task.FromResult(SignalResult.Hold(
                    $"Breakout bar volume ({breakoutVol:F0}) < {config.BreakoutVolumeMultiple}× session avg ({sessionAvgVol:F0}) — low conviction",
                    indicatorValues: indicators));
        }

        // ── SHORT signal ──────────────────────────────────────────────────────
        if (shortFires)
        {
            var alertCandle = candles[shortAlertIdx!.Value];
            var emaAtAlert  = ema[shortAlertIdx.Value];
            var entry = alertCandle.Low;
            var sl    = alertCandle.High;
            var risk  = sl - entry;

            if (risk <= 0)
                return Task.FromResult(SignalResult.Hold(
                    $"Invalid short alert candle: high ({alertCandle.High}) ≤ low ({alertCandle.Low})",
                    indicatorValues: indicators));

            if (config.MinRiskPoints > 0 && risk < config.MinRiskPoints)
                return Task.FromResult(SignalResult.Skip(SkippedReason.FilterFailed,
                    $"Short risk {risk:F2}pts below minimum {config.MinRiskPoints}pts"));

            var tp = entry - risk * config.RiskRewardRatio;

            return Task.FromResult(SignalResult.Sell(
                entryPrice: entry,
                stopLoss:   sl,
                takeProfit: tp,
                reason: $"Alert Candle Short: Low={entry:F2} floated {alertCandle.Low - emaAtAlert:F2}pts above EMA({config.EmaPeriod})={emaAtAlert:F2}. " +
                        $"SL={sl:F2} TP={tp:F2} (1:{config.RiskRewardRatio:F0} RRR)",
                diagnosticsJson: new
                {
                    AlertCandleTime  = alertCandle.OpenTime.ToString(),
                    AlertCandleHigh  = alertCandle.High, AlertCandleLow = alertCandle.Low,
                    EmaAtAlertBar    = Math.Round(emaAtAlert, 2),
                    FloatAboveEma    = Math.Round(alertCandle.Low - emaAtAlert, 2),
                    BreakoutBarLow   = candles[^1].Low,
                    Risk             = Math.Round(risk, 2),
                    RiskRewardRatio  = config.RiskRewardRatio
                },
                indicatorValues: indicators));
        }

        // ── LONG signal ───────────────────────────────────────────────────────
        {
            var alertCandle = candles[longAlertIdx!.Value];
            var emaAtAlert  = ema[longAlertIdx.Value];
            var entry = alertCandle.High;
            var sl    = alertCandle.Low;
            var risk  = entry - sl;

            if (risk <= 0)
                return Task.FromResult(SignalResult.Hold(
                    $"Invalid long alert candle: high ({alertCandle.High}) ≤ low ({alertCandle.Low})",
                    indicatorValues: indicators));

            if (config.MinRiskPoints > 0 && risk < config.MinRiskPoints)
                return Task.FromResult(SignalResult.Skip(SkippedReason.FilterFailed,
                    $"Long risk {risk:F2}pts below minimum {config.MinRiskPoints}pts"));

            var tp = entry + risk * config.RiskRewardRatio;

            return Task.FromResult(SignalResult.Buy(
                entryPrice: entry,
                stopLoss:   sl,
                takeProfit: tp,
                reason: $"Alert Candle Long: High={entry:F2} floated {emaAtAlert - alertCandle.High:F2}pts below EMA({config.EmaPeriod})={emaAtAlert:F2}. " +
                        $"SL={sl:F2} TP={tp:F2} (1:{config.RiskRewardRatio:F0} RRR)",
                diagnosticsJson: new
                {
                    AlertCandleTime  = alertCandle.OpenTime.ToString(),
                    AlertCandleHigh  = alertCandle.High, AlertCandleLow = alertCandle.Low,
                    EmaAtAlertBar    = Math.Round(emaAtAlert, 2),
                    FloatBelowEma    = Math.Round(emaAtAlert - alertCandle.High, 2),
                    BreakoutBarHigh  = candles[^1].High,
                    Risk             = Math.Round(risk, 2),
                    RiskRewardRatio  = config.RiskRewardRatio
                },
                indicatorValues: indicators));
        }
    }

    // ── Private: EMA calculation ───────────────────────────────────────────

    /// <summary>
    /// Computes EMA over the full candle array, preserving index alignment.
    /// result[i] = EMA value at candle i.
    /// result[i] = 0 for i &lt; (period - 1) — not yet warmed up.
    /// This alignment means: ema[i] corresponds directly to candles[i].
    /// </summary>
    private static decimal[] ComputeEmaFull(decimal[] closes, int period)
    {
        var result = new decimal[closes.Length];
        if (closes.Length < period) return result;

        // Seed EMA = SMA of the first 'period' candles
        decimal seed = 0m;
        for (int i = 0; i < period; i++) seed += closes[i];
        result[period - 1] = seed / period;

        // Smoothing factor: k = 2 / (period + 1)
        var k = 2m / (period + 1);
        for (int i = period; i < closes.Length; i++)
            result[i] = closes[i] * k + result[i - 1] * (1 - k);

        return result;
    }
}

/// <summary>
/// Configuration for AlertCandleShortStrategy.
/// All parameters stored in strategy_instances.parameters_json (CLAUDE.md Rule #20).
///
/// Default values match the exact specification:
///   EMA period = 5, RRR = 3.0 (1:3), short only, 5-minute bars.
///
/// Recommended schedule_json for this strategy:
///   session_start: "09:20"   — wait for first few bars to warm up EMA
///   session_stop:  "15:15"   — EOD exit as per strategy rules
///   force_exit_on_session_end: true
/// </summary>
public class AlertCandleShortConfig
{
    /// <summary>Fast EMA period for the alert-candle pattern. Default 5 per strategy spec.</summary>
    public int     EmaPeriod              { get; set; } = 5;

    /// <summary>
    /// Minimum Risk:Reward ratio. Spec says "minimum 1:3" → default 3.0.
    /// TP = entry − risk × RiskRewardRatio.
    /// </summary>
    public decimal RiskRewardRatio        { get; set; } = 3.0m;

    /// <summary>
    /// Minimum risk in points. Filters tiny alert candles that produce noisy signals.
    /// Default 15 pts (suitable for BankNifty 5-min; set to 0 to disable).
    /// </summary>
    public decimal MinRiskPoints          { get; set; } = 15m;

    /// <summary>
    /// Trend filter EMA period. Alert candles are only valid when close &lt; EMA(this period).
    /// Prevents shorting into an uptrend — the primary cause of losses in this strategy.
    /// Default 20. Set to 0 to disable the trend filter.
    /// </summary>
    public int     TrendFilterPeriod      { get; set; } = 20;

    /// <summary>
    /// Skip this many bars at the start of each session before scanning for alert candles.
    /// Opening bars (first 15 min of a 5-min chart = 3 bars) are erratic and produce
    /// false setups. Default 3. Set to 0 to disable.
    /// </summary>
    public int     SessionStartBufferBars { get; set; } = 3;

    /// <summary>
    /// Volume confirmation on the breakout bar. The bar that breaks the alert candle
    /// boundary must have volume ≥ this multiple of the session average.
    /// Higher volume on the breakout = institutional participation = higher confidence.
    /// Default 1.5. Set to 0 to disable.
    /// </summary>
    public decimal BreakoutVolumeMultiple { get; set; } = 1.5m;

    /// <summary>
    /// Enable the long-side mirror of the Alert Candle setup.
    /// Long alert candle: entire candle (including high) floats BELOW the fast EMA.
    /// Entry: when the next bar breaks above the alert candle's High (stop-buy at alertHigh).
    /// Stop: alert candle Low. TP: entry + risk × RRR.
    /// Only takes long signals when price is above the trend EMA.
    /// Default false (short-only per original spec).
    /// </summary>
    public bool    AllowLong              { get; set; } = false;

    public static AlertCandleShortConfig FromJson(string json)
        => JsonSerializer.Deserialize<AlertCandleShortConfig>(json,
               new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
           ?? new AlertCandleShortConfig();

    public static IReadOnlyList<StrategyParamDef> GetSchema() =>
    [
        new("EmaPeriod",              "EMA Period",                "int",     5,    Min: 3,    Max: 20,  Hint: "The EMA the Alert Candle must float ABOVE (no wick touch)"),
        new("RiskRewardRatio",        "Min Risk:Reward",           "decimal", 3.0m, Min: 1.5m, Max: 10.0m, Step: 0.5m, Hint: "TP = entry − (SL − entry) × this. Spec minimum is 1:3"),
        new("MinRiskPoints",          "Min Risk (points)",         "decimal", 15m,  Min: 0m,   Max: 500m, Step: 5m,   Hint: "Skip if SL−Entry < this. Filters tiny candles. 0 = disabled"),
        new("TrendFilterPeriod",      "Trend Filter EMA Period",   "int",     20,   Min: 0,    Max: 100, Hint: "Only short when close < this EMA. Set to 0 to disable"),
        new("SessionStartBufferBars", "Session Start Buffer Bars", "int",     3,    Min: 0,    Max: 12,   Hint: "Skip first N bars of each session (opening noise)"),
        new("BreakoutVolumeMultiple", "Breakout Volume Multiple",  "decimal", 1.5m, Min: 0.0m, Max: 5.0m, Step: 0.5m, Hint: "Breakout bar must have volume ≥ this × session avg. 0 = disabled"),
        new("AllowLong",              "Allow Long Trades",         "bool",    false, Hint: "Mirror long setup: candle high floats below EMA → next bar breaks above → BUY"),
    ];
}
