using System.Text.Json;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.IronCondor;

/// <summary>
/// Iron Condor Strategy (#80).
/// Sells an OTM call spread + OTM put spread simultaneously.
/// Profits when underlying stays within short strikes until expiry.
/// Ideal for Nifty/BankNifty weekly expiry in low-to-mid IV environments.
///
/// Structure:
///   Leg 1: Sell OTM Call  (short call, receives premium)
///   Leg 2: Buy  far OTM Call wing (long call, limits upside risk)
///   Leg 3: Sell OTM Put   (short put, receives premium)
///   Leg 4: Buy  far OTM Put wing  (long put, limits downside risk)
///
/// Entry: ATM IV in [MinAtmIv, MaxAtmIv] range + price range-bound (within Bollinger Bands).
/// Exit:  At ProfitTargetPct of max credit, or on breach of short strikes.
/// </summary>
public class IronCondorStrategy(IronCondorConfig config) : IStrategy
{
    public string Name => "IronCondor";

    public Task<SignalResult> EvaluateAsync(StrategyContext context, CancellationToken ct)
    {
        var candles = context.Candles;
        if (candles.Count < config.BollingerPeriod + 5)
            return Task.FromResult(SignalResult.Skip(SkippedReason.InsufficientData,
                $"Need {config.BollingerPeriod + 5} candles"));

        if (context.OptionChain is null)
            return Task.FromResult(SignalResult.Skip(SkippedReason.FilterFailed,
                "IronCondor requires OptionChain snapshot"));

        var chain  = context.OptionChain;
        decimal iv = chain.AtmIv;

        // IC-3: AtmIv == 0 means the chain snapshot has no valid IV data — do not enter.
        if (iv <= 0)
            return Task.FromResult(SignalResult.Skip(SkippedReason.FilterFailed,
                "IronCondor: AtmIv is zero — option chain IV data unavailable, cannot size legs"));

        // ── IV range filter ────────────────────────────────────────────────
        if (iv < config.MinAtmIv)
            return Task.FromResult(SignalResult.Hold(
                $"ATM IV {iv:F1}% below minimum {config.MinAtmIv}% — insufficient premium"));
        if (iv > config.MaxAtmIv)
            return Task.FromResult(SignalResult.Hold(
                $"ATM IV {iv:F1}% above maximum {config.MaxAtmIv}% — directional risk too high"));

        // ── Range-bound check: price inside Bollinger Bands ────────────────
        var current = candles[^1];
        var closes  = candles.TakeLast(config.BollingerPeriod).Select(c => c.Close).ToArray();
        decimal sma = closes.Average();
        decimal std = (decimal)Math.Sqrt((double)closes.Average(c => (c - sma) * (c - sma)));
        decimal bbUpper = sma + config.BollingerStdDev * std;
        decimal bbLower = sma - config.BollingerStdDev * std;

        if (current.Close >= bbUpper || current.Close <= bbLower)
            return Task.FromResult(SignalResult.Hold(
                $"Price {current.Close:F2} outside BB({config.BollingerPeriod},{config.BollingerStdDev}) " +
                $"[{bbLower:F2},{bbUpper:F2}] — not range-bound"));

        // ── Build 4 legs ───────────────────────────────────────────────────
        var legs = new[]
        {
            // Bear call spread
            new SpreadLeg(OptionType.Call, OrderDirection.Sell, StrikeSelectionMode.ByDelta,
                          TargetDelta: config.ShortCallDelta, Quantity: 1),
            new SpreadLeg(OptionType.Call, OrderDirection.Buy,  StrikeSelectionMode.OtmByStrike,
                          OtmStrikes: config.WingWidthStrikes, Quantity: 1),
            // Bull put spread
            new SpreadLeg(OptionType.Put,  OrderDirection.Sell, StrikeSelectionMode.ByDelta,
                          TargetDelta: config.ShortPutDelta,  Quantity: 1),
            new SpreadLeg(OptionType.Put,  OrderDirection.Buy,  StrikeSelectionMode.OtmByStrike,
                          OtmStrikes: config.WingWidthStrikes, Quantity: 1),
        };

        var indicators = new Dictionary<string, decimal>
        {
            ["atmIv"]   = iv,
            ["sma"]     = sma,
            ["bbUpper"] = bbUpper,
            ["bbLower"] = bbLower,
        };

        var spread = new SpreadSignalResult(
            SpreadType:      "IronCondor",
            Legs:            legs,
            Reason:          $"Iron Condor: IV={iv:F1}%, range-bound [{bbLower:F2},{bbUpper:F2}], " +
                             $"short Δcall={config.ShortCallDelta}, short Δput={config.ShortPutDelta}",
            DiagnosticsJson: indicators,
            SpotPrice:       chain.SpotPrice,
            NearExpiryDate:  chain.Expiry);

        return Task.FromResult(SignalResult.SpreadEntry(spread));
    }
}

public class IronCondorConfig
{
    public decimal MinAtmIv          { get; set; } = 12m;    // minimum IV to have enough premium
    public decimal MaxAtmIv          { get; set; } = 35m;    // too high = directional risk
    public decimal ShortCallDelta    { get; set; } = 0.20m;  // sell ~20-delta call
    public decimal ShortPutDelta     { get; set; } = 0.20m;  // sell ~20-delta put
    public int     WingWidthStrikes  { get; set; } = 2;       // strike intervals for each wing
    public int     BollingerPeriod   { get; set; } = 20;
    public decimal BollingerStdDev   { get; set; } = 2.0m;

    public static IronCondorConfig FromJson(string json)
        => JsonSerializer.Deserialize<IronCondorConfig>(json) ?? new();

    public static IReadOnlyList<StrategyParamDef> GetSchema() =>
    [
        new("MinAtmIv",         "Min ATM IV %",           "decimal", 12m,   Min: 5m,   Max: 30m,  Step: 1m),
        new("MaxAtmIv",         "Max ATM IV %",           "decimal", 35m,   Min: 15m,  Max: 80m,  Step: 5m),
        new("ShortCallDelta",   "Short Call Delta",       "decimal", 0.20m, Min: 0.05m, Max: 0.45m, Step: 0.05m),
        new("ShortPutDelta",    "Short Put Delta",        "decimal", 0.20m, Min: 0.05m, Max: 0.45m, Step: 0.05m),
        new("WingWidthStrikes", "Wing Width (Strikes)",   "int",     2,     Min: 1,    Max: 10),
        new("BollingerPeriod",  "Bollinger Period",       "int",     20,    Min: 10,   Max: 50),
        new("BollingerStdDev",  "Bollinger Std Dev",      "decimal", 2.0m,  Min: 1.0m, Max: 3.0m, Step: 0.5m),
    ];
}
