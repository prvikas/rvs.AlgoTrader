using MassTransit;
using Microsoft.AspNetCore.SignalR;
using rvs.AlgoTrader.Domain.Events;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.API.Hubs;

namespace rvs.AlgoTrader.API.Messaging;

/// <summary>
/// Consumes domain events and pushes them to connected React clients via SignalR.
/// Implements all event types relevant to real-time UI updates.
/// Hub clients are expected to call joinGroup("strategy-{id}") for targeted updates.
/// </summary>
public class SignalRHubConsumer(
    IHubContext<StrategyHub> strategyHub,
    IHubContext<AlertHub> alertHub,
    ILogger<SignalRHubConsumer> logger) :
    IConsumer<OrderPlaced>,
    IConsumer<OrderFilled>,
    IConsumer<OrderCancelled>,
    IConsumer<PositionOpened>,
    IConsumer<PositionClosed>,
    IConsumer<SignalGenerated>,
    IConsumer<StreamDisconnected>,
    IConsumer<StreamReconnected>,
    IConsumer<AlertTriggered>,
    IConsumer<MonitoringAlertTriggered>,
    IConsumer<ColdRestartPausedEvent>,
    IConsumer<StrategyAutoResumed>
{

    public async Task Consume(ConsumeContext<OrderPlaced> context)
    {
        var e = context.Message;
        logger.LogDebug("SignalR push: OrderPlaced {OrderId} {Symbol}", e.OrderId, e.InternalSymbol);
        await strategyHub.Clients.Group("all-strategies").SendAsync("OrderPlaced", new
        {
            e.OrderId, e.BrokerName, e.BrokerOrderId, e.InternalSymbol,
            e.OrderType, e.Direction, e.Quantity, e.Price, e.StrategyRunId,
            OccurredAt = e.OccurredAt.ToDateTimeOffset()
        }, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<OrderFilled> context)
    {
        var e = context.Message;
        logger.LogDebug("SignalR push: OrderFilled {OrderId} {Symbol}", e.OrderId, e.InternalSymbol);
        await strategyHub.Clients.Group("all-strategies").SendAsync("OrderFilled", new
        {
            e.OrderId, e.BrokerName, e.BrokerOrderId, e.InternalSymbol,
            e.Direction, e.FilledQuantity, e.FillPrice, e.IsPartialFill, e.StrategyRunId,
            OccurredAt = e.OccurredAt.ToDateTimeOffset()
        }, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<OrderCancelled> context)
    {
        var e = context.Message;
        await strategyHub.Clients.Group("all-strategies").SendAsync("OrderCancelled", new
        {
            e.OrderId, e.BrokerName, e.BrokerOrderId, e.InternalSymbol,
            e.CancelReason, e.StrategyRunId,
            OccurredAt = e.OccurredAt.ToDateTimeOffset()
        }, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<PositionOpened> context)
    {
        var e = context.Message;
        await strategyHub.Clients.Group("all-strategies").SendAsync("PositionOpened", new
        {
            e.PositionId, e.BrokerName, e.InternalSymbol, e.Quantity,
            e.EntryPrice, e.StopLoss, e.TakeProfit, e.StrategyRunId,
            OccurredAt = e.OccurredAt.ToDateTimeOffset()
        }, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<PositionClosed> context)
    {
        var e = context.Message;
        await strategyHub.Clients.Group("all-strategies").SendAsync("PositionClosed", new
        {
            e.PositionId, e.BrokerName, e.InternalSymbol, e.Quantity,
            e.EntryPrice, e.ExitPrice, e.RealizedPnl, e.CloseReason, e.StrategyRunId,
            OccurredAt = e.OccurredAt.ToDateTimeOffset()
        }, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<SignalGenerated> context)
    {
        var e = context.Message;
        logger.LogDebug("SignalR push: Signal {Signal} for {Symbol} from {Strategy}", e.Signal, e.InternalSymbol, e.StrategyName);
        await Task.WhenAll(
            strategyHub.Clients.Group($"strategy-{e.StrategyInstanceId}").SendAsync("SignalGenerated", new
            {
                e.StrategyInstanceId, e.StrategyName, e.InternalSymbol, e.Timeframe,
                e.Signal, e.EntryPrice, e.StopLoss, e.TakeProfit, e.Reason,
                OccurredAt = e.OccurredAt.ToDateTimeOffset()
            }, context.CancellationToken),
            strategyHub.Clients.Group("all-strategies").SendAsync("SignalGenerated", new
            {
                e.StrategyInstanceId, e.StrategyName, e.InternalSymbol, e.Timeframe,
                e.Signal, e.EntryPrice, e.StopLoss, e.TakeProfit, e.Reason,
                OccurredAt = e.OccurredAt.ToDateTimeOffset()
            }, context.CancellationToken));
    }

    public async Task Consume(ConsumeContext<StreamDisconnected> context)
    {
        var e = context.Message;
        logger.LogWarning("SignalR push: StreamDisconnected {Broker} attempt={Attempt}", e.BrokerName, e.ReconnectAttempt);
        await strategyHub.Clients.Group("all-strategies").SendAsync("StreamDisconnected", new
        {
            e.BrokerName, e.Reason, e.ReconnectAttempt,
            OccurredAt = e.OccurredAt.ToDateTimeOffset()
        }, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<StreamReconnected> context)
    {
        var e = context.Message;
        logger.LogInformation("SignalR push: StreamReconnected {Broker} downtime={Downtime}s", e.BrokerName, e.TotalDowntimeSeconds);
        await strategyHub.Clients.Group("all-strategies").SendAsync("StreamReconnected", new
        {
            e.BrokerName, e.TotalDowntimeSeconds, e.ResubscribedSymbols,
            OccurredAt = e.OccurredAt.ToDateTimeOffset()
        }, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<AlertTriggered> context)
    {
        var e = context.Message;
        await alertHub.Clients.Group("alerts").SendAsync("AlertTriggered", new
        {
            e.AlertRuleId, e.AlertType, e.Severity, e.Message, e.Channels,
            OccurredAt = e.OccurredAt.ToDateTimeOffset()
        }, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<MonitoringAlertTriggered> context)
    {
        var e = context.Message;
        await alertHub.Clients.Group("alerts").SendAsync("MonitoringAlert", new
        {
            e.AlertRuleId, e.MetricName, e.MetricValue, e.ThresholdValue,
            e.Operator, e.Severity, e.Message,
            OccurredAt = e.OccurredAt.ToDateTimeOffset()
        }, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ColdRestartPausedEvent> context)
    {
        var e = context.Message;
        logger.LogInformation("SignalR push: ColdRestartPaused {InstanceId} {StrategyName}", e.StrategyInstanceId, e.StrategyName);
        await strategyHub.Clients.Group("all-strategies").SendAsync("ColdRestartPauseNotification", new
        {
            e.StrategyInstanceId, e.StrategyName, e.PauseReason,
            ShutdownAt = e.ShutdownAt.ToDateTimeOffset(),
            OccurredAt = e.OccurredAt.ToDateTimeOffset()
        }, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<StrategyAutoResumed> context)
    {
        var e = context.Message;
        await strategyHub.Clients.Group("all-strategies").SendAsync("StrategyAutoResumed", new
        {
            e.StrategyInstanceId, e.StrategyName, e.ResumeReason,
            SessionStart = e.SessionStart.ToDateTimeOffset(),
            SessionStop = e.SessionStop.ToDateTimeOffset(),
            OccurredAt = e.OccurredAt.ToDateTimeOffset()
        }, context.CancellationToken);
    }
}
