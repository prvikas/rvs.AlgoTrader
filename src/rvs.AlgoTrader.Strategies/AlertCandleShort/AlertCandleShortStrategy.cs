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

    public Task<SignalResult> EvaluateAsync(StrategyContext context, CancellationToken ct)
    {
        var candles = context.Candles;

        // Minimum data: EMA warm-up requires EmaPeriod candles + at least 2 today for pattern
        if (candles.Count < config.EmaPeriod + 1)
            return Task.FromResult(SignalResult.Skip(SkippedReason.InsufficientData,
                $"Need at least {config.EmaPeriod + 1} candles for EMA warm-up; have {candles.Count}"));

        // ── Step 1: Compute 5-EMA over the full candle array (aligned by index) ──

        var closes = candles.Select(c => c.Close).ToArray();
        var ema    = ComputeEmaFull(closes, config.EmaPeriod);

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
                "Waiting — need at least 2 closed bars today to check Alert Candle pattern"));

        // ── Step 3: Scan today's candles for the FIRST Alert Candle + breakout ──

        // Scan from the start of today up to (but not including) the last bar,
        // because the breakout bar is always the candle AFTER the alert candle.
        int? alertCandleIdx  = null;
        int? breakoutCandleIdx = null;

        for (int i = todayStart; i < candles.Count - 1; i++)
        {
            // EMA must be warmed up at this index
            if (ema[i] == 0m) continue;

            var candidate = candles[i];

            // ALERT CANDLE RULE: low must NOT touch the EMA — strict ">" required.
            // "Does not touch the 5-EMA at all" means the entire candle floats above.
            if (candidate.Low <= ema[i]) continue; // low at or below EMA → not an Alert Candle

            // The next candle must break below the Alert Candle's low to trigger entry
            int nextI = i + 1;
            if (candles[nextI].Low < candidate.Low)
            {
                alertCandleIdx   = i;
                breakoutCandleIdx = nextI;
                break; // Only care about the FIRST occurrence
            }
        }

        // ── Step 4: No pattern found today → wait ─────────────────────────────

        if (alertCandleIdx == null)
            return Task.FromResult(SignalResult.Hold(
                "No Alert Candle + breakout pattern found in today's session yet"));

        // ── Step 5: Pattern found — was it already signalled? ─────────────────

        // If the breakout bar is NOT the most recently closed candle, it already happened.
        // The strategy emits SELL exactly once: on the bar that IS the breakout.
        // All subsequent bars see the breakout in historical data → HOLD (one trade per day).
        if (breakoutCandleIdx!.Value < candles.Count - 1)
            return Task.FromResult(SignalResult.Hold(
                "Alert Candle signal already triggered earlier today — one trade per day rule"));

        // ── Step 6: This bar IS the breakout → generate SELL signal ──────────

        var alertCandle = candles[alertCandleIdx.Value];
        var emaAtAlert  = ema[alertCandleIdx.Value];

        // Entry: at Alert Candle Low (stop-sell trigger price)
        // In live trading, this is placed as a sell-stop order at alertCandle.Low.
        // For the strategy signal, entry = alertCandle.Low.
        var entry = alertCandle.Low;
        var sl    = alertCandle.High;
        var risk  = sl - entry;

        // Safety check: alert candle must have a valid body (high > low)
        if (risk <= 0)
            return Task.FromResult(SignalResult.Hold(
                $"Invalid alert candle: high ({alertCandle.High}) ≤ low ({alertCandle.Low})"));

        // ATR filter (optional): skip signals in extremely thin markets
        if (config.MinRiskPoints > 0 && risk < config.MinRiskPoints)
            return Task.FromResult(SignalResult.Skip(SkippedReason.FilterFailed,
                $"Risk of {risk:F2} pts below minimum {config.MinRiskPoints} pts — candle too small to trade"));

        var tp = entry - risk * config.RiskRewardRatio;

        var diagnostics = new
        {
            AlertCandleTime  = alertCandle.OpenTime.ToString(),
            AlertCandleHigh  = alertCandle.High,
            AlertCandleLow   = alertCandle.Low,
            AlertCandleClose = alertCandle.Close,
            EmaAtAlertBar    = Math.Round(emaAtAlert, 2),
            LowAboveEmaBy    = Math.Round(alertCandle.Low - emaAtAlert, 2),
            BreakoutBarLow   = candles[^1].Low,
            Risk             = Math.Round(risk, 2),
            RiskRewardRatio  = config.RiskRewardRatio,
            Remark           = "First trigger of the day — subsequent bars will HOLD"
        };

        return Task.FromResult(SignalResult.Sell(
            entryPrice: entry,
            stopLoss:   sl,
            takeProfit: tp,
            reason: $"Alert Candle Short: Low={entry} floated {alertCandle.Low - emaAtAlert:F2}pts above EMA({config.EmaPeriod})={emaAtAlert:F2}. " +
                    $"Breakout confirmed. SL={sl}, TP={tp:F2} (1:{config.RiskRewardRatio:F0} RRR). " +
                    $"EOD exit: 15:15 IST via schedule force-exit.",
            diagnosticsJson: diagnostics));
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
    /// <summary>EMA period. Rule specifies 5-period EMA. Do not change without backtesting.</summary>
    public int     EmaPeriod          { get; set; } = 5;

    /// <summary>
    /// Minimum Risk:Reward ratio for take profit.
    /// Rule specifies "minimum 1:3", so default is 3.0.
    /// TP = entry − risk × RiskRewardRatio.
    /// </summary>
    public decimal RiskRewardRatio    { get; set; } = 3.0m;

    /// <summary>
    /// Minimum risk in points to filter out tiny candles.
    /// Set to 0 to disable (take all valid alert candles regardless of size).
    /// Default 0 — the original rules don't specify a minimum candle size.
    /// Example: set to 20 for Nifty (20-point minimum risk = 20-point candle size).
    /// </summary>
    public decimal MinRiskPoints      { get; set; } = 0m;

    public static AlertCandleShortConfig FromJson(string json)
        => JsonSerializer.Deserialize<AlertCandleShortConfig>(json,
               new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
           ?? new AlertCandleShortConfig();
}
