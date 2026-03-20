namespace rvs.AlgoTrader.Application.DTOs.Strategy;

public record SignalJournalEntryDto(
    long Id, Guid StrategyInstanceId, string InternalSymbol,
    DateTimeOffset EvaluatedAt, string Timeframe, string Signal,
    decimal? EntryPrice, decimal? StopLoss, decimal? TakeProfit,
    string? Reason, object? DiagnosticsJson, bool ActedOn, string? SkippedReason);
