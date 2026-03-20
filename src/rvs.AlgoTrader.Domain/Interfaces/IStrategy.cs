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
}

public record StrategyContext(
    Guid StrategyInstanceId,
    string InternalSymbol,
    string Timeframe,
    IReadOnlyList<ClosedCandle> Candles,   // historical closed candles only — never open bar
    object ConfigJson,
    string CorrelationId
);

public record SignalResult(
    string Signal,                          // BUY, SELL, HOLD
    decimal? EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    string Reason,
    object? DiagnosticsJson,
    string? SkippedReason                   // THROTTLED, MARKET_CLOSED, KILL_SWITCH, RISK_LIMIT, INSUFFICIENT_CAPITAL, TIMEOUT, OUTSIDE_SCHEDULE
)
{
    public static SignalResult Buy(decimal entryPrice, decimal stopLoss, decimal takeProfit, string reason, object? diagnosticsJson = null, ZonedDateTime? candleTimestamp = null)
        => new("BUY", entryPrice, stopLoss, takeProfit, reason, diagnosticsJson, null);

    public static SignalResult Sell(decimal entryPrice, decimal stopLoss, decimal takeProfit, string reason, object? diagnosticsJson = null, ZonedDateTime? candleTimestamp = null)
        => new("SELL", entryPrice, stopLoss, takeProfit, reason, diagnosticsJson, null);

    public static SignalResult Hold(string reason, object? diagnosticsJson = null)
        => new("HOLD", null, null, null, reason, diagnosticsJson, null);

    public static SignalResult Skip(SkippedReason skippedReason, string reason = "Signal skipped")
        => new("HOLD", null, null, null, reason, null, skippedReason.ToString());
}
