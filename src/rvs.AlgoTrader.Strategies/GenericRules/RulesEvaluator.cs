using NodaTime;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.GenericRules;

// ─────────────────────────────────────────────────────────────────────────────
// RulesEvaluator — evaluates an EntryExitBlock (RuleGroup tree) against
// computed indicator values and the current candle.
//
// Operator support:
//   ">" | "<" | ">=" | "<=" | "==" | "!=" | "crossesAbove" | "crossesBelow"
//   "touchedAndBounced"  — price pulled back to indicator this bar or last bar,
//                          and the current close is back ABOVE it (long) or
//                          BELOW it (short). Eliminates late crossover entries.
//
// Operand kind support:
//   "indicator"     — computed indicator value (by id + optional field)
//   "value"         — static decimal
//   "window"        — aggregated over N bars (avg/max/min/highest/lowest)
//   "percentile"    — % rank of indicator over lookback bars
//   "slope"         — positive (1) if indicator rose over lookback bars, -1 if fell, 0 flat
//   "close"|"open"|"high"|"low"|"volume" — current bar OHLCV
//   "prevClose"|"prevHigh"|"prevLow"|"prevOpen" — previous bar OHLCV
//   "isBullishBar"  — 1 if current close > current open, else 0
//   "isBearishBar"  — 1 if current close < current open, else 0
//   "htfBias"       — 1 if higher-timeframe indicator is bullish (value > 0),
//                     useful for EMA/SuperTrend HTF filters without a separate
//                     HTF candle feed: reads the indicator tagged role="htfBias"
//   "sessionState"  — boolean session properties (treated as 1 or 0)
// ─────────────────────────────────────────────────────────────────────────────

public static class RulesEvaluator
{
    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates a RuleBlock against indicator values and returns true if the
    /// block fires (all groups pass, or any group passes, depending on groupOperator).
    /// Returns false if the block is disabled or has no groups.
    /// </summary>
    public static bool Evaluate(
        RuleBlock block,
        Dictionary<string, ComputedIndicator> indicators,
        IReadOnlyList<ClosedCandle> candles)
    {
        if (!block.Enabled || block.Groups.Length == 0) return false;

        var isOr = block.GroupOperator.Equals("OR", StringComparison.OrdinalIgnoreCase);
        foreach (var group in block.Groups)
        {
            var groupResult = EvaluateGroup(group, indicators, candles);
            if (isOr && groupResult) return true;
            if (!isOr && !groupResult) return false;
        }
        // AND: all groups passed. OR: none passed.
        return !isOr;
    }

    // ── Group evaluation ──────────────────────────────────────────────────────

    private static bool EvaluateGroup(
        RuleGroup group,
        Dictionary<string, ComputedIndicator> indicators,
        IReadOnlyList<ClosedCandle> candles)
    {
        if (group.Conditions.Length == 0) return false;

        var isOr = group.LogicalOperator.Equals("OR", StringComparison.OrdinalIgnoreCase);
        foreach (var condition in group.Conditions)
        {
            try
            {
                var result = EvaluateCondition(condition, indicators, candles);
                if (isOr  &&  result) return true;
                if (!isOr && !result) return false;
            }
            catch
            {
                // Malformed condition (missing indicator, etc.) = false, not a crash
                if (!isOr) return false;
            }
        }
        return !isOr;
    }

    // ── Condition evaluation ──────────────────────────────────────────────────

    private static bool EvaluateCondition(
        Condition condition,
        Dictionary<string, ComputedIndicator> indicators,
        IReadOnlyList<ClosedCandle> candles)
    {
        var op = condition.Operator;

        // sessionState: boolean — no right operand needed
        if (condition.Left.Kind.Equals("sessionState", StringComparison.OrdinalIgnoreCase))
        {
            var val = ResolveSessionState(condition.Left.Property, candles);
            return val > 0;
        }

        // slope: rising/falling check — no numeric right needed
        if (condition.Left.Kind.Equals("slope", StringComparison.OrdinalIgnoreCase))
        {
            var slope = ComputeSlope(condition.Left.IndicatorId, condition.Left.LookbackBars ?? 3, indicators);
            return op is ">" or ">=" ? slope > 0 : slope < 0;
        }

        // isBullishBar / isBearishBar: candle body check — no right operand needed
        if (condition.Left.Kind.Equals("isBullishBar", StringComparison.OrdinalIgnoreCase))
            return candles.Count > 0 && candles[^1].Close > candles[^1].Open;

        if (condition.Left.Kind.Equals("isBearishBar", StringComparison.OrdinalIgnoreCase))
            return candles.Count > 0 && candles[^1].Close < candles[^1].Open;

        // touchedAndBounced: pullback entry operator.
        //   Long version:  Low of current OR previous bar <= indicator value,
        //                  AND current Close > indicator value.
        //   Short version: High of current OR previous bar >= indicator value,
        //                  AND current Close < indicator value.
        //   Operator is "touchedAndBounced" for longs, "touchedAndFailed" for shorts.
        if (op.Equals("touchedAndBounced", StringComparison.OrdinalIgnoreCase) ||
            op.Equals("touchedAndFailed",  StringComparison.OrdinalIgnoreCase))
        {
            return EvaluateTouchedAndBounced(condition, indicators, candles,
                isBullish: op.Equals("touchedAndBounced", StringComparison.OrdinalIgnoreCase));
        }

        // Normal numeric comparison
        var leftVal  = ResolveOperand(condition.Left,  indicators, candles, useCurrentValue: true);
        var rightVal = condition.Right != null
            ? ResolveOperand(condition.Right, indicators, candles, useCurrentValue: true)
            : 0m;

        return op switch
        {
            ">"  or "&gt;"  => leftVal >  rightVal,
            "<"  or "&lt;"  => leftVal <  rightVal,
            ">=" or "&gt;=" => leftVal >= rightVal,
            "<=" or "&lt;=" => leftVal <= rightVal,
            "==" => Math.Abs(leftVal - rightVal) < 0.000001m,
            "!=" => Math.Abs(leftVal - rightVal) >= 0.000001m,

            "crossesAbove" => EvaluateCrossover(condition, indicators, candles, isAbove: true),
            "crossesBelow" => EvaluateCrossover(condition, indicators, candles, isAbove: false),

            _ => throw new InvalidOperationException($"Unknown operator: '{op}'")
        };
    }

    // ── Crossover ────────────────────────────────────────────────────────────

    private static bool EvaluateCrossover(
        Condition condition,
        Dictionary<string, ComputedIndicator> indicators,
        IReadOnlyList<ClosedCandle> candles,
        bool isAbove)
    {
        var leftCurr  = ResolveOperand(condition.Left,  indicators, candles, useCurrentValue: true);
        var rightCurr = condition.Right != null
            ? ResolveOperand(condition.Right, indicators, candles, useCurrentValue: true)
            : 0m;
        var leftPrev  = ResolveOperand(condition.Left,  indicators, candles, useCurrentValue: false);
        var rightPrev = condition.Right != null
            ? ResolveOperand(condition.Right, indicators, candles, useCurrentValue: false)
            : 0m;

        return isAbove
            ? leftCurr > rightCurr && leftPrev <= rightPrev   // crossed above
            : leftCurr < rightCurr && leftPrev >= rightPrev;  // crossed below
    }

    // ── TouchedAndBounced — pullback entry operator ───────────────────────────
    // Long (touchedAndBounced): price wicked into the indicator level on this bar
    // OR the previous bar (candle low <= indicator), and the current close is back
    // ABOVE the indicator. This confirms a pullback-to-support bounce entry.
    //
    // Short (touchedAndFailed): mirror — high >= indicator AND close < indicator.
    //
    // Why this beats crossesAbove for trend entries:
    //   crossesAbove fires when EMA9 just crossed EMA21 — i.e., the trend just started.
    //   By then, the impulsive bar is already 60-70% complete and entry is at the top.
    //   touchedAndBounced fires AFTER the trend is established (EMA9 already > EMA21)
    //   on a pullback reset — entering with the trend at a better price.
    private static bool EvaluateTouchedAndBounced(
        Condition condition,
        Dictionary<string, ComputedIndicator> indicators,
        IReadOnlyList<ClosedCandle> candles,
        bool isBullish)
    {
        if (candles.Count < 2) return false;
        if (condition.Right == null) return false;

        var indicatorLevel = ResolveOperand(condition.Right, indicators, candles, useCurrentValue: true);
        if (indicatorLevel == 0m) return false;

        var current  = candles[^1];
        var previous = candles[^2];

        if (isBullish)
        {
            // Price touched (wicked into) indicator on current or previous bar
            bool touched = current.Low <= indicatorLevel || previous.Low <= indicatorLevel;
            // Current close is back above the indicator — the bounce has confirmed
            bool bounced = current.Close > indicatorLevel;
            return touched && bounced;
        }
        else
        {
            // Price touched (wicked into) indicator on current or previous bar
            bool touched = current.High >= indicatorLevel || previous.High >= indicatorLevel;
            // Current close is back below the indicator — the failure has confirmed
            bool failed  = current.Close < indicatorLevel;
            return touched && failed;
        }
    }

    // ── Operand resolution ────────────────────────────────────────────────────

    private static decimal ResolveOperand(
        ConditionOperand operand,
        Dictionary<string, ComputedIndicator> indicators,
        IReadOnlyList<ClosedCandle> candles,
        bool useCurrentValue)
    {
        switch (operand.Kind.ToLowerInvariant())
        {
            case "indicator":
            {
                if (string.IsNullOrEmpty(operand.IndicatorId)) return 0m;
                if (!indicators.TryGetValue(operand.IndicatorId, out var ind)) return 0m;
                return useCurrentValue ? ind.GetField(operand.Field) : ind.GetPreviousField(operand.Field);
            }

            case "value":
                return operand.Value ?? 0m;

            case "window":
            {
                if (operand.Expr == null) return 0m;
                return ComputeWindow(operand.Expr, indicators);
            }

            case "percentile":
            {
                if (string.IsNullOrEmpty(operand.IndicatorId)) return 0m;
                if (!indicators.TryGetValue(operand.IndicatorId, out var ind)) return 0m;
                return ComputePercentile(ind.History, operand.LookbackBars ?? 100, ind.Current);
            }

            // "absence" — true (1) when indicator is NOT present or has been 0 for N bars.
            case "absence":
            {
                if (string.IsNullOrEmpty(operand.IndicatorId)) return 1m;
                if (!indicators.TryGetValue(operand.IndicatorId, out var ind)) return 1m;
                var n = Math.Min(operand.LookbackBars ?? 1, ind.History.Length);
                if (n == 0) return 1m;
                var allZero = ind.History[^n..].All(v => v == 0m);
                return allZero ? 1m : 0m;
            }

            // ── HTF bias operand ─────────────────────────────────────────────
            // Reads an indicator tagged with role="htfBias" (e.g. 1H EMA21 or 1H SuperTrend).
            // Returns 1 if indicator value > 0 (bullish), 0 if <= 0 (bearish/flat).
            // Usage in a condition: left.kind="htfBias" left.indicatorId="ema21_1h" operator=">" right.value=0
            // HTF indicators must be registered in the indicators array with
            // a timeframe field set to the desired higher timeframe (e.g. "60" for 1H).
            // IndicatorEngine computes them from the same candle feed using
            // the full candle history (not just the last bar) so they are
            // always populated even on 15m strategy runs.
            case "htfbias":
            {
                if (string.IsNullOrEmpty(operand.IndicatorId)) return 0m;
                if (!indicators.TryGetValue(operand.IndicatorId, out var htfInd)) return 0m;
                var htfVal = htfInd.GetField(operand.Field);
                // For SuperTrend: positive value = bullish direction, negative = bearish.
                // For EMA: value is the price level; bias is implicit (use slope condition separately).
                return htfVal > 0 ? 1m : 0m;
            }

            // ── Candle body direction ────────────────────────────────────────
            case "isbullishbar":
                return candles.Count > 0 && candles[^1].Close > candles[^1].Open ? 1m : 0m;
            case "isbearishbar":
                return candles.Count > 0 && candles[^1].Close < candles[^1].Open ? 1m : 0m;

            // ── Current bar OHLCV ────────────────────────────────────────────
            case "close":   return candles.Count > 0 ? candles[^1].Close  : 0m;
            case "open":    return candles.Count > 0 ? candles[^1].Open   : 0m;
            case "high":    return candles.Count > 0 ? candles[^1].High   : 0m;
            case "low":     return candles.Count > 0 ? candles[^1].Low    : 0m;
            case "volume":  return candles.Count > 0 ? candles[^1].Volume : 0m;

            // ── Previous bar OHLCV ───────────────────────────────────────────
            // Useful for: "previous high breakout", "previous bar close above EMA"
            case "prevclose": return candles.Count > 1 ? candles[^2].Close  : 0m;
            case "prevopen":  return candles.Count > 1 ? candles[^2].Open   : 0m;
            case "prevhigh":  return candles.Count > 1 ? candles[^2].High   : 0m;
            case "prevlow":   return candles.Count > 1 ? candles[^2].Low    : 0m;

            default: return 0m;
        }
    }

    // ── Window aggregation ────────────────────────────────────────────────────

    private static decimal ComputeWindow(
        WindowExpression expr,
        Dictionary<string, ComputedIndicator> indicators)
    {
        if (!indicators.TryGetValue(expr.SourceIndicatorId, out var ind)) return 0m;
        var history = ind.History;
        if (history.Length == 0) return 0m;

        var n = Math.Min(expr.LookbackBars, history.Length);
        var window = history[^n..];

        var agg = expr.AggregationType.ToLowerInvariant() switch
        {
            "avg"      or "average" => window.Average(),
            "max"      or "highest" => window.Max(),
            "min"      or "lowest"  => window.Min(),
            "sum"                   => window.Sum(),
            _ => window.Average()
        };

        return expr.Multiplier.HasValue ? agg * expr.Multiplier.Value : agg;
    }

    // ── Slope ────────────────────────────────────────────────────────────────

    private static decimal ComputeSlope(
        string? indicatorId,
        int lookbackBars,
        Dictionary<string, ComputedIndicator> indicators)
    {
        if (string.IsNullOrEmpty(indicatorId)) return 0m;
        if (!indicators.TryGetValue(indicatorId, out var ind)) return 0m;
        var history = ind.History;
        if (history.Length < 2) return 0m;
        var n = Math.Min(lookbackBars, history.Length);
        var oldest  = history[^n];
        var newest  = history[^1];
        return newest > oldest ? 1m : newest < oldest ? -1m : 0m;
    }

    // ── Percentile rank ───────────────────────────────────────────────────────

    private static decimal ComputePercentile(
        decimal[] history,
        int lookbackBars,
        decimal currentValue)
    {
        if (history.Length == 0) return 50m;
        var n = Math.Min(lookbackBars, history.Length);
        var window = history[^n..];
        var below = window.Count(v => v < currentValue);
        return (decimal)below / window.Length * 100m;
    }

    // ── Session state ─────────────────────────────────────────────────────────

    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    private static decimal ResolveSessionState(string? property, IReadOnlyList<ClosedCandle> candles)
    {
        if (candles.Count == 0) return 0m;
        var current = candles[^1];

        return (property ?? "") switch
        {
            // 1 if the current bar is within the first 15 minutes after session open (9:15–9:30 IST)
            "IsFirstSignalOfSession" => IsSessionOpenBar(current) ? 1m : 0m,

            // Number of bars elapsed since 9:15 IST on the same date
            "BarsSinceSessionOpen" => ComputeBarsSinceOpen(candles),

            // Cannot track cross-trade state without external state — return 0 (not available in eval context)
            "PreviousTradeWasLoss" => 0m,

            // Cannot know open position count from candle history alone
            "OpenPositionCount" => 0m,

            _ => 0m
        };
    }

    private static bool IsSessionOpenBar(ClosedCandle candle)
    {
        var zdt = candle.OpenTime;
        var istTime = zdt.WithZone(Ist).TimeOfDay;
        // Session open = 9:15 IST; treat first 15 minutes as "first signal of session"
        return istTime >= LocalTime.FromHourMinuteSecondNanosecond(9, 15, 0, 0)
            && istTime <  LocalTime.FromHourMinuteSecondNanosecond(9, 30, 0, 0);
    }

    private static decimal ComputeBarsSinceOpen(IReadOnlyList<ClosedCandle> candles)
    {
        var current = candles[^1];
        var currentDate = current.OpenTime.WithZone(Ist).Date;
        var sessionOpen = LocalTime.FromHourMinuteSecondNanosecond(9, 15, 0, 0);

        int count = 0;
        for (int i = candles.Count - 1; i >= 0; i--)
        {
            var zdt = candles[i].OpenTime.WithZone(Ist);
            if (zdt.Date != currentDate) break;
            if (zdt.TimeOfDay >= sessionOpen) count++;
        }
        return count;
    }
}
