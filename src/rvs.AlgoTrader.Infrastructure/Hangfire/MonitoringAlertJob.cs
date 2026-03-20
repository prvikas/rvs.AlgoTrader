using Hangfire;
using MassTransit;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Domain.Events;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Hangfire;

public class MonitoringAlertJob(ILogger<MonitoringAlertJob> logger)
{
    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogDebug("[MonitoringAlert] Evaluating alert rules");
        // Production: query monitoring_alert_rules from DB, evaluate each rule,
        // publish MonitoringAlertTriggered for any threshold breaches
        await Task.CompletedTask;
    }
}
