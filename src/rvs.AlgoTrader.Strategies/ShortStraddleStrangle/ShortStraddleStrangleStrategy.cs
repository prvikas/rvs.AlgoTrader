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

    // Signal is driven entirely by OptionChain (IV, DTE) — no candle-based indicator warmup.
    // MinWarmupBars = 1 so the engine has at least one closed candle for timestamp resolution.
    public int MinWarmupBars => 1;

    public Task<SignalResult> EvaluateAsync(StrategyContext context, CancellationToken ct)
    {
        if (context.OptionChain is null)
            return Task.FromResult(SignalResult.Skip(SkippedReason.FilterFailed,
                "ShortStraddleStrangle requires OptionChain snapshot"));

        // Filter to ±ChainStrikeDepth strikes from ATM before reading IV or DTE.
        var chain  = context.OptionChain.FilteredAroundAtm(config.StrikeInterval, config.ChainStrikeDepth);
        decimal iv = chain.AtmIv;

        // ── SS-3: DTE filter — only sell premium in the optimal theta decay window ─
        if (config.MinDte > 0 || config.MaxDte > 0)
        {
            var currentDate = context.Candles[^1].OpenTime.Date;
            int dte = (chain.Expiry - currentDate).Days;
            if (dte < config.MinDte)
                return Task.FromResult(SignalResult.Hold(
                    $"DTE {dte}d below minimum {config.MinDte}d — too close to expiry, premium decays non-linearly"));
            if (config.MaxDte > 0 && dte > config.MaxDte)
                return Task.FromResult(SignalResult.Hold(
                    $"DTE {dte}d above maximum {config.MaxDte}d — too far from expiry, theta decay too slow"));
        }

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
                ["atmIv"]     = iv,
                ["minIv"]     = config.MinAtmIv,
                ["callDelta"] = config.StrategyType == ShortStraddleStrangleType.Strangle ? config.StrangleCallDelta : 0m,
                ["putDelta"]  = config.StrategyType == ShortStraddleStrangleType.Strangle ? config.StranglePutDelta  : 0m,
            },
            SpotPrice:       chain.SpotPrice,
            NearExpiryDate:  chain.Expiry);

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
    /// <summary>
    /// Close the spread when this fraction of entry credit is captured as profit (e.g. 0.50 = 50%).
    /// Used by SpreadBacktestEngine. Default 0.50 — close at 50% of max profit.
    /// </summary>
    public decimal ProfitTargetPct                  { get; set; } = 0.50m;
    /// <summary>Number of strikes on each side of ATM included in chain analytics (AtmIv, PCR).
    /// 5 = ±5 strikes. Excludes far-OTM hedges from IV measurement.</summary>
    public int     ChainStrikeDepth                 { get; set; } = 5;
    /// <summary>Strike interval (50 = NIFTY, 100 = BANKNIFTY). Used with ChainStrikeDepth.</summary>
    public decimal StrikeInterval                   { get; set; } = 50m;

    // SS-3: DTE filter — sell premium only in the optimal theta-decay window.
    // Too close to expiry (< MinDte): gamma risk spikes; small moves cause outsized losses.
    // Too far from expiry (> MaxDte): theta decay is slow; capital tied up with low daily P&L.
    /// <summary>Minimum DTE to enter. Default 3 — avoids gamma risk in final days before expiry.</summary>
    public int    MinDte                            { get; set; } = 3;
    /// <summary>Maximum DTE to enter. Default 14 — ensures theta decay is meaningful. Set 0 to disable upper cap.</summary>
    public int    MaxDte                            { get; set; } = 14;

    public static ShortStraddleStrangleConfig FromJson(string json)
        => JsonSerializer.Deserialize<ShortStraddleStrangleConfig>(json) ?? new();

    public static IReadOnlyList<StrategyParamDef> GetSchema() =>
    [
        new("StrategyType",      "Strategy Type",        "select", "Straddle", Options:
            [new("Straddle", "Short Straddle (ATM)"), new("Strangle", "Short Strangle (OTM)")]),
        new("MinAtmIv",          "Min ATM IV %",         "decimal", 18m,   Min: 10m,   Max: 60m,  Step: 1m,   Hint: "Only enter when IV is elevated"),
        new("StrangleCallDelta", "Strangle Call Delta",  "decimal", 0.20m, Min: 0.05m, Max: 0.40m, Step: 0.05m, Hint: "OTM delta for the call leg (strangle only)"),
        new("StranglePutDelta",  "Strangle Put Delta",   "decimal", 0.20m, Min: 0.05m, Max: 0.40m, Step: 0.05m, Hint: "OTM delta for the put leg (strangle only); set lower than call to account for put skew"),
        new("MaxLossMultiple",   "Max Loss Multiple",    "decimal", 2.0m,  Min: 1.0m,  Max: 5.0m,  Step: 0.5m, Hint: "MANDATORY: force-close when loss ≥ this × premium"),
        new("ProfitTargetPct",   "Profit Target %",      "decimal", 0.50m, Min: 0.10m, Max: 1.0m,  Step: 0.05m,Hint: "Close when this fraction of entry credit is captured (backtest engine)"),
        new("MinDte",            "Min DTE",              "int",     3,     Min: 0,    Max: 30,    Hint: "Minimum days to expiry for entry (SS-3). 0 = disabled."),
        new("MaxDte",            "Max DTE",              "int",     14,    Min: 0,    Max: 90,    Hint: "Maximum days to expiry for entry (SS-3). 0 = no upper cap."),
        new("ChainStrikeDepth",  "Chain Strike Depth",   "int",     5,     Min: 1,    Max: 20,    Hint: "Strikes each side of ATM for IV analytics. Excludes far-OTM hedges."),
        new("StrikeInterval",    "Strike Interval (pts)","decimal", 50m,   Min: 10m,  Max: 500m,  Step: 10m, Hint: "50 for NIFTY, 100 for BANKNIFTY"),
    ];
}
