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
        if (context.OptionChain is null)
            return Task.FromResult(SignalResult.Skip(SkippedReason.FilterFailed,
                "CalendarSpread requires OptionChain snapshot"));

        var chain  = context.OptionChain;
        decimal iv = chain.AtmIv;

        // ── IV filter: prefer low IV (buy cheap long-dated vega) ──────────
        if (iv > config.MaxAtmIv)
            return Task.FromResult(SignalResult.Hold(
                $"ATM IV {iv:F1}% above maximum {config.MaxAtmIv}% — long vega too expensive for calendar"));

        if (iv < config.MinAtmIv)
            return Task.FromResult(SignalResult.Hold(
                $"ATM IV {iv:F1}% below minimum {config.MinAtmIv}% — near-expiry premium insufficient"));

        // ── Choose call or put calendar based on market bias ───────────────
        OptionType optType;
        string biasReason;
        if (chain.IsBullishBias && config.AllowCallCalendar)
        {
            optType    = OptionType.Call;
            biasReason = "bullish bias (call calendar)";
        }
        else if (chain.IsBearishBias && config.AllowPutCalendar)
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
            Reason:          $"Calendar Spread: IV={iv:F1}%, {biasReason}, " +
                             $"sell near-ATM {optType} + buy far-ATM {optType}",
            DiagnosticsJson: new { atmIv = iv, optionType = optType.ToString() });

        return Task.FromResult(SignalResult.SpreadEntry(spread));
    }
}

public class CalendarSpreadConfig
{
    public decimal MinAtmIv         { get; set; } = 8m;    // near leg needs some premium
    public decimal MaxAtmIv         { get; set; } = 25m;   // above this → long vega too expensive
    public bool    AllowCallCalendar { get; set; } = true;
    public bool    AllowPutCalendar  { get; set; } = true;

    public static CalendarSpreadConfig FromJson(string json)
        => JsonSerializer.Deserialize<CalendarSpreadConfig>(json) ?? new();

    public static IReadOnlyList<StrategyParamDef> GetSchema() =>
    [
        new("MinAtmIv",         "Min ATM IV %",      "decimal", 8m,   Min: 3m,  Max: 20m, Step: 1m),
        new("MaxAtmIv",         "Max ATM IV %",      "decimal", 25m,  Min: 10m, Max: 60m, Step: 5m,   Hint: "Avoid expensive long vega above this IV"),
        new("AllowCallCalendar","Allow Call Calendar","bool",    true,  Hint: "Enter call calendars on bullish/neutral bias"),
        new("AllowPutCalendar", "Allow Put Calendar", "bool",    true,  Hint: "Enter put calendars on bearish bias"),
    ];
}
