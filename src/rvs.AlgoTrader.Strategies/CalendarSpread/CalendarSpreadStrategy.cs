using System.Text.Json;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.CalendarSpread;

/// <summary>
/// Calendar Spread Strategy (#82) — Theta + Vega play.
/// Buy far-dated option + Sell near-dated option at the same ATM strike.
///
/// Profits from:
///   1. Faster theta decay in the short near-expiry leg.
///   2. IV expansion benefiting the long far-expiry leg (vega long).
///
/// Entry:  Low-to-mid IV environment (sell near, buy far before an expected IV expansion event).
/// Exit:   Before near-expiry to avoid gamma risk; roll or close at VegaProfitTargetPct.
/// Risk:   Defined — maximum loss is the net debit paid.
///
/// Leg structure:
///   Leg 1 (near): Sell  ATM option, NearestWeekly = true  (near expiry)
///   Leg 2 (far):  Buy   ATM option, NearestWeekly = false (next monthly expiry)
/// </summary>
public class CalendarSpreadStrategy(CalendarSpreadConfig config) : IStrategy
{
    public string Name => "CalendarSpread";

    public Task<SignalResult> EvaluateAsync(StrategyContext context, CancellationToken ct)
    {
        // CS-1: Use NearExpiryChain when available (populated by ForwardTestEngine / StrategyEvaluationQueue).
        // Fall back to the legacy single OptionChain for backward compatibility.
        // Filter both chains to ±ChainStrikeDepth strikes so IV slope is measured from liquid strikes only.
        var nearChain = (context.NearExpiryChain ?? context.OptionChain)
            ?.FilteredAroundAtm(config.StrikeInterval, config.ChainStrikeDepth);
        var farChain  = context.FarExpiryChain
            ?.FilteredAroundAtm(config.StrikeInterval, config.ChainStrikeDepth);

        if (nearChain is null)
            return Task.FromResult(SignalResult.Skip(SkippedReason.FilterFailed,
                "CalendarSpread requires OptionChain snapshot"));

        decimal iv = nearChain.AtmIv;

        // CS-2: IV term-structure slope filter.
        // Calendar spreads earn vega P&L when near IV > far IV (sell overpriced near theta,
        // buy cheaper far vega).  Require slope ≥ MinIvSlope to confirm the term structure
        // supports the trade; skip when far chain is unavailable (degrade gracefully).
        decimal ivSlope = 0m;
        if (farChain != null)
        {
            ivSlope = nearChain.AtmIv - farChain.AtmIv;
            if (ivSlope < config.MinIvSlope)
                return Task.FromResult(SignalResult.Hold(
                    $"IV slope {ivSlope:F1}% (near={nearChain.AtmIv:F1}% − far={farChain.AtmIv:F1}%) " +
                    $"below minimum {config.MinIvSlope}% — near premium not elevated enough vs far expiry"));
        }

        // ── IV filter: prefer low-to-mid IV (buy cheap long-dated vega) ──
        if (iv > config.MaxAtmIv)
            return Task.FromResult(SignalResult.Hold(
                $"ATM IV {iv:F1}% above maximum {config.MaxAtmIv}% — long vega too expensive for calendar"));

        if (iv < config.MinAtmIv)
            return Task.FromResult(SignalResult.Hold(
                $"ATM IV {iv:F1}% below minimum {config.MinAtmIv}% — near-expiry premium insufficient"));

        // ── Choose call or put calendar based on market bias ───────────────
        OptionType optType;
        string biasReason;
        if (nearChain.IsBullishBias && config.AllowCallCalendar)
        {
            optType    = OptionType.Call;
            biasReason = "bullish bias (call calendar)";
        }
        else if (nearChain.IsBearishBias && config.AllowPutCalendar)
        {
            optType    = OptionType.Put;
            biasReason = "bearish bias (put calendar)";
        }
        else if (config.AllowCallCalendar)
        {
            optType    = OptionType.Call;
            biasReason = "neutral (default call calendar)";
        }
        else
        {
            return Task.FromResult(SignalResult.Hold("No calendar type allowed by config for current bias"));
        }

        // ── Build 2 legs: sell near-expiry + buy far-expiry ────────────────
        var nearLeg = new SpreadLeg(
            OptionType:    optType,
            Direction:     OrderDirection.Sell,
            SelectionMode: StrikeSelectionMode.Atm,
            NearestWeekly: true,   // near-dated
            Quantity:      1);

        var farLeg = new SpreadLeg(
            OptionType:    optType,
            Direction:     OrderDirection.Buy,
            SelectionMode: StrikeSelectionMode.Atm,
            NearestWeekly: false,  // far-dated (monthly)
            Quantity:      1);

        var spread = new SpreadSignalResult(
            SpreadType:      "CalendarSpread",
            Legs:            [nearLeg, farLeg],
            Reason:          $"Calendar Spread: nearIV={iv:F1}%, slope={ivSlope:F1}%, {biasReason}, " +
                             $"sell near-ATM {optType} + buy far-ATM {optType}",
            DiagnosticsJson: new Dictionary<string, decimal>
            {
                ["nearAtmIv"] = nearChain.AtmIv,
                ["farAtmIv"]  = farChain?.AtmIv ?? 0m,
                ["ivSlope"]   = ivSlope,
                ["spotPrice"] = nearChain.SpotPrice,
            },
            SpotPrice:       nearChain.SpotPrice,
            NearExpiryDate:  nearChain.Expiry);

        return Task.FromResult(SignalResult.SpreadEntry(spread));
    }
}

public class CalendarSpreadConfig
{
    public decimal MinAtmIv           { get; set; } = 8m;     // near leg needs some premium
    public decimal MaxAtmIv           { get; set; } = 25m;    // above this → long vega too expensive
    public bool    AllowCallCalendar   { get; set; } = true;
    public bool    AllowPutCalendar    { get; set; } = true;

    /// <summary>
    /// CS-2: Minimum IV slope (near ATM IV − far ATM IV) required to enter.
    /// Calendar spread is most profitable when near-expiry volatility is elevated
    /// relative to far-expiry (sell expensive near theta, buy cheaper far vega).
    /// Default 2.0% — requires near IV to exceed far IV by at least 2 percentage points.
    /// Set to 0 to disable when only a single option chain is available.
    /// </summary>
    public decimal MinIvSlope          { get; set; } = 2.0m;

    /// <summary>
    /// Profit target as a fraction of the net debit paid (e.g. 0.50 = 50% of max debit recovered).
    /// Used by SpreadBacktestEngine to close the position when this profit is reached.
    /// </summary>
    public decimal VegaProfitTargetPct { get; set; } = 0.50m;
    /// <summary>Number of strikes on each side of ATM included for IV slope computation.
    /// Filters out far-OTM hedges so near/far IV comparison is clean.</summary>
    public int     ChainStrikeDepth    { get; set; } = 5;
    /// <summary>Strike interval (50 = NIFTY, 100 = BANKNIFTY).</summary>
    public decimal StrikeInterval      { get; set; } = 50m;

    public static CalendarSpreadConfig FromJson(string json)
        => JsonSerializer.Deserialize<CalendarSpreadConfig>(json) ?? new();

    public static IReadOnlyList<StrategyParamDef> GetSchema() =>
    [
        new("MinAtmIv",           "Min ATM IV %",           "decimal", 8m,    Min: 3m,   Max: 20m,  Step: 1m),
        new("MaxAtmIv",           "Max ATM IV %",           "decimal", 25m,   Min: 10m,  Max: 60m,  Step: 5m,   Hint: "Avoid expensive long vega above this IV"),
        new("AllowCallCalendar",  "Allow Call Calendar",    "bool",    true,  Hint: "Enter call calendars on bullish/neutral bias"),
        new("AllowPutCalendar",   "Allow Put Calendar",     "bool",    true,  Hint: "Enter put calendars on bearish bias"),
        new("MinIvSlope",         "Min IV Slope %",         "decimal", 2.0m,  Min: 0m,   Max: 10m,  Step: 0.5m, Hint: "CS-2: near IV must exceed far IV by at least this %. 0 = skip slope check."),
        new("VegaProfitTargetPct","Vega Profit Target",     "decimal", 0.50m, Min: 0.1m, Max: 1.0m, Step: 0.05m,Hint: "Close when this fraction of net debit is recovered as profit"),
        new("ChainStrikeDepth",   "Chain Strike Depth",     "int",     5,     Min: 1,    Max: 20,   Hint: "Strikes each side of ATM for IV slope analytics. Excludes far-OTM hedges."),
        new("StrikeInterval",     "Strike Interval (pts)",  "decimal", 50m,   Min: 10m,  Max: 500m, Step: 10m,  Hint: "50 for NIFTY, 100 for BANKNIFTY"),
    ];
}
