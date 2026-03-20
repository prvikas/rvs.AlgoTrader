using MassTransit;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Events;

namespace rvs.AlgoTrader.Infrastructure.Messaging.Consumers;

/// <summary>
/// Writes all domain events to the append-only audit_log table.
/// Consumes all event types — provides complete audit trail for SEBI compliance.
/// </summary>
public class AuditLogConsumer(IAuditService audit) :
    IConsumer<OrderPlaced>,
    IConsumer<OrderFilled>,
    IConsumer<OrderCancelled>,
    IConsumer<PositionOpened>,
    IConsumer<PositionClosed>,
    IConsumer<SignalGenerated>,
    IConsumer<AlertTriggered>,
    IConsumer<StreamDisconnected>,
    IConsumer<StreamReconnected>,
    IConsumer<PositionMismatchDetected>,
    IConsumer<StrategyAutoResumed>,
    IConsumer<ColdRestartPausedEvent>
{
    public async Task Consume(ConsumeContext<OrderPlaced> context)
        => await audit.LogAsync("ORDER_PLACED", "System", "Order", context.Message.BrokerOrderId,
            context.Message, context.Message.CorrelationId, context.CancellationToken);

    public async Task Consume(ConsumeContext<OrderFilled> context)
        => await audit.LogAsync("ORDER_FILLED", "System", "Order", context.Message.BrokerOrderId,
            context.Message, context.Message.CorrelationId, context.CancellationToken);

    public async Task Consume(ConsumeContext<OrderCancelled> context)
        => await audit.LogAsync("ORDER_CANCELLED", "System", "Order", context.Message.BrokerOrderId,
            context.Message, context.Message.CorrelationId, context.CancellationToken);

    public async Task Consume(ConsumeContext<PositionOpened> context)
        => await audit.LogAsync("POSITION_OPENED", "System", "Position", context.Message.PositionId.ToString(),
            context.Message, context.Message.CorrelationId, context.CancellationToken);

    public async Task Consume(ConsumeContext<PositionClosed> context)
        => await audit.LogAsync("POSITION_CLOSED", "System", "Position", context.Message.PositionId.ToString(),
            context.Message, context.Message.CorrelationId, context.CancellationToken);

    public async Task Consume(ConsumeContext<SignalGenerated> context)
        => await audit.LogAsync("SIGNAL_GENERATED", "System", "Strategy", context.Message.StrategyInstanceId.ToString(),
            context.Message, context.Message.CorrelationId, context.CancellationToken);

    public async Task Consume(ConsumeContext<AlertTriggered> context)
        => await audit.LogAsync("ALERT_TRIGGERED", "System", "Alert", context.Message.AlertRuleId.ToString(),
            context.Message, context.Message.CorrelationId, context.CancellationToken);

    public async Task Consume(ConsumeContext<StreamDisconnected> context)
        => await audit.LogAsync("STREAM_DISCONNECTED", "System", "Broker", context.Message.BrokerName,
            context.Message, context.Message.CorrelationId, context.CancellationToken);

    public async Task Consume(ConsumeContext<StreamReconnected> context)
        => await audit.LogAsync("STREAM_RECONNECTED", "System", "Broker", context.Message.BrokerName,
            context.Message, context.Message.CorrelationId, context.CancellationToken);

    public async Task Consume(ConsumeContext<PositionMismatchDetected> context)
        => await audit.LogAsync("POSITION_MISMATCH", "System", "Broker", context.Message.BrokerName,
            context.Message, context.Message.CorrelationId, context.CancellationToken);

    public async Task Consume(ConsumeContext<StrategyAutoResumed> context)
        => await audit.LogAsync("STRATEGY_AUTO_RESUMED", "System", "Strategy", context.Message.StrategyInstanceId.ToString(),
            context.Message, context.Message.CorrelationId, context.CancellationToken);

    public async Task Consume(ConsumeContext<ColdRestartPausedEvent> context)
        => await audit.LogAsync("STRATEGY_COLD_RESTART_PAUSED", "System", "Strategy", context.Message.StrategyInstanceId.ToString(),
            context.Message, context.Message.CorrelationId, context.CancellationToken);
}
