using NodaTime;

namespace rvs.AlgoTrader.Domain.Events;

public record StreamDisconnected(
    string BrokerName, string Reason, int ReconnectAttempt,
    string CorrelationId, ZonedDateTime OccurredAt);

public record StreamReconnected(
    string BrokerName, int TotalDowntimeSeconds,
    IReadOnlyList<string> ResubscribedSymbols,
    string CorrelationId, ZonedDateTime OccurredAt);
