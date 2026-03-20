using MassTransit;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Events;

namespace rvs.AlgoTrader.Infrastructure.Messaging.Consumers;

public class AlertTriggeredConsumer(INotificationService notifications, ILogger<AlertTriggeredConsumer> logger) : IConsumer<AlertTriggered>
{
    public async Task Consume(ConsumeContext<AlertTriggered> context)
    {
        var evt = context.Message;
        logger.LogInformation("[AlertTriggered] {Type} [{Severity}]: {Message}", evt.AlertType, evt.Severity, evt.Message);

        foreach (var channel in evt.Channels)
        {
            try
            {
                await notifications.SendAsync(channel, evt.Severity, evt.Message, context.CancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[AlertTriggered] Failed to send via {Channel}", channel);
            }
        }
    }
}
