using System.Text.Json;
using NodaTime;
using NodaTime.TimeZones;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.IntradayPcrOptions;

/// <summary>
/// STRAT-003: Intraday PCR/OI/VWAP Options Strategy.
/// Spec: docs/STRATEGY_SPECS.md § STRAT-003
///
/// Session:  Observe 09:15–11:00 IST; no trade during observation window.
///           Large gap (&gt;GapThresholdPts) → defer to 13:00 IST.
/// Bias:     PCR(OI) &gt; PcrUpperThreshold → bullish → buy calls.
///           PCR(OI) &lt; PcrLowerThreshold → bearish → buy puts.
/// Strike:   Delta 0.30–0.35; prefer near-expiry weekly.
/// Entry:    Option price within VWAP tolerance.
/// Stop:     Below/above the session option low/high.
/// Expiry:   Expiry-day contract → roll to next expiry.
/// </summary>
public class IntradayPcrOptionsStrategy(IntradayPcrOptionsConfig config) : IStrategy
{
    public string Name => "IntradayPcrOptions";

    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    public Task<SignalResult> EvaluateAsync(StrategyContext context, CancellationToken ct)
    {
        var candles = context.Candles;
        if (candles.Count < 5)
            return Task.FromResult(SignalResult.Skip(SkippedReason.InsufficientData, "Need at least 5 intraday candles"));

        var current  = candles[^1];
        var localNow = current.CloseTime.ToInstant().InZone(Ist).LocalDateTime;
        var istTime  = localNow.TimeOfDay;

        // ── Session window check ───────────────────────────────────────────
        var observeStart  = new LocalTime(config.ObserveStartHour,  config.ObserveStartMinute);
        var observeEnd    = new LocalTime(config.ObserveEndHour,    config.ObserveEndMinute);
        var deferredStart = new LocalTime(config.DeferredStartHour, config.DeferredStartMinute);

        // No trade during observation window
        if (istTime >= observeStart && istTime < observeEnd)
            return Task.FromResult(SignalResult.Hold(
                $"Observation window {observeStart}–{observeEnd} IST — no trades until {observeEnd}"));

        // ── Gap detection ──────────────────────────────────────────────────
        if (candles.Count >= 2)
        {
            decimal prevClose = candles[^2].Close;
            decimal gapPts    = Math.Abs(current.Open - prevClose);
            if (gapPts > config.GapThresholdPts && istTime < deferredStart)
                return Task.FromResult(SignalResult.Hold(
                    $"Large gap {gapPts:F0}pts > {config.GapThresholdPts}pts — deferred to 13:00 IST"));
        }

        // ── OptionChain required ───────────────────────────────────────────
        if (context.OptionChain is null)
            return Task.FromResult(SignalResult.Skip(SkippedReason.FilterFailed,
                "IntradayPcrOptions requires OptionChain snapshot — not available"));

        var chain = context.OptionChain;

        // ── PCR by change in OI (spec: PCR(change in OI)) ─────────────────
        // Use OI change ratio when CE side has positive build; fall back to static OI ratio
        // when change OI is unavailable (0) to avoid losing a valid signal.
        decimal pcrChange = chain.PutCallRatioChangeOI;
        decimal pcr = pcrChange > 0 ? pcrChange : chain.PutCallRatioOI;

        if (pcr >= config.PcrLowerThreshold && pcr <= config.PcrUpperThreshold)
            return Task.FromResult(SignalResult.Hold(
                $"PCR {pcr:F2} in neutral zone [{config.PcrLowerThreshold},{config.PcrUpperThreshold}] — no bias"));

        bool bullish = pcr < config.PcrLowerThreshold;

        // ── VWAP filter ────────────────────────────────────────────────────
        decimal vwap = ComputeVwap(candles);
        decimal vwapTol = vwap * config.VwapTolerancePct / 100m;
        bool inVwapZone = Math.Abs(current.Close - vwap) <= vwapTol;

        if (!inVwapZone)
            return Task.FromResult(SignalResult.Hold(
                $"Price {current.Close:F2} outside VWAP {vwap:F2} ±{vwapTol:F2} tolerance"));

        // ── Build single-leg signal ────────────────────────────────────────
        var legSpec = new OptionsLegSpec(
            OptionType:           bullish ? OptionType.Call : OptionType.Put,
            SelectionMode:        StrikeSelectionMode.ByDelta,
            TargetDelta:          config.TargetDelta,
            NearestWeeklyExpiry:  true);

        string biasReason = bullish
            ? $"PCR {pcr:F2} < {config.PcrLowerThreshold} (bullish) — buy call"
            : $"PCR {pcr:F2} > {config.PcrUpperThreshold} (bearish) — buy put";

        // ── Stop: near option/underlying session day low/high (spec: "stop: near option day low/high") ──
        // Option candles are not available in context; use session range of the underlying as proxy.
        // For a call buy: stop below session low (underlying falls → call loses delta value).
        // For a put buy:  stop above session high (underlying rises → put loses delta value).
        // StopBufferPct adds a small buffer so a brief wick doesn't trigger the stop.
        decimal sessionLow   = candles.Min(c => c.Low);
        decimal sessionHigh  = candles.Max(c => c.High);
        decimal stopLoss     = bullish
            ? sessionLow  * (1m - config.StopBufferPct / 100m)   // buffer below session low
            : sessionHigh * (1m + config.StopBufferPct / 100m);  // buffer above session high
        decimal stopDistance = Math.Abs(current.Close - stopLoss);
        decimal takeProfit   = bullish
            ? current.Close + stopDistance * config.RiskRewardRatio
            : current.Close - stopDistance * config.RiskRewardRatio;

        var indicators = new Dictionary<string, decimal>
        {
            ["vwap"]        = vwap,
            ["pcr"]         = pcr,
            ["pcrChangeOI"] = pcrChange,
            ["sessionLow"]  = sessionLow,
            ["sessionHigh"] = sessionHigh,
        };

        return Task.FromResult(new SignalResult(
            Signal:          bullish ? SignalType.Buy : SignalType.Sell,
            EntryPrice:      current.Close,
            StopLoss:        stopLoss,
            TakeProfit:      takeProfit,
            Reason:          $"{biasReason}, VWAP={vwap:F2}, stop@{stopLoss:F2} (session), time={istTime}",
            DiagnosticsJson: null,
            SkippedReason:   null,
            IndicatorValues: indicators,
            OptionsLeg:      legSpec));
    }

    private static decimal ComputeVwap(IReadOnlyList<ClosedCandle> candles)
    {
        decimal tvSum = 0, vSum = 0;
        foreach (var c in candles)
        {
            var typ = (c.High + c.Low + c.Close) / 3m;
            tvSum += typ * c.Volume;
            vSum  += c.Volume;
        }
        return vSum == 0 ? 0 : tvSum / vSum;
    }
}

public class IntradayPcrOptionsConfig
{
    public decimal PcrUpperThreshold    { get; set; } = 1.2m;   // PCR(changeOI) above = bullish for calls
    public decimal PcrLowerThreshold    { get; set; } = 0.8m;   // PCR(changeOI) below = bearish for puts
    public decimal GapThresholdPts      { get; set; } = 100m;   // Nifty points for gap detection
    public decimal TargetDelta          { get; set; } = 0.32m;
    public decimal VwapTolerancePct     { get; set; } = 0.5m;   // price within 0.5% of VWAP
    /// <summary>% buffer below/above session low/high for the stop trigger (spec: "near option day low/high").</summary>
    public decimal StopBufferPct        { get; set; } = 0.3m;   // 0.3% buffer beyond session extreme
    public decimal RiskRewardRatio      { get; set; } = 1.5m;
    // Configurable session window (IST), default: observe 09:15–11:00; defer on gap to 13:00
    public int ObserveStartHour         { get; set; } = 9;
    public int ObserveStartMinute       { get; set; } = 15;
    public int ObserveEndHour           { get; set; } = 11;
    public int ObserveEndMinute         { get; set; } = 0;
    public int DeferredStartHour        { get; set; } = 13;
    public int DeferredStartMinute      { get; set; } = 0;

    public static IntradayPcrOptionsConfig FromJson(string json)
        => JsonSerializer.Deserialize<IntradayPcrOptionsConfig>(json) ?? new();

    public static IReadOnlyList<StrategyParamDef> GetSchema() =>
    [
        new("PcrUpperThreshold", "PCR Upper Threshold", "decimal", 1.2m,  Min: 0.8m,  Max: 2.5m, Step: 0.1m, Hint: "PCR(change OI) above this = bullish, buy calls"),
        new("PcrLowerThreshold", "PCR Lower Threshold", "decimal", 0.8m,  Min: 0.3m,  Max: 1.2m, Step: 0.1m, Hint: "PCR(change OI) below this = bearish, buy puts"),
        new("GapThresholdPts",   "Gap Threshold (pts)", "decimal", 100m,  Min: 20m,   Max: 500m, Step: 10m,  Hint: "Large gap → defer to DeferredStartHour:Minute IST"),
        new("TargetDelta",       "Target Delta",        "decimal", 0.32m, Min: 0.15m, Max: 0.5m, Step: 0.05m),
        new("VwapTolerancePct",  "VWAP Tolerance %",   "decimal", 0.5m,  Min: 0.1m,  Max: 2.0m, Step: 0.1m),
        new("StopBufferPct",     "Stop Buffer %",       "decimal", 0.3m,  Min: 0.1m,  Max: 2.0m, Step: 0.1m, Hint: "% buffer beyond session low/high for the stop trigger"),
        new("RiskRewardRatio",   "Risk:Reward Ratio",   "decimal", 1.5m,  Min: 1.0m,  Max: 5.0m, Step: 0.5m),
        new("ObserveStartHour",  "Observe Start Hour",  "int",     9,     Min: 9,     Max: 10,   Hint: "IST hour for session observation start"),
        new("ObserveStartMinute","Observe Start Min",   "int",     15,    Min: 0,     Max: 59),
        new("ObserveEndHour",    "Observe End Hour",    "int",     11,    Min: 10,    Max: 12,   Hint: "IST hour for observation end / earliest entry time"),
        new("ObserveEndMinute",  "Observe End Min",     "int",     0,     Min: 0,     Max: 59),
        new("DeferredStartHour", "Deferred Start Hour", "int",     13,    Min: 12,    Max: 14,   Hint: "IST hour for entry after large gap (default 13:00)"),
    ];
}
