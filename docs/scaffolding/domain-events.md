# Scaffolding: Domain Events — rvs.AlgoTrader

All domain events are C# records in `rvs.AlgoTrader.Domain.Events`.
Published via **MassTransit 8+ on RabbitMQ**. For critical paths (kill-switch, order fills)
when RabbitMQ is unavailable, fall back to in-process synchronous dispatch.

**Rule:** Domain events are immutable records. No setters, no mutable state.

---

## Namespace & Base Convention

```csharp
namespace rvs.AlgoTrader.Domain.Events;

// All events carry a CorrelationId for end-to-end tracing.
// Timestamp uses NodaTime ZonedDateTime — never DateTime or DateTimeOffset.
```

---

## Order Events

```csharp
namespace rvs.AlgoTrader.Domain.Events;

/// <summary>
/// Published when an order request is successfully submitted to the broker.
/// Does NOT mean the order is filled — only that it was accepted by the broker API.
/// </summary>
public record OrderPlaced(
    Guid OrderId,
    string BrokerName,
    string BrokerOrderId,
    string InternalSymbol,
    string OrderType,          // MARKET, LIMIT, SL, SL-M
    string Direction,          // BUY, SELL
    int Quantity,
    decimal? Price,
    Guid? StrategyRunId,
    string CorrelationId,
    ZonedDateTime OccurredAt
);

/// <summary>
/// Published when the broker confirms an order fill (partial or complete).
/// </summary>
public record OrderFilled(
    Guid OrderId,
    string BrokerName,
    string BrokerOrderId,
    string InternalSymbol,
    string Direction,          // BUY, SELL
    int FilledQuantity,
    decimal FillPrice,
    bool IsPartialFill,
    Guid? StrategyRunId,
    string CorrelationId,
    ZonedDateTime OccurredAt
);

/// <summary>
/// Published when an order is cancelled (by user, system, or broker rejection).
/// </summary>
public record OrderCancelled(
    Guid OrderId,
    string BrokerName,
    string BrokerOrderId,
    string InternalSymbol,
    string CancelReason,       // USER_REQUESTED, BROKER_REJECTED, KILL_SWITCH, SESSION_END
    Guid? StrategyRunId,
    string CorrelationId,
    ZonedDateTime OccurredAt
);
```

---

## Position Events

```csharp
namespace rvs.AlgoTrader.Domain.Events;

/// <summary>
/// Published when a new position is opened (first fill for a new position).
/// </summary>
public record PositionOpened(
    Guid PositionId,
    string BrokerName,
    string InternalSymbol,
    int Quantity,
    decimal EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    Guid? StrategyRunId,
    string CorrelationId,
    ZonedDateTime OccurredAt
);

/// <summary>
/// Published when a position is fully closed (all quantity exited).
/// PnL is the realized P&L after all transaction costs.
/// </summary>
public record PositionClosed(
    Guid PositionId,
    string BrokerName,
    string InternalSymbol,
    int Quantity,
    decimal EntryPrice,
    decimal ExitPrice,
    decimal RealizedPnl,       // net of brokerage, STT, GST, SEBI, stamp duty, slippage
    string CloseReason,        // TAKE_PROFIT, STOP_LOSS, TRAILING_SL, SESSION_END,
                               // KILL_SWITCH, MANUAL, RISK_LIMIT
    Guid? StrategyRunId,
    string CorrelationId,
    ZonedDateTime OccurredAt
);
```

---

## Strategy Signal Events

```csharp
namespace rvs.AlgoTrader.Domain.Events;

/// <summary>
/// Published for every strategy evaluation result that produces an actionable signal (BUY or SELL).
/// HOLD and SKIPPED signals are written to signal_journal but do NOT publish this event.
/// </summary>
public record SignalGenerated(
    Guid StrategyInstanceId,
    string StrategyName,
    string InternalSymbol,
    string Timeframe,
    string Signal,             // BUY, SELL
    decimal? EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    string Reason,
    string CorrelationId,
    ZonedDateTime OccurredAt
);
```

---

## Alert Events

```csharp
namespace rvs.AlgoTrader.Domain.Events;

/// <summary>
/// Published when IMonitoringAlertEvaluator fires an alert rule.
/// AlertType maps to monitoring_alert_rules.alert_type.
/// Channels: TELEGRAM, EMAIL, SIGNALR — delivered by AlertNotificationService consumer.
/// </summary>
public record AlertTriggered(
    Guid AlertRuleId,
    string AlertType,
    string Severity,           // INFO, WARN, CRITICAL
    string Message,
    string[] Channels,         // TELEGRAM, EMAIL, SIGNALR
    string CorrelationId,
    ZonedDateTime OccurredAt
);

/// <summary>
/// Published by IMonitoringAlertEvaluator when a monitoring threshold is breached.
/// Carries full metric context for downstream consumers.
/// </summary>
public record MonitoringAlertTriggered(
    Guid AlertRuleId,
    string MetricName,
    double MetricValue,
    double ThresholdValue,
    string Operator,           // GT, LT, GTE, LTE
    string Severity,           // INFO, WARN, CRITICAL
    string Message,
    string CorrelationId,
    ZonedDateTime OccurredAt
);
```

---

## Market Data Events

```csharp
namespace rvs.AlgoTrader.Domain.Events;

/// <summary>
/// Published by CandleAggregatorService when a candle bar closes.
/// Triggers StrategyEvaluationQueue for all strategy instances watching this symbol+timeframe.
/// ONLY closed candles are published — partial/open bars are NEVER published.
/// </summary>
public record CandleClosedEvent(
    string InternalSymbol,
    string Timeframe,
    ClosedCandle ClosedCandle,
    string CorrelationId,
    ZonedDateTime OccurredAt
);

// ClosedCandle is a value object defined in rvs.AlgoTrader.Domain
// public record ClosedCandle(
//     string InternalSymbol, string Timeframe,
//     ZonedDateTime OpenTime, ZonedDateTime CloseTime,
//     decimal Open, decimal High, decimal Low, decimal Close, long Volume
// );
```

---

## Stream Events

```csharp
namespace rvs.AlgoTrader.Domain.Events;

/// <summary>
/// Published by ReconnectingBrokerStreamClient when a WebSocket disconnects.
/// Triggers red badge in UI and pauses new signal generation per failure_behavior_json.
/// </summary>
public record StreamDisconnected(
    string BrokerName,
    string Reason,             // CONNECTION_LOST, AUTH_EXPIRED, RATE_LIMITED
    int ReconnectAttempt,
    string CorrelationId,
    ZonedDateTime OccurredAt
);

/// <summary>
/// Published by ReconnectingBrokerStreamClient after successful reconnect and re-subscribe.
/// Clears red badge in UI; resumes signal generation.
/// </summary>
public record StreamReconnected(
    string BrokerName,
    int TotalDowntimeSeconds,
    IReadOnlyList<string> ResubscribedSymbols,
    string CorrelationId,
    ZonedDateTime OccurredAt
);
```

---

## Reconciliation Events

```csharp
namespace rvs.AlgoTrader.Domain.Events;

/// <summary>
/// Published by position reconciliation Hangfire job when broker positions
/// do not match local positions table. Triggers WARN log + alert + optional auto-sync.
/// </summary>
public record PositionMismatchDetected(
    string BrokerName,
    string InternalSymbol,
    int LocalQuantity,
    int BrokerQuantity,
    decimal LocalAvgPrice,
    decimal BrokerAvgPrice,
    bool AutoSyncEnabled,
    string CorrelationId,
    ZonedDateTime OccurredAt
);
```

---

## Scheduling Events

```csharp
namespace rvs.AlgoTrader.Domain.Events;

/// <summary>
/// Published by IStartupOrchestrator Step 6 when an instance was RUNNING at shutdown
/// but auto_resume_on_restart = false, so it is restored to PAUSED status.
/// Consumed by StrategyHub to push ColdRestartPauseNotification to connected React clients.
/// React Dashboard renders <ColdRestartNoticeBanner /> listing all paused instances.
/// </summary>
public record ColdRestartPausedEvent(
    Guid StrategyInstanceId,
    string StrategyName,
    string PauseReason,        // "auto_resume_on_restart=false — manual restart required"
    ZonedDateTime ShutdownAt,  // when the system was last shut down (from audit_log)
    string CorrelationId,
    ZonedDateTime OccurredAt
);

/// <summary>
/// Published by IStartupOrchestrator Step 6 when an instance is auto-resumed
/// within its scheduled session window on cold restart.
/// auto_resume_on_restart must be true and instance must have been RUNNING at shutdown.
/// </summary>
public record StrategyAutoResumed(
    Guid StrategyInstanceId,
    string StrategyName,
    string ResumeReason,       // "Within scheduled session on cold restart"
    ZonedDateTime SessionStart,
    ZonedDateTime SessionStop,
    string CorrelationId,
    ZonedDateTime OccurredAt
);

/// <summary>
/// Published when a strategy instance misses its scheduled session window entirely.
/// Triggered when: restart occurs after session_start AND missed_session_behavior = SKIP.
/// Instance is NOT started; next eligible session day is scheduled.
/// </summary>
public record StrategyMissedSessionWindow(
    Guid StrategyInstanceId,
    string StrategyName,
    ZonedDateTime MissedSessionStart,
    ZonedDateTime MissedSessionStop,
    string MissedReason,       // "Cold restart after session start with SKIP behavior"
    string CorrelationId,
    ZonedDateTime OccurredAt
);
```

---

## MassTransit Consumer Registration Pattern

```csharp
// Program.cs / DI registration
builder.Services.AddMassTransit(x =>
{
    // Register all domain event consumers
    x.AddConsumer<OrderPlacedConsumer>();
    x.AddConsumer<OrderFilledConsumer>();
    x.AddConsumer<OrderCancelledConsumer>();
    x.AddConsumer<PositionOpenedConsumer>();
    x.AddConsumer<PositionClosedConsumer>();
    x.AddConsumer<SignalGeneratedConsumer>();
    x.AddConsumer<AlertTriggeredConsumer>();
    x.AddConsumer<MonitoringAlertTriggeredConsumer>();
    x.AddConsumer<CandleClosedEventConsumer>();
    x.AddConsumer<StreamDisconnectedConsumer>();
    x.AddConsumer<StreamReconnectedConsumer>();
    x.AddConsumer<PositionMismatchDetectedConsumer>();
    x.AddConsumer<StrategyAutoResumedConsumer>();
    x.AddConsumer<StrategyMissedSessionWindowConsumer>();
    x.AddConsumer<ColdRestartPausedEventConsumer>(); // → StrategyHub push + AuditLogConsumer

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host("rabbitmq", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });
        cfg.ConfigureEndpoints(ctx);
    });
});
```

---

## Complete Domain Event Inventory

| Event | Publisher | Consumer(s) |
|---|---|---|
| `OrderPlaced` | `LiveExecutionEngine` | `AuditLogConsumer`, `SignalRHubConsumer` |
| `OrderFilled` | Broker WebSocket handler | `PositionService`, `CapitalAllocatorConsumer`, `SignalRHubConsumer` |
| `OrderCancelled` | Broker WebSocket handler / Kill Switch | `CapitalAllocatorConsumer`, `AuditLogConsumer` |
| `PositionOpened` | `PositionService` | `SignalRHubConsumer`, `AuditLogConsumer` |
| `PositionClosed` | `PositionService` | `CapitalAllocatorConsumer`, `SignalRHubConsumer`, `PnLRecorder` |
| `SignalGenerated` | `StrategyEvaluationQueue` | `AuditLogConsumer`, `SignalRHubConsumer` |
| `AlertTriggered` | `IMonitoringAlertEvaluator` | `AlertNotificationService` (Telegram/Email) |
| `MonitoringAlertTriggered` | `IMonitoringAlertEvaluator` | `AlertNotificationService`, `SignalRHubConsumer` |
| `CandleClosedEvent` | `CandleAggregatorService` | `StrategyEvaluationQueue` per strategy instance |
| `StreamDisconnected` | `ReconnectingBrokerStreamClient` | `AlertNotificationService`, `SignalRHubConsumer` |
| `StreamReconnected` | `ReconnectingBrokerStreamClient` | `SignalRHubConsumer`, `AuditLogConsumer` |
| `PositionMismatchDetected` | Reconciliation Hangfire job | `AlertNotificationService`, `AuditLogConsumer` |
| `StrategyAutoResumed` | `IStartupOrchestrator` Step 6 | `AuditLogConsumer`, `AlertNotificationService` |
| `StrategyMissedSessionWindow` | `IStartupOrchestrator` Step 6 / `IStrategySchedulerJob` | `AuditLogConsumer`, `AlertNotificationService` |
| `ColdRestartPausedEvent` | `IStartupOrchestrator` Step 6 | `StrategyHubConsumer` (push to React banner), `AuditLogConsumer` |
