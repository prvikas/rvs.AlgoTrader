using NodaTime;

namespace rvs.AlgoTrader.Domain.Events;

public record AlertTriggered(
    Guid AlertRuleId, string AlertType, string Severity, string Message,
    string[] Channels, string CorrelationId, ZonedDateTime OccurredAt);

public record MonitoringAlertTriggered(
    Guid AlertRuleId, string MetricName, double MetricValue, double ThresholdValue,
    string Operator, string Severity, string Message,
    string CorrelationId, ZonedDateTime OccurredAt);
