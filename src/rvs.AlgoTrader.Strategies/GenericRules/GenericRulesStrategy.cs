using NodaTime;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using DomainOptionType      = rvs.AlgoTrader.Domain.Enums.OptionType;
using DomainOrderDirection  = rvs.AlgoTrader.Domain.Enums.OrderDirection;
using DomainStrikeMode      = rvs.AlgoTrader.Domain.Enums.StrikeSelectionMode;

namespace rvs.AlgoTrader.Strategies.GenericRules;

/// <summary>
/// GenericRulesStrategy — executes any strategy whose rules were defined in the UI.
///
/// HOW IT WORKS:
///   ParametersJson contains the full Strategy JSON produced by the frontend
///   StrategyDefinitionPage. The JSON defines:
///     • indicators[]   — SMA, EMA, RSI, ATR, BollingerBands, MACD, VWAP, etc.
///     • longEntry      — condition tree for entering a long position
///     • longExit       — condition tree for exiting a long position
///     • shortEntry     — condition tree for entering a short position
///     • shortExit      — condition tree for exiting a short position
///
///   The IndicatorEngine computes all indicator values for each bar.
///   The RulesEvaluator walks the condition tree and returns true/false.
///
/// EXECUTION MODEL:
///   Entry:  fires Buy/Sell → BacktestEngine fills at next bar's open (FillModel.NextBarOpen)
///   Exit:   fires ExitLong/ExitShort → BacktestEngine fills at next bar's open
///   Skip:   Invalidation-layer indicators (e.g. SessionFilter) block signals when they
///           evaluate to 0 (outside allowed window/day).
///
/// SIGNAL-DRIVEN EXITS:
///   Uses SignalType.ExitLong / ExitShort (AP-007 compliant — never partial candle).
///   BacktestEngine re-evaluates this strategy each bar while a position is open,
///   passing CurrentPosition = "LONG" or "SHORT" in StrategyContext.
///
/// STOP LOSS:
///   ATR-based disaster stop only (default 3× ATR). Primary exit is signal-based.
///   Set stopLoss.type = "ATRMultiple" and stopLoss.value in the UI to override.
/// </summary>
public class GenericRulesStrategy(GenericRulesConfig config) : IStrategy
{
    private static readonly DateTimeZone Ist =
        DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    public string Name => "GenericRules";

    public int MinWarmupBars
    {
        get
        {
            // Compute worst-case warmup across all indicators + some extra
            var maxPeriod = config.Indicators
                .Select(ind => ind.Type.ToUpperInvariant() switch
                {
                    "SMA"             => ind.GetInt("period", 20),
                    "EMA"             => ind.GetInt("period", 20),
                    "HULLMA"          => ind.GetInt("period", 20) * 2,
                    "RSI"             => ind.GetInt("period", 14) + 1,
                    "ATR"             => ind.GetInt("period", 14),
                    "BOLLINGERBANDS"  => ind.GetInt("period", 20),
                    "MACD"            => ind.GetInt("slowPeriod", 26) + ind.GetInt("signalPeriod", 9),
                    "CCI"             => ind.GetInt("period", 20),
                    "ADX"             => ind.GetInt("period", 14) * 2,
                    "STOCHASTICS"     => ind.GetInt("kPeriod", 14) + ind.GetInt("dPeriod", 3),
                    "DONCHIANCHANNEL" => ind.GetInt("period", 20),
                    "SUPERTREND"      => ind.GetInt("period", 10),
                    _ => 1
                })
                .DefaultIfEmpty(20)
                .Max();

            return maxPeriod + 10;
        }
    }

    public Task<SignalResult> EvaluateAsync(StrategyContext context, CancellationToken ct)
    {
        var candles = context.Candles;
        if (candles.Count < 2)
            return Task.FromResult(SignalResult.Skip(
                SkippedReason.InsufficientData, "Insufficient candles for GenericRules"));

        var current = candles[^1];

        // ── Invalidation layer: time / session filters ────────────────────────
        var invalidationIndicators = config.Indicators
            .Where(i => i.SignalLayer?.Equals("Invalidation", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        // ── Compute all indicators (pass context for IVRank/PCR/AtmIV/MaxPain) ─
        var computed = IndicatorEngine.ComputeAll(candles, config.Indicators, context);

        // Build indicator snapshot for charting
        var indicatorSnapshot = new Dictionary<string, decimal>();
        foreach (var kv in computed)
        {
            var ind = config.Indicators.FirstOrDefault(i => i.Id == kv.Key);
            var label = ind != null ? $"{ind.Type.ToLower()}{ind.GetInt("period")}" : kv.Key;
            indicatorSnapshot[label] = kv.Value.Current;
            foreach (var field in kv.Value.Fields)
                indicatorSnapshot[$"{label}_{field.Key}"] = field.Value;
        }

        // ── Check Invalidation layer ──────────────────────────────────────────
        foreach (var inv in invalidationIndicators)
        {
            if (computed.TryGetValue(inv.Id, out var invVal) && invVal.Current == 0m)
                return Task.FromResult(SignalResult.Hold(
                    $"Invalidation filter '{inv.Type}' blocked signal (value=0)",
                    indicatorValues: indicatorSnapshot));
        }

        // ── Position-aware evaluation ─────────────────────────────────────────
        var position = context.CurrentPosition;

        if (position == "LONG")
        {
            if (config.LongExit.Enabled &&
                RulesEvaluator.Evaluate(config.LongExit, computed, candles))
                return Task.FromResult(SignalResult.ExitLong("Long exit conditions met", indicatorSnapshot));
            return Task.FromResult(SignalResult.Hold(
                "Long: holding — exit conditions not met", indicatorValues: indicatorSnapshot));
        }

        if (position == "SHORT")
        {
            if (config.ShortExit.Enabled &&
                RulesEvaluator.Evaluate(config.ShortExit, computed, candles))
                return Task.FromResult(SignalResult.ExitShort("Short exit conditions met", indicatorSnapshot));
            return Task.FromResult(SignalResult.Hold(
                "Short: holding — exit conditions not met", indicatorValues: indicatorSnapshot));
        }

        // ── No position: evaluate entry ───────────────────────────────────────

        var opts = config.OptionsConfig;

        if (opts?.Enabled == true)
        {
            // Options mode: check IV/PCR filters, then fire a SpreadEntry signal
            var filterBlock = CheckOptionsFilters(opts, context);
            if (filterBlock != null)
                return Task.FromResult(SignalResult.Hold(filterBlock, indicatorValues: indicatorSnapshot));

            // Use longEntry conditions for direction — if longEntry fires it's a bullish spread,
            // shortEntry fires it's a bearish spread (or the spread type itself is direction-neutral).
            var longFires  = config.LongEntry.Enabled  && RulesEvaluator.Evaluate(config.LongEntry,  computed, candles);
            var shortFires = config.ShortEntry.Enabled && RulesEvaluator.Evaluate(config.ShortEntry, computed, candles);

            if (!longFires && !shortFires)
                return Task.FromResult(SignalResult.Hold(
                    "Options: no entry conditions met", indicatorValues: indicatorSnapshot));

            var legs = BuildSpreadLegs(opts);
            var spotPrice = context.OptionChain?.SpotPrice ?? current.Close;
            var expiryDate = context.OptionChain?.Expiry;

            var spread = new SpreadSignalResult(
                SpreadType:        opts.SpreadType,
                Legs:              legs,
                Reason:            $"GenericRules options entry: {opts.SpreadType} ({(longFires ? "long" : "short")} entry)",
                DiagnosticsJson:   indicatorSnapshot,
                SpotPrice:         spotPrice,
                NearExpiryDate:    expiryDate
            );

            return Task.FromResult(SignalResult.SpreadEntry(spread));
        }

        // ── Equity mode: plain Buy / Sell ─────────────────────────────────────

        var atrNow = GetAtrValue(computed) ?? ComputeFallbackAtr(candles);

        if (config.LongEntry.Enabled &&
            RulesEvaluator.Evaluate(config.LongEntry, computed, candles))
        {
            //var sl   = current.Close - atrNow * config.AtrStopMult;
            // Signal at current.Close but engine fills at next open — use a price-agnostic ATR stop distance
            // The engine will anchor the real SL from the fill price in the next bar
            // Option: leave entryPrice = current.Close but note the discrepancy is minor for liquid stocks
            // Better fix: compute SL as a distance, not an absolute price, and let the engine apply it
            var slDistance = atrNow * config.AtrStopMult;
            var sl = current.Close - slDistance;   // acceptable approximation for non-gapping instruments
            var risk = current.Close - sl;
            var tp   = current.Close + risk * config.RiskRewardRatio;

            return Task.FromResult(SignalResult.Buy(
                entryPrice: current.Close,
                stopLoss:   Math.Max(sl, current.Low * 0.99m),
                takeProfit: tp,
                reason:     "Long entry conditions met",
                indicatorValues: indicatorSnapshot));
        }

        if (config.AllowShort &&
            config.ShortEntry.Enabled &&
            RulesEvaluator.Evaluate(config.ShortEntry, computed, candles))
        {
            var sl   = current.Close + atrNow * config.AtrStopMult;
            var risk = sl - current.Close;
            var tp   = current.Close - risk * config.RiskRewardRatio;

            return Task.FromResult(SignalResult.Sell(
                entryPrice: current.Close,
                stopLoss:   Math.Min(sl, current.High * 1.01m),
                takeProfit: tp,
                reason:     "Short entry conditions met",
                indicatorValues: indicatorSnapshot));
        }

        return Task.FromResult(SignalResult.Hold(
            "No entry conditions met", indicatorValues: indicatorSnapshot));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Find the first ATR indicator in the computed set, or return null.
    /// Allows the user to include an ATR indicator for the stop calculation.
    /// </summary>
    private decimal? GetAtrValue(Dictionary<string, ComputedIndicator> computed)
    {
        var atrDef = config.Indicators.FirstOrDefault(i =>
            i.Type.Equals("ATR", StringComparison.OrdinalIgnoreCase));
        if (atrDef == null) return null;
        return computed.TryGetValue(atrDef.Id, out var v) ? v.Current : null;
    }

    /// <summary>
    /// Fallback ATR (14-period) when no ATR indicator was defined in the strategy.
    /// Used only for stop-loss placement; strategy exits are signal-driven.
    /// </summary>
    private static decimal ComputeFallbackAtr(IReadOnlyList<ClosedCandle> candles)
    {
        var atr = IndicatorEngine.ComputeAtr(candles, Math.Min(14, candles.Count - 1));
        return atr.Length > 0 ? atr[^1] : candles[^1].Close * 0.02m; // 2% fallback
    }

    // ── Options helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Validates IV / PCR numeric filter bounds from OptionsConfig.
    /// Returns a hold reason string if any filter fails; null if all pass.
    /// </summary>
    private static string? CheckOptionsFilters(OptionsConfig opts, StrategyContext ctx)
    {
        var ivRank = ctx.SymbolIvRank?.IvRank;
        var atmIv  = ctx.OptionChain?.AtmIv;
        var pcr    = ctx.OptionChain?.PutCallRatioOI;

        if (opts.MinIvRank.HasValue && (ivRank == null || ivRank < opts.MinIvRank))
            return $"Options: IVRank {ivRank:F1} below minimum {opts.MinIvRank}";
        if (opts.MaxIvRank.HasValue && ivRank != null && ivRank > opts.MaxIvRank)
            return $"Options: IVRank {ivRank:F1} above maximum {opts.MaxIvRank}";
        if (opts.MinAtmIv.HasValue && (atmIv == null || atmIv < opts.MinAtmIv))
            return $"Options: ATM IV {atmIv:F1} below minimum {opts.MinAtmIv}";
        if (opts.MaxAtmIv.HasValue && atmIv != null && atmIv > opts.MaxAtmIv)
            return $"Options: ATM IV {atmIv:F1} above maximum {opts.MaxAtmIv}";
        if (opts.MinPcr.HasValue && (pcr == null || pcr < opts.MinPcr))
            return $"Options: PCR {pcr:F2} below minimum {opts.MinPcr}";
        if (opts.MaxPcr.HasValue && pcr != null && pcr > opts.MaxPcr)
            return $"Options: PCR {pcr:F2} above maximum {opts.MaxPcr}";

        return null;
    }

    /// <summary>
    /// Builds the SpreadLeg list. Uses custom legs from OptionsConfig.Legs when provided;
    /// otherwise falls back to built-in presets for each spread type.
    /// </summary>
    private static IReadOnlyList<SpreadLeg> BuildSpreadLegs(OptionsConfig opts)
    {
        if (opts.Legs.Length > 0)
            return opts.Legs.Select(MapLeg).ToList();

        bool weekly = opts.ExpiryPreference.Equals("Weekly", StringComparison.OrdinalIgnoreCase);

        return opts.SpreadType switch
        {
            "BullCallSpread"  => [
                new SpreadLeg(DomainOptionType.Call, DomainOrderDirection.Buy,  DomainStrikeMode.Atm,         NearestWeekly: weekly, Quantity: 1),
                new SpreadLeg(DomainOptionType.Call, DomainOrderDirection.Sell, DomainStrikeMode.OtmByStrike, OtmStrikes: 2, NearestWeekly: weekly, Quantity: 1),
            ],
            "BearPutSpread"   => [
                new SpreadLeg(DomainOptionType.Put, DomainOrderDirection.Buy,  DomainStrikeMode.Atm,         NearestWeekly: weekly, Quantity: 1),
                new SpreadLeg(DomainOptionType.Put, DomainOrderDirection.Sell, DomainStrikeMode.OtmByStrike, OtmStrikes: 2, NearestWeekly: weekly, Quantity: 1),
            ],
            "BullPutSpread"   => [
                new SpreadLeg(DomainOptionType.Put, DomainOrderDirection.Sell, DomainStrikeMode.Atm,         NearestWeekly: weekly, Quantity: 1),
                new SpreadLeg(DomainOptionType.Put, DomainOrderDirection.Buy,  DomainStrikeMode.OtmByStrike, OtmStrikes: 2, NearestWeekly: weekly, Quantity: 1),
            ],
            "BearCallSpread"  => [
                new SpreadLeg(DomainOptionType.Call, DomainOrderDirection.Sell, DomainStrikeMode.Atm,         NearestWeekly: weekly, Quantity: 1),
                new SpreadLeg(DomainOptionType.Call, DomainOrderDirection.Buy,  DomainStrikeMode.OtmByStrike, OtmStrikes: 2, NearestWeekly: weekly, Quantity: 1),
            ],
            "IronCondor"      => [
                new SpreadLeg(DomainOptionType.Call, DomainOrderDirection.Sell, DomainStrikeMode.OtmByStrike, OtmStrikes: 2, NearestWeekly: weekly, Quantity: 1),
                new SpreadLeg(DomainOptionType.Call, DomainOrderDirection.Buy,  DomainStrikeMode.OtmByStrike, OtmStrikes: 4, NearestWeekly: weekly, Quantity: 1),
                new SpreadLeg(DomainOptionType.Put,  DomainOrderDirection.Sell, DomainStrikeMode.OtmByStrike, OtmStrikes: 2, NearestWeekly: weekly, Quantity: 1),
                new SpreadLeg(DomainOptionType.Put,  DomainOrderDirection.Buy,  DomainStrikeMode.OtmByStrike, OtmStrikes: 4, NearestWeekly: weekly, Quantity: 1),
            ],
            "ShortStraddle"   => [
                new SpreadLeg(DomainOptionType.Call, DomainOrderDirection.Sell, DomainStrikeMode.Atm, NearestWeekly: weekly, Quantity: 1),
                new SpreadLeg(DomainOptionType.Put,  DomainOrderDirection.Sell, DomainStrikeMode.Atm, NearestWeekly: weekly, Quantity: 1),
            ],
            "ShortStrangle"   => [
                new SpreadLeg(DomainOptionType.Call, DomainOrderDirection.Sell, DomainStrikeMode.OtmByStrike, OtmStrikes: 2, NearestWeekly: weekly, Quantity: 1),
                new SpreadLeg(DomainOptionType.Put,  DomainOrderDirection.Sell, DomainStrikeMode.OtmByStrike, OtmStrikes: 2, NearestWeekly: weekly, Quantity: 1),
            ],
            "CalendarSpread"  => [
                new SpreadLeg(DomainOptionType.Call, DomainOrderDirection.Sell, DomainStrikeMode.Atm, NearestWeekly: true,  Quantity: 1),
                new SpreadLeg(DomainOptionType.Call, DomainOrderDirection.Buy,  DomainStrikeMode.Atm, NearestWeekly: false, Quantity: 1),
            ],
            "LongCall"        => [
                new SpreadLeg(DomainOptionType.Call, DomainOrderDirection.Buy, DomainStrikeMode.Atm, NearestWeekly: weekly, Quantity: 1),
            ],
            "LongPut"         => [
                new SpreadLeg(DomainOptionType.Put, DomainOrderDirection.Buy, DomainStrikeMode.Atm, NearestWeekly: weekly, Quantity: 1),
            ],
            _ => [
                new SpreadLeg(DomainOptionType.Call, DomainOrderDirection.Buy, DomainStrikeMode.Atm, NearestWeekly: weekly, Quantity: 1),
            ],
        };
    }

    private static SpreadLeg MapLeg(OptionsSpreadLegDef d) => new(
        OptionType:    d.OptionType.Equals("PE", StringComparison.OrdinalIgnoreCase)
                           ? DomainOptionType.Put : DomainOptionType.Call,
        Direction:     d.Direction.Equals("Sell", StringComparison.OrdinalIgnoreCase)
                           ? DomainOrderDirection.Sell : DomainOrderDirection.Buy,
        SelectionMode: d.SelectionMode.ToUpperInvariant() switch
        {
            "OTMBYSTRIKE" => DomainStrikeMode.OtmByStrike,
            "OTMBYPCT"    => DomainStrikeMode.OtmByPct,
            "BYDELTA"     => DomainStrikeMode.ByDelta,
            _             => DomainStrikeMode.Atm,
        },
        OtmStrikes:    d.OtmStrikes > 0 ? d.OtmStrikes : null,
        OtmPct:        d.OtmPct    > 0 ? d.OtmPct     : null,
        TargetDelta:   d.TargetDelta > 0 ? d.TargetDelta : null,
        NearestWeekly: d.NearestWeekly,
        Quantity:      Math.Max(1, d.Quantity)
    );
}
