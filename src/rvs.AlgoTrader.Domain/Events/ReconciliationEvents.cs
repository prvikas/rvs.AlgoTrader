using NodaTime;

namespace rvs.AlgoTrader.Domain.Events;

public record PositionMismatchDetected(
    string BrokerName, string InternalSymbol,
    int LocalQuantity, int BrokerQuantity,
    decimal LocalAvgPrice, decimal BrokerAvgPrice,
    bool AutoSyncEnabled, string CorrelationId, ZonedDateTime OccurredAt);
