using NodaTime;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Domain.Events;

public record OrderPlaced(
    Guid OrderId, string BrokerName, string BrokerOrderId,
    string InternalSymbol, OrderType OrderType, OrderDirection Direction,
    int Quantity, decimal? Price, Guid? StrategyRunId,
    string CorrelationId, ZonedDateTime OccurredAt);

public record OrderFilled(
    Guid OrderId, string BrokerName, string BrokerOrderId,
    string InternalSymbol, OrderDirection Direction,
    int FilledQuantity, decimal FillPrice, bool IsPartialFill,
    Guid? StrategyRunId, string CorrelationId, ZonedDateTime OccurredAt);

public record OrderCancelled(
    Guid OrderId, string BrokerName, string BrokerOrderId,
    string InternalSymbol, string CancelReason,
    Guid? StrategyRunId, string CorrelationId, ZonedDateTime OccurredAt);
