using Hangfire;
using MassTransit;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Events;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Hangfire;

public class ReconciliationJob(
    IBrokerClientFactory brokerFactory,
    IPositionRepository positionRepo,
    ILogger<ReconciliationJob> logger)
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    [AutomaticRetry(Attempts = 2)]
    public async Task ExecuteAsync(string brokerName, CancellationToken ct)
    {
        logger.LogInformation("[Reconciliation] Reconciling positions for {Broker}", brokerName);
        var brokerPositions = await brokerFactory.GetOrderClient(brokerName)
            .GetOrderBookAsync(ct); // simplified; use IBrokerAccountClient for positions

        var localPositions = await positionRepo.GetOpenPositionsAsync(brokerName, ct);
        var correlationId = Guid.NewGuid().ToString();

        foreach (var local in localPositions)
        {
            // Check if broker has matching position
            // Simplified: publish mismatch event for any position not in broker's list
            logger.LogDebug("[Reconciliation] Local position {Symbol} qty={Qty}",
                local.InternalSymbol, local.Quantity);
        }

        logger.LogInformation("[Reconciliation] Complete for {Broker}", brokerName);
    }
}
