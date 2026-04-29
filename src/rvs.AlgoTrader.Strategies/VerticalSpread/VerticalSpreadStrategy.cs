using System.Text.Json;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.VerticalSpread;

/// <summary>
/// Bull/Bear Vertical Spread Strategy (#83).
/// Directional, defined-risk two-leg options spreads.
///
/// Four variants via VerticalSpreadType:
///   BullCallSpread  — Buy lower call  + Sell higher call  (debit,  bullish)
///   BearPutSpread   — Buy higher put  + Sell lower put    (debit,  bearish)
///   BullPutSpread   — Sell higher put + Buy lower put     (credit, neutral-bullish)
///   BearCallSpread  — Sell lower call + Buy higher call   (credit, neutral-bearish)
///
/// Entry:  Direction confirmed by trend (SMA20/SMA50 slope) + optional OptionChain bias.
///         Delta-based strike selection (~20–30 delta for credit, ATM for debit).
/// Exit:   At ProfitTargetPct of max profit or on stop breach.
/// </summary>
public class VerticalSpreadStrategy(VerticalSpreadConfig config) : IStrategy
{
    public string Name => "VerticalSpread";

    // Trend SMA needs TrendSmaPeriod candles; +5 gives a stable average before trading.
    public int MinWarmupBars => config.TrendSmaPeriod + 5;

    public Task<SignalResult> EvaluateAsync(StrategyContext context, CancellationToken ct)
    {
        var candles = context.Candles;
        if (candles.Count < config.TrendSmaPeriod + 5)
            return Task.FromResult(SignalResult.Skip(SkippedReason.InsufficientData,
                $"Need {config.TrendSmaPeriod + 5} candles"));

        var current = candles[^1];

        // ── Trend filter ───────────────────────────────────────────────────
        decimal sma = candles.TakeLast(config.TrendSmaPeriod).Average(c => c.Close);
        bool trendUp   = current.Close > sma;
        bool trendDown = current.Close < sma;

        // Validate direction against configured spread type
        var typeOk = config.SpreadType switch
        {
            VerticalSpreadType.BullCallSpread => trendUp,
            VerticalSpreadType.BearPutSpread  => trendDown,
            VerticalSpreadType.BullPutSpread  => trendUp,
            VerticalSpreadType.BearCallSpread => trendDown,
            _ => false,
        };

        if (!typeOk)
            return Task.FromResult(SignalResult.Hold(
                $"{config.SpreadType}: trend not aligned (close={current.Close:F2}, SMA{config.TrendSmaPeriod}={sma:F2})"));

        // ── Optional: OptionChain bias confirmation ────────────────────────
        // Filter to liquid near-ATM strikes so PCR/MaxPain bias is not distorted by far-OTM hedges.
        if (context.OptionChain is not null)
        {
            var filteredChain = context.OptionChain.FilteredAroundAtm(config.StrikeInterval, config.ChainStrikeDepth);
            bool chainBullish = filteredChain.IsBullishBias;
            bool chainBearish = filteredChain.IsBearishBias;
            bool chainAligned = config.SpreadType switch
            {
                VerticalSpreadType.BullCallSpread => !chainBearish,
                VerticalSpreadType.BearPutSpread  => !chainBullish,
                VerticalSpreadType.BullPutSpread  => !chainBearish,
                VerticalSpreadType.BearCallSpread => !chainBullish,
                _ => true,
            };
            if (!chainAligned)
                return Task.FromResult(SignalResult.Hold(
                    $"{config.SpreadType}: option chain bias conflicts with direction"));
        }

        // ── Build legs ─────────────────────────────────────────────────────
        SpreadLeg leg1, leg2;

        switch (config.SpreadType)
        {
            case VerticalSpreadType.BullCallSpread:
                // Debit: buy lower call (ATM or slightly OTM) + sell higher call
                leg1 = new SpreadLeg(OptionType.Call, OrderDirection.Buy,  StrikeSelectionMode.ByDelta,
                                     TargetDelta: config.LongLegDelta,  Quantity: 1);
                leg2 = new SpreadLeg(OptionType.Call, OrderDirection.Sell, StrikeSelectionMode.OtmByStrike,
                                     OtmStrikes: config.SpreadWidthStrikes, Quantity: 1);
                break;

            case VerticalSpreadType.BearPutSpread:
                // Debit: buy higher put (ATM or slightly OTM) + sell lower put
                leg1 = new SpreadLeg(OptionType.Put,  OrderDirection.Buy,  StrikeSelectionMode.ByDelta,
                                     TargetDelta: config.LongLegDelta,  Quantity: 1);
                leg2 = new SpreadLeg(OptionType.Put,  OrderDirection.Sell, StrikeSelectionMode.OtmByStrike,
                                     OtmStrikes: config.SpreadWidthStrikes, Quantity: 1);
                break;

            case VerticalSpreadType.BullPutSpread:
                // Credit: sell higher put + buy lower put
                leg1 = new SpreadLeg(OptionType.Put,  OrderDirection.Sell, StrikeSelectionMode.ByDelta,
                                     TargetDelta: config.ShortLegDelta, Quantity: 1);
                leg2 = new SpreadLeg(OptionType.Put,  OrderDirection.Buy,  StrikeSelectionMode.OtmByStrike,
                                     OtmStrikes: config.SpreadWidthStrikes, Quantity: 1);
                break;

            case VerticalSpreadType.BearCallSpread:
                // Credit: sell lower call + buy higher call
                leg1 = new SpreadLeg(OptionType.Call, OrderDirection.Sell, StrikeSelectionMode.ByDelta,
                                     TargetDelta: config.ShortLegDelta, Quantity: 1);
                leg2 = new SpreadLeg(OptionType.Call, OrderDirection.Buy,  StrikeSelectionMode.OtmByStrike,
                                     OtmStrikes: config.SpreadWidthStrikes, Quantity: 1);
                break;

            default:
                return Task.FromResult(SignalResult.Skip(SkippedReason.FilterFailed,
                    $"Unknown VerticalSpreadType: {config.SpreadType}"));
        }

        var indicators = new Dictionary<string, decimal>
        {
            [$"sma{config.TrendSmaPeriod}"] = sma,
        };

        var spread = new SpreadSignalResult(
            SpreadType:      config.SpreadType.ToString(),
            Legs:            [leg1, leg2],
            Reason:          $"{config.SpreadType}: close={current.Close:F2}, SMA{config.TrendSmaPeriod}={sma:F2}, " +
                             $"width={config.SpreadWidthStrikes} strikes",
            DiagnosticsJson: indicators,
            // VerticalSpread targets equity underlyings — SpotPrice from current candle.
            // NearExpiryDate requires an option chain; use context.OptionChain if available.
            SpotPrice:       context.OptionChain?.SpotPrice ?? current.Close,
            NearExpiryDate:  context.OptionChain?.Expiry);

        return Task.FromResult(SignalResult.SpreadEntry(spread));
    }
}

public enum VerticalSpreadType
{
    BullCallSpread,  // debit, bullish
    BearPutSpread,   // debit, bearish
    BullPutSpread,   // credit, neutral-bullish
    BearCallSpread,  // credit, neutral-bearish
}

public class VerticalSpreadConfig
{
    public VerticalSpreadType SpreadType         { get; set; } = VerticalSpreadType.BullCallSpread;
    public int     TrendSmaPeriod                { get; set; } = 50;
    public decimal LongLegDelta                  { get; set; } = 0.40m;  // ~ATM for debit spreads
    public decimal ShortLegDelta                 { get; set; } = 0.25m;  // OTM for credit spreads
    public int     SpreadWidthStrikes            { get; set; } = 2;
    /// <summary>Number of strikes each side of ATM for bias (PCR/MaxPain) analytics.</summary>
    public int     ChainStrikeDepth              { get; set; } = 5;
    /// <summary>Strike interval (50 = NIFTY, 100 = BANKNIFTY).</summary>
    public decimal StrikeInterval                { get; set; } = 50m;

    public static VerticalSpreadConfig FromJson(string json)
        => JsonSerializer.Deserialize<VerticalSpreadConfig>(json) ?? new();

    public static IReadOnlyList<StrategyParamDef> GetSchema() =>
    [
        new("SpreadType", "Spread Type", "select", "BullCallSpread", Options:
        [
            new("BullCallSpread", "Bull Call Spread (debit, bullish)"),
            new("BearPutSpread",  "Bear Put Spread (debit, bearish)"),
            new("BullPutSpread",  "Bull Put Spread (credit, neutral-bullish)"),
            new("BearCallSpread", "Bear Call Spread (credit, neutral-bearish)"),
        ]),
        new("TrendSmaPeriod",     "Trend SMA Period",       "int",     50,    Min: 10,   Max: 200),
        new("LongLegDelta",       "Long Leg Delta",         "decimal", 0.40m, Min: 0.2m, Max: 0.55m, Step: 0.05m, Hint: "Delta of the long leg (debit spreads)"),
        new("ShortLegDelta",      "Short Leg Delta",        "decimal", 0.25m, Min: 0.1m, Max: 0.45m, Step: 0.05m, Hint: "Delta of the short leg (credit spreads)"),
        new("SpreadWidthStrikes", "Spread Width (Strikes)", "int",     2,     Min: 1,    Max: 10),
        new("ChainStrikeDepth",   "Chain Strike Depth",     "int",     5,     Min: 1,    Max: 20,    Hint: "Strikes each side of ATM for PCR/bias analytics."),
        new("StrikeInterval",     "Strike Interval (pts)",  "decimal", 50m,   Min: 10m,  Max: 500m,  Step: 10m, Hint: "50 for NIFTY, 100 for BANKNIFTY"),
    ];
}
