using NodaTime;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Domain.Interfaces;

public interface IStrategy
{
    string Name { get; }
    Task<SignalResult> EvaluateAsync(StrategyContext context, CancellationToken ct);
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
    IReadOnlyList<StrategyParamOption>? Options = null);

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
    OptionChainSnapshot? OptionChain = null
);

public record SignalResult(
    string Signal,                          // BUY, SELL, HOLD
    decimal? EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    string Reason,
    object? DiagnosticsJson,
    string? SkippedReason,                  // THROTTLED, MARKET_CLOSED, KILL_SWITCH, RISK_LIMIT, INSUFFICIENT_CAPITAL, TIMEOUT, OUTSIDE_SCHEDULE
    // Indicator snapshot for this bar — used by BacktestEngine to stream chart overlays.
    // Keys are human-readable names: "ema5", "ema9", "ema21", "vwap", "bbUpper", "bbMid", "bbLower", "atr", "rangeHigh", "rangeLow".
    // Return null if indicators are not applicable or not yet warmed up.
    IReadOnlyDictionary<string, decimal>? IndicatorValues = null
)
{
    public static SignalResult Buy(decimal entryPrice, decimal stopLoss, decimal takeProfit, string reason,
        object? diagnosticsJson = null, ZonedDateTime? candleTimestamp = null,
        IReadOnlyDictionary<string, decimal>? indicatorValues = null)
        => new("BUY", entryPrice, stopLoss, takeProfit, reason, diagnosticsJson, null, indicatorValues);

    public static SignalResult Sell(decimal entryPrice, decimal stopLoss, decimal takeProfit, string reason,
        object? diagnosticsJson = null, ZonedDateTime? candleTimestamp = null,
        IReadOnlyDictionary<string, decimal>? indicatorValues = null)
        => new("SELL", entryPrice, stopLoss, takeProfit, reason, diagnosticsJson, null, indicatorValues);

    public static SignalResult Hold(string reason, object? diagnosticsJson = null,
        IReadOnlyDictionary<string, decimal>? indicatorValues = null)
        => new("HOLD", null, null, null, reason, diagnosticsJson, null, indicatorValues);

    public static SignalResult Skip(SkippedReason skippedReason, string reason = "Signal skipped")
        => new("HOLD", null, null, null, reason, null, skippedReason.ToString(), null);
}
