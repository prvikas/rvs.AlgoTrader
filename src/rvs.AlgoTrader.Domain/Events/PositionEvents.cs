using NodaTime;

namespace rvs.AlgoTrader.Domain.Events;

public record PositionOpened(
    Guid PositionId, string BrokerName, string InternalSymbol,
    int Quantity, decimal EntryPrice, decimal? StopLoss, decimal? TakeProfit,
    Guid? StrategyRunId, string CorrelationId, ZonedDateTime OccurredAt);

public record PositionClosed(
    Guid PositionId, string BrokerName, string InternalSymbol,
    int Quantity, decimal EntryPrice, decimal ExitPrice, decimal RealizedPnl,
    string CloseReason, Guid? StrategyRunId, string CorrelationId, ZonedDateTime OccurredAt);
