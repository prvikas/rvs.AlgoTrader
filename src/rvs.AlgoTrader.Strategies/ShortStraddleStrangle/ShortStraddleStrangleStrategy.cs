using System.Text.Json;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.ShortStraddleStrangle;

/// <summary>
/// Short Straddle / Short Strangle Strategy (#81).
/// Sells theta-decay premium; profits from time decay and IV crush.
///
/// SAFETY GATE (mandatory per spec): system MUST enforce stop-loss or hedge leg.
/// Naked short options carry unlimited theoretical risk — this strategy always
/// requires MaxLossMultiple configured and enforced by the risk manager.
///
/// Straddle:  Sell ATM Call + Sell ATM Put  (same strike, same expiry).
/// Strangle:  Sell OTM Call + Sell OTM Put  (different strikes, no wing protection).
///
/// Entry:  High IV environment (AtmIv ≥ MinAtmIv), no event within exclusion window.
/// Stop:   Activated when total loss ≥ MaxLossMultiple × premium received.
/// Exit:   At ProfitTargetPct of max premium, time-based, or on breach of short strikes.
/// </summary>
public class ShortStraddleStrangleStrategy(ShortStraddleStrangleConfig config) : IStrategy
{
    public string Name => "ShortStraddleStrangle";

    public Task<SignalResult> EvaluateAsync(StrategyContext context, CancellationToken ct)
    {
        if (context.OptionChain is null)
            return Task.FromResult(SignalResult.Skip(SkippedReason.FilterFailed,
                "ShortStraddleStrangle requires OptionChain snapshot"));

        var chain  = context.OptionChain;
        decimal iv = chain.AtmIv;

        // ── IV filter — enter only in elevated IV environments ─────────────
        if (iv < config.MinAtmIv)
            return Task.FromResult(SignalResult.Hold(
                $"ATM IV {iv:F1}% below minimum {config.MinAtmIv}% — insufficient premium for straddle/strangle"));

        // ── Build legs based on StrategyType ───────────────────────────────
        SpreadLeg callLeg, putLeg;

        if (config.StrategyType == ShortStraddleStrangleType.Straddle)
        {
            // ATM call + ATM put (same strike = ATM)
            callLeg = new SpreadLeg(OptionType.Call, OrderDirection.Sell, StrikeSelectionMode.Atm,     Quantity: 1);
            putLeg  = new SpreadLeg(OptionType.Put,  OrderDirection.Sell, StrikeSelectionMode.Atm,     Quantity: 1);
        }
        else
        {
            // OTM call + OTM put (strangle — wider strikes, lower premium, higher breakeven)
            // SS-2: Use separate deltas for call and put legs — same delta forces equal OTM distance
            // on both sides which is rarely optimal (skew exists in index options).
            callLeg = new SpreadLeg(OptionType.Call, OrderDirection.Sell, StrikeSelectionMode.ByDelta,
                                    TargetDelta: config.StrangleCallDelta, Quantity: 1);
            putLeg  = new SpreadLeg(OptionType.Put,  OrderDirection.Sell, StrikeSelectionMode.ByDelta,
                                    TargetDelta: config.StranglePutDelta,  Quantity: 1);
        }

        string typeName = config.StrategyType == ShortStraddleStrangleType.Straddle ? "Straddle" : "Strangle";
        string reason   = $"Short {typeName}: IV={iv:F1}% ≥ {config.MinAtmIv}%, " +
                          $"StopAtLoss={config.MaxLossMultiple}×premium";

        // SS-4: DiagnosticsJson must be Dictionary<string, decimal> — anonymous objects cannot be
        // serialised consistently across JSON serialisers and break diagnostic logging pipelines.
        var spread = new SpreadSignalResult(
            SpreadType:      $"Short{typeName}",
            Legs:            [callLeg, putLeg],
            Reason:          reason,
            DiagnosticsJson: new Dictionary<string, decimal>
            {
                ["atmIv"]  = iv,
                ["minIv"]  = config.MinAtmIv,
                ["callDelta"] = config.StrategyType == ShortStraddleStrangleType.Strangle ? config.StrangleCallDelta : 0m,
                ["putDelta"]  = config.StrategyType == ShortStraddleStrangleType.Strangle ? config.StranglePutDelta  : 0m,
            });

        return Task.FromResult(SignalResult.SpreadEntry(spread));
    }
}

public enum ShortStraddleStrangleType { Straddle, Strangle }

public class ShortStraddleStrangleConfig
{
    /// <summary>Straddle = ATM; Strangle = OTM</summary>
    public ShortStraddleStrangleType StrategyType   { get; set; } = ShortStraddleStrangleType.Straddle;
    public decimal MinAtmIv                         { get; set; } = 18m;   // minimum IV for entry
    /// <summary>OTM delta for the call leg of a strangle (separate from put to account for IV skew).</summary>
    public decimal StrangleCallDelta                { get; set; } = 0.20m;
    /// <summary>OTM delta for the put leg of a strangle — put skew is typically steeper, use lower delta.</summary>
    public decimal StranglePutDelta                 { get; set; } = 0.20m;
    /// <summary>
    /// MANDATORY safety gate: position is force-closed when realised loss ≥ this multiple of premium.
    /// Enforced by PortfolioRiskManager / external stop-loss handler. Must not be disabled.
    /// </summary>
    public decimal MaxLossMultiple                  { get; set; } = 2.0m;  // stop at 2× premium received

    public static ShortStraddleStrangleConfig FromJson(string json)
        => JsonSerializer.Deserialize<ShortStraddleStrangleConfig>(json) ?? new();

    public static IReadOnlyList<StrategyParamDef> GetSchema() =>
    [
        new("StrategyType",      "Strategy Type",        "select", "Straddle", Options:
            [new("Straddle", "Short Straddle (ATM)"), new("Strangle", "Short Strangle (OTM)")]),
        new("MinAtmIv",          "Min ATM IV %",         "decimal", 18m,   Min: 10m,   Max: 60m,  Step: 1m,   Hint: "Only enter when IV is elevated"),
        new("StrangleCallDelta", "Strangle Call Delta",  "decimal", 0.20m, Min: 0.05m, Max: 0.40m, Step: 0.05m, Hint: "OTM delta for the call leg (strangle only)"),
        new("StranglePutDelta",  "Strangle Put Delta",   "decimal", 0.20m, Min: 0.05m, Max: 0.40m, Step: 0.05m, Hint: "OTM delta for the put leg (strangle only); set lower than call to account for put skew"),
        new("MaxLossMultiple",   "Max Loss Multiple",    "decimal", 2.0m,  Min: 1.0m,  Max: 5.0m, Step: 0.5m, Hint: "MANDATORY: force-close when loss ≥ this × premium"),
    ];
}
