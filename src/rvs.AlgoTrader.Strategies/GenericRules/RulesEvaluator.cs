using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.GenericRules;

// ─────────────────────────────────────────────────────────────────────────────
// RulesEvaluator — evaluates an EntryExitBlock (RuleGroup tree) against
// computed indicator values and the current candle.
//
// Operator support:
//   ">" | "<" | ">=" | "<=" | "==" | "!=" | "crossesAbove" | "crossesBelow"
//
// Operand kind support:
//   "indicator"   — computed indicator value (by id + optional field)
//   "value"       — static decimal
//   "window"      — aggregated over N bars (avg/max/min/highest/lowest)
//   "percentile"  — % rank of indicator over lookback bars
//   "slope"       — positive (1) if indicator rose over lookback bars, -1 if fell, 0 flat
//   "close"|"open"|"high"|"low"|"volume" — current bar OHLCV
//   "sessionState" — boolean session properties (treated as 1 or 0)
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
        if (group.Conditions.Length == 0) return true; // empty group = no filter

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

            _ => false   // unknown operator
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

            // OHLCV shorthand operands
            case "close":   return candles.Count > 0 ? candles[^1].Close  : 0m;
            case "open":    return candles.Count > 0 ? candles[^1].Open   : 0m;
            case "high":    return candles.Count > 0 ? candles[^1].High   : 0m;
            case "low":     return candles.Count > 0 ? candles[^1].Low    : 0m;
            case "volume":  return candles.Count > 0 ? candles[^1].Volume : 0m;

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

    private static decimal ResolveSessionState(string? property, IReadOnlyList<ClosedCandle> candles)
    {
        if (candles.Count == 0) return 0m;
        return (property ?? "") switch
        {
            // Additional session state properties can be added here
            _ => 0m
        };
    }
}
