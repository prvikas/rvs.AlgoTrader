using System.Text.Json;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.FibOptionSpread;

/// <summary>
/// STRAT-002: Fibonacci Hedged Option Spread.
/// Spec: docs/STRATEGY_SPECS.md § STRAT-002
///
/// Filter:  ATM IV ≥ MinAtmIv (from OptionChain), no naked selling.
/// Setup:   swing high/low → Fib retracement levels.
/// Entry:   price in Fib 1.618 extension zone (or 0.618 deep retracement for reversal).
/// Direction: uptrend (close &gt; SMA50) → put credit spread; downtrend → call credit spread.
/// Stop:    underlying breaches 0.786 level.
/// Structure: sell closer OTM strike + buy further OTM strike (defined-risk spread).
/// </summary>
public class FibOptionSpreadStrategy(FibOptionSpreadConfig config) : IStrategy
{
    public string Name => "FibOptionSpread";

    private static readonly decimal[] FibLevels = [0.236m, 0.382m, 0.500m, 0.618m, 0.786m, 1.000m, 1.618m];

    public Task<SignalResult> EvaluateAsync(StrategyContext context, CancellationToken ct)
    {
        var candles = context.Candles;
        int required = config.SwingLookback + config.Sma50Period + 5;
        if (candles.Count < required)
            return Task.FromResult(SignalResult.Skip(SkippedReason.InsufficientData,
                $"Need {required} candles, have {candles.Count}"));

        var current = candles[^1];

        // ── Event calendar filter (spec: no event within exclusion_days window) ──
        if (config.ExclusionDays > 0 && context.HasUpcomingEvent)
            return Task.FromResult(SignalResult.Hold(
                $"High-impact event within {config.ExclusionDays} days — no spread entry (EventCalendarService)"));

        // ── IV Percentile filter (spec: IVP ≥ threshold, requires 60-day warmup) ──
        if (context.SymbolIvRank is not null)
        {
            if (context.SymbolIvRank.DataPointsUsed < 60)
                return Task.FromResult(SignalResult.Hold(
                    $"IV history warmup incomplete ({context.SymbolIvRank.DataPointsUsed}/60 days) — cannot compute reliable IVP"));
            if (context.SymbolIvRank.IvPercentile < config.MinIvp)
                return Task.FromResult(SignalResult.Hold(
                    $"IVP {context.SymbolIvRank.IvPercentile:F0}% below threshold {config.MinIvp}% — premium insufficient for spread"));
        }

        // ── IV (ATM) filter ───────────────────────────────────────────────
        if (context.OptionChain is not null)
        {
            if (context.OptionChain.AtmIv < config.MinAtmIv)
                return Task.FromResult(SignalResult.Hold(
                    $"ATM IV {context.OptionChain.AtmIv:F1}% below minimum {config.MinAtmIv}% — spread premium insufficient"));
        }

        // ── SMA50 trend ───────────────────────────────────────────────────
        decimal sma50 = candles.TakeLast(config.Sma50Period).Average(c => c.Close);
        bool isUptrend = current.Close > sma50;

        // ── Swing high/low detection ───────────────────────────────────────
        int swingEnd   = candles.Count - 1;
        int swingStart = Math.Max(0, swingEnd - config.SwingLookback);
        decimal swingHigh = decimal.MinValue;
        decimal swingLow  = decimal.MaxValue;
        for (int i = swingStart; i <= swingEnd; i++)
        {
            if (candles[i].High > swingHigh) swingHigh = candles[i].High;
            if (candles[i].Low  < swingLow)  swingLow  = candles[i].Low;
        }
        decimal range = swingHigh - swingLow;
        if (range <= 0)
            return Task.FromResult(SignalResult.Hold("Swing range is zero"));

        // ── Fib levels ────────────────────────────────────────────────────
        // For uptrend: retracement is measured down from swing high
        // Entry zone: price near 1.618 extension above swing high, or deep retracement 0.618
        // For downtrend: retracement is measured up from swing low
        decimal fib618;
        decimal fib786;
        decimal fib1618;
        string direction;

        if (isUptrend)
        {
            fib618  = swingHigh - range * 0.618m;   // 61.8% retracement = support
            fib786  = swingHigh - range * 0.786m;   // 78.6% = deep support / invalidation
            // FIB-1: 1.618 extension is ABOVE swing high for an uptrend (range*0.618 added above),
            // not below swingLow. Previous code was inverted — yielded a level below the base,
            // meaning the entry zone was always in a bearish area on a bullish chart.
            fib1618 = swingHigh + range * 0.618m;   // 1.618 extension above swingHigh (put spread entry zone)
            direction = "put-credit-spread";
        }
        else
        {
            fib618  = swingLow  + range * 0.618m;   // 61.8% retracement from low = resistance
            fib786  = swingLow  + range * 0.786m;   // 78.6% = deep resistance / invalidation
            // FIB-1: 1.618 extension is BELOW swing low for a downtrend.
            fib1618 = swingLow  - range * 0.618m;   // 1.618 extension below swingLow (call spread entry zone)
            direction = "call-credit-spread";
        }

        // ── Entry zone: Fib 1.618 extension (spec: "1.618 = entry zone") ────
        // For uptrend: 1.618 ext = swingHigh - range*1.618 (below swing low) = put-spread entry
        // For downtrend: 1.618 ext = swingLow + range*1.618 (above swing high) = call-spread entry
        decimal entryZoneBuffer = range * config.EntryZonePct / 100m;
        bool inEntryZone = current.Close >= fib1618 - entryZoneBuffer
                        && current.Close <= fib1618 + entryZoneBuffer;

        if (!inEntryZone)
            return Task.FromResult(SignalResult.Hold(
                $"Price {current.Close:F2} not in Fib 1.618 zone ({fib1618 - entryZoneBuffer:F2}–{fib1618 + entryZoneBuffer:F2})"));

        var indicators = new Dictionary<string, decimal>
        {
            ["swingHigh"] = swingHigh,
            ["swingLow"]  = swingLow,
            ["fib618"]    = fib618,
            ["fib786"]    = fib786,
            ["fib1618"]   = fib1618,
            ["sma50"]     = sma50,
        };

        // ── Build spread legs ──────────────────────────────────────────────
        SpreadLeg shortLeg, longLeg;
        if (isUptrend)
        {
            // Put credit spread: sell closer put, buy further OTM put (wing)
            shortLeg = new SpreadLeg(
                OptionType:    OptionType.Put,
                Direction:     OrderDirection.Sell,
                SelectionMode: StrikeSelectionMode.ByDelta,
                TargetDelta:   config.ShortLegDelta,
                Quantity:      1);
            longLeg = new SpreadLeg(
                OptionType:    OptionType.Put,
                Direction:     OrderDirection.Buy,
                SelectionMode: StrikeSelectionMode.OtmByStrike,
                OtmStrikes:    config.WingWidthStrikes,
                Quantity:      1);
        }
        else
        {
            // Call credit spread: sell closer call, buy further OTM call (wing)
            shortLeg = new SpreadLeg(
                OptionType:    OptionType.Call,
                Direction:     OrderDirection.Sell,
                SelectionMode: StrikeSelectionMode.ByDelta,
                TargetDelta:   config.ShortLegDelta,
                Quantity:      1);
            longLeg = new SpreadLeg(
                OptionType:    OptionType.Call,
                Direction:     OrderDirection.Buy,
                SelectionMode: StrikeSelectionMode.OtmByStrike,
                OtmStrikes:    config.WingWidthStrikes,
                Quantity:      1);
        }

        // Stop: underlying breaches 0.786 level (spec: "stop: underlying breaches 0.786")
        // Returned as SignalResult.StopLoss so execution engines can wire trailing-stop management.
        decimal stopLevel = fib786;

        var spread = new SpreadSignalResult(
            SpreadType:         direction,
            Legs:               [shortLeg, longLeg],
            Reason:             $"Fib 1.618 entry: price={current.Close:F2}, zone={fib1618:F2}, " +
                                $"stop@0.786={fib786:F2}, {direction}, wing={config.WingWidthStrikes} strikes",
            DiagnosticsJson:    indicators,
            // FIB-2: Pass the 0.786 Fib level as UnderlyingStopLevel. SpreadOrderManager polls
            // the underlying price and force-closes the spread if it breaches this level,
            // regardless of the option leg P&L. This gives the spread a hard underlying stop.
            UnderlyingStopLevel: stopLevel);

        // Wrap in a SignalResult that carries the 0.786 stop so TrailingStopManager can act on it.
        return Task.FromResult(new SignalResult(
            Signal:          SignalType.Buy,
            EntryPrice:      current.Close,
            StopLoss:        stopLevel,
            TakeProfit:      null,           // premium capture target managed by ISpreadOrderManager
            Reason:          spread.Reason,
            DiagnosticsJson: indicators,
            SkippedReason:   null,
            IndicatorValues: null,
            OptionsLeg:      null,
            Spread:          spread));
    }
}

public class FibOptionSpreadConfig
{
    public int     SwingLookback    { get; set; } = 50;
    public int     Sma50Period      { get; set; } = 50;
    public decimal MinAtmIv         { get; set; } = 12m;    // minimum ATM IV % to trade
    public decimal MinIvp           { get; set; } = 30m;    // minimum IV Percentile (0-100); 0 = disable
    public int     ExclusionDays    { get; set; } = 3;      // skip entry within N days of a high-impact event; 0 = disable
    public decimal EntryZonePct     { get; set; } = 2.0m;   // % buffer around Fib 1.618 extension zone
    public decimal ShortLegDelta    { get; set; } = 0.25m;  // sell ~25-delta option
    public int     WingWidthStrikes { get; set; } = 2;       // strike intervals for hedge leg

    public static FibOptionSpreadConfig FromJson(string json)
    {
        var p = JsonSerializer.Deserialize<FibOptionSpreadConfig>(json) ?? new FibOptionSpreadConfig();
        if (p.SwingLookback    <= 0) throw new ArgumentException("SwingLookback must be > 0",    nameof(p.SwingLookback));
        if (p.Sma50Period      <= 0) throw new ArgumentException("Sma50Period must be > 0",      nameof(p.Sma50Period));
        if (p.WingWidthStrikes <= 0) throw new ArgumentException("WingWidthStrikes must be > 0", nameof(p.WingWidthStrikes));
        if (p.ShortLegDelta    <= 0) throw new ArgumentException("ShortLegDelta must be > 0",    nameof(p.ShortLegDelta));
        if (p.EntryZonePct     <  0) throw new ArgumentException("EntryZonePct must be >= 0",    nameof(p.EntryZonePct));
        return p;
    }

    public static IReadOnlyList<StrategyParamDef> GetSchema() =>
    [
        new("SwingLookback",    "Swing Lookback Bars",    "int",     50,    Min: 20,   Max: 200),
        new("Sma50Period",      "SMA 50 Period",          "int",     50,    Min: 20,   Max: 200),
        new("MinAtmIv",         "Min ATM IV %",           "decimal", 12m,   Min: 5m,   Max: 50m,  Step: 1m,  Hint: "Minimum ATM implied volatility to enter spread"),
        new("MinIvp",           "Min IV Percentile",      "decimal", 30m,   Min: 0m,   Max: 100m, Step: 5m,  Hint: "Minimum IVP (0-100) required; 0 = disabled; requires 60-day warmup"),
        new("ExclusionDays",    "Event Exclusion Days",   "int",     3,     Min: 0,    Max: 14,   Hint: "Skip entry within N calendar days of any high-impact event; 0 = disabled"),
        new("EntryZonePct",     "Entry Zone Buffer %",    "decimal", 2.0m,  Min: 0.5m, Max: 10m,  Step: 0.5m, Hint: "% buffer around Fib 1.618 extension level for entry zone"),
        new("ShortLegDelta",    "Short Leg Delta",        "decimal", 0.25m, Min: 0.1m, Max: 0.5m, Step: 0.05m, Hint: "Delta of the short option leg (~0.25 = 25-delta)"),
        new("WingWidthStrikes", "Wing Width (Strikes)",   "int",     2,     Min: 1,    Max: 10,   Hint: "Number of strike intervals between short and long leg"),
    ];
}
