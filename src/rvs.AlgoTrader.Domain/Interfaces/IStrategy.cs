using NodaTime;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Domain.Interfaces;

public interface IStrategy
{
    string Name { get; }
    Task<SignalResult> EvaluateAsync(StrategyContext context, CancellationToken ct);

    /// <summary>
    /// Minimum bars required before the strategy can produce reliable signals.
    /// BacktestEngine uses Max(request.WarmupBars, strategy.MinWarmupBars) so strategies
    /// with long lookbacks (e.g. SMA-200) are never under-warmed.
    /// </summary>
    int MinWarmupBars => 50;
}

public interface IStrategyFactory
{
    IStrategy Create(string strategyName, string? parametersJson);
    IEnumerable<string> GetRegisteredNames();
    IReadOnlyList<StrategyParamDef> GetParameterSchema(string strategyName);
}

/// <summary>
/// Describes one configurable parameter for a strategy.
/// The single source of truth — defined in each strategy's Config class via GetSchema().
/// Consumed by the frontend to render a dynamic parameter editor without any hardcoded metadata.
/// </summary>
public record StrategyParamDef(
    string Key,
    string Label,
    string Type,          // "int" | "decimal" | "bool" | "select"
    object? Default,
    decimal? Min = null,
    decimal? Max = null,
    decimal? Step = null,
    string? Hint = null,
    IReadOnlyList<StrategyParamOption>? Options = null,
    string? Section = null,
    string? EnabledBy = null);

public record StrategyParamOption(string Value, string Label);

public record StrategyContext(
    Guid StrategyInstanceId,
    string InternalSymbol,
    string Timeframe,
    IReadOnlyList<ClosedCandle> Candles,           // historical closed candles only — never open bar
    object ConfigJson,
    string CorrelationId,

    // Pre-fetched option chain snapshot for the underlying instrument.
    // Null when the strategy does not require option chain analysis, or when the instrument
    // is not a derivative (equity cash segment strategies won't have this).
    //
    // Use this for: PCR-based sentiment filter, max-pain level, OI support/resistance.
    // IOptionChainService is called by StrategyEvaluationQueue BEFORE building this context,
    // so strategies can read OptionChain directly without any I/O inside EvaluateAsync (Rule #18).
    OptionChainSnapshot? OptionChain = null,

    // ── Multi-timeframe candle arrays (#94) ──────────────────────────────────
    // Higher-TF candles pre-fetched by StrategyEvaluationQueue before EvaluateAsync.
    // Null when Timeframe is already >= that granularity (e.g. Candles15Min is null for "15m"/"1d").
    // Strategies use these for higher-TF trend confirmation only — no I/O inside EvaluateAsync.
    IReadOnlyList<ClosedCandle>? Candles15Min = null,
    IReadOnlyList<ClosedCandle>? Candles1Hour = null,
    IReadOnlyList<ClosedCandle>? CandlesDaily = null,

    // ── Pre-fetched market-context data for strategy filters ─────────────────
    // Populated by StrategyEvaluationQueue; null when data not yet available.

    // % of NSE equities above 200-day SMA (latest EOD snapshot).
    // VCP strategy uses this to avoid entries in bear-market regimes.
    // Null when breadth data has not yet been computed.
    decimal? BreadthPct200Sma = null,

    // IV Percentile (0-100) for the underlying symbol vs 52-week IV history.
    // STRAT-002 uses this to ensure elevated IV premium before entering spreads.
    // Null when fewer than 60 days of IV history are available (warmup requirement).
    IvRankSnapshot? SymbolIvRank = null,

    // True when EventCalendarService detects a high-impact event within the
    // configured exclusion window. STRAT-002 skips entry when true.
    bool HasUpcomingEvent = false,

    // CS-1: Dual-expiry option chains for CalendarSpread.
    // Near = weekly expiry (sell leg). Far = monthly expiry (buy leg).
    // Populated by StrategyEvaluationQueue and ForwardTestEngine for CalendarSpread strategies.
    // Null for all other strategies or when chain fetch fails.
    OptionChainSnapshot? NearExpiryChain = null,
    OptionChainSnapshot? FarExpiryChain  = null,

    // Current open position held by the strategy. Populated by BacktestEngine when it
    // re-evaluates the strategy during an open trade to check for strategy-driven exits.
    // "LONG", "SHORT", or null (no position / not applicable).
    // Strategies that use signal-based exits (e.g. SMA crossover) read this to decide
    // whether to check entry conditions or exit conditions on the current bar.
    string? CurrentPosition = null
);

/// <summary>
/// One leg of a multi-leg spread signal.
/// Each leg resolves to a concrete NFO instrument via IOptionLegSelector before order placement.
/// </summary>
public record SpreadLeg(
    Domain.Enums.OptionType     OptionType,
    Domain.Enums.OrderDirection Direction,      // Buy or Sell
    Domain.Enums.StrikeSelectionMode SelectionMode,
    decimal?                    OtmPct        = null,
    int?                        OtmStrikes    = null,
    decimal?                    FixedStrike   = null,
    decimal?                    TargetDelta   = null,
    bool                        NearestWeekly = true,
    int                         Quantity      = 1,    // in lots
    // IC-2/VS-1: When set, OtmByStrike counts from this resolved strike rather than ATM.
    // SpreadOrderManager passes the short leg's actual filled strike as FromStrike for the wing leg.
    decimal?                    FromStrike    = null
);

/// <summary>
/// Signal for a multi-leg spread strategy (Iron Condor, Vertical, Straddle, etc.).
/// Returned by IStrategy.EvaluateAsync as SpreadSignalResult when all legs should be sent.
/// ISpreadOrderManager places all legs atomically (best-effort — no true atomic order exists
/// across broker APIs; partial fill handling is implemented via rollback cancellations).
/// </summary>
public record SpreadSignalResult(
    string                      SpreadType,       // "IronCondor", "Vertical", "Straddle", etc.
    IReadOnlyList<SpreadLeg>    Legs,
    string                      Reason,
    object?                     DiagnosticsJson   = null,
    // FIB-2: Optional underlying price level at which the spread should be force-closed.
    // Set to the Fib 0.786 level by FibOptionSpreadStrategy (the "stop" for underlying price).
    // SpreadOrderManager polls the underlying price; if it breaches this level the spread is closed
    // regardless of option P&L. Null = no underlying stop (relies on premium stop-loss only).
    decimal?                    UnderlyingStopLevel = null,
    // SEQ-1: Underlying spot price at signal time — required by SpreadOrderManager for strike
    // resolution via IOptionLegSelector.ResolveAsync. Set by every option strategy from
    // context.OptionChain.SpotPrice. Null for equity-based spreads (e.g. VerticalSpread on equity).
    decimal?                    SpotPrice = null,
    // SEQ-1: Nearest (short-leg) expiry date — required by SpreadOrderManager to select the
    // correct option contract. For CalendarSpread this is the near (weekly) expiry.
    // Null when the strategy cannot determine expiry (degrade to SpreadOrderManager's own lookup).
    LocalDate?                  NearExpiryDate = null
);

/// <summary>
/// Specifies how to select a concrete option instrument from a signal.
/// Returned by option strategies in SignalResult.OptionsLeg.
/// IOptionLegSelector resolves this to a specific NFO symbol + strike before order placement.
/// </summary>
public record OptionsLegSpec(
    Domain.Enums.OptionType OptionType,               // CE or PE
    Domain.Enums.StrikeSelectionMode SelectionMode,
    decimal? OtmPct           = null,   // OtmByPct: percent OTM, e.g. 0.02 = 2%
    int? OtmStrikes           = null,   // OtmByStrike: number of strike intervals OTM
    decimal? FixedStrike      = null,   // FixedStrike: exact strike
    decimal? TargetDelta      = null,   // ByDelta: e.g. 0.30 for 30-delta
    bool NearestWeeklyExpiry  = true,   // false = nearest monthly
    // IC-2/VS-1: Anchor for OtmByStrike — use short leg's resolved strike, not ATM.
    decimal? FromStrike       = null
);

public record SignalResult(
    SignalType Signal,
    decimal? EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    string Reason,
    object? DiagnosticsJson,
    string? SkippedReason,
    // Indicator snapshot for this bar — used by BacktestEngine to stream chart overlays.
    // Keys are human-readable names: "ema5", "ema9", "ema21", "vwap", "bbUpper", "bbMid", "bbLower", "atr", "rangeHigh", "rangeLow".
    // Return null if indicators are not applicable or not yet warmed up.
    IReadOnlyDictionary<string, decimal>? IndicatorValues = null,
    // Optional: populated by options strategies. IOptionLegSelector converts this to a
    // concrete NFO symbol + BrokerToken before the signal reaches LiveExecutionEngine.
    OptionsLegSpec? OptionsLeg = null,
    // Optional: populated by multi-leg spread strategies (Iron Condor, Vertical, Straddle, etc.).
    // When non-null, ISpreadOrderManager handles atomic leg placement instead of LiveExecutionEngine.
    SpreadSignalResult? Spread = null
)
{
    public static SignalResult Buy(decimal entryPrice, decimal stopLoss, decimal takeProfit, string reason,
        object? diagnosticsJson = null, ZonedDateTime? candleTimestamp = null,
        IReadOnlyDictionary<string, decimal>? indicatorValues = null)
        => new(SignalType.Buy, entryPrice, stopLoss, takeProfit, reason, diagnosticsJson, null, indicatorValues);

    public static SignalResult Sell(decimal entryPrice, decimal stopLoss, decimal takeProfit, string reason,
        object? diagnosticsJson = null, ZonedDateTime? candleTimestamp = null,
        IReadOnlyDictionary<string, decimal>? indicatorValues = null)
        => new(SignalType.Sell, entryPrice, stopLoss, takeProfit, reason, diagnosticsJson, null, indicatorValues);

    public static SignalResult Hold(string reason, object? diagnosticsJson = null,
        IReadOnlyDictionary<string, decimal>? indicatorValues = null)
        => new(SignalType.Hold, null, null, null, reason, diagnosticsJson, null, indicatorValues);

    public static SignalResult Skip(SkippedReason skippedReason, string reason = "Signal skipped")
        => new(SignalType.Hold, null, null, null, reason, null, skippedReason.ToString(), null);

    /// <summary>
    /// Signals that the strategy wants to close an existing LONG position.
    /// BacktestEngine executes at the OPEN of the next candle (next-candle-open fill model).
    /// Ignored if there is no open long position.
    /// </summary>
    public static SignalResult ExitLong(string reason, IReadOnlyDictionary<string, decimal>? indicatorValues = null)
        => new(SignalType.ExitLong, null, null, null, reason, null, null, indicatorValues);

    /// <summary>
    /// Signals that the strategy wants to close an existing SHORT position.
    /// BacktestEngine executes at the OPEN of the next candle (next-candle-open fill model).
    /// Ignored if there is no open short position.
    /// </summary>
    public static SignalResult ExitShort(string reason, IReadOnlyDictionary<string, decimal>? indicatorValues = null)
        => new(SignalType.ExitShort, null, null, null, reason, null, null, indicatorValues);

    /// <summary>Entry signal for a multi-leg spread. ISpreadOrderManager places all legs atomically.</summary>
    public static SignalResult SpreadEntry(SpreadSignalResult spread)
        => new(SignalType.Buy, null, null, null, spread.Reason, spread.DiagnosticsJson, null, null, null, spread);
}
