using MassTransit;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Events;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Converts strategy signals into broker orders.
/// Checks: kill switch → capital reservation → idempotency → risk limits → order placement.
/// Publishes OrderPlaced domain event on success.
/// </summary>
public class LiveExecutionEngine(
    IBrokerClientFactory brokerFactory,
    ICapitalAllocator capitalAllocator,
    IIdempotencyService idempotency,
    IKillSwitchService killSwitch,
    IOrderRepository orderRepo,
    IPublishEndpoint bus,
    IClock clock,
    ILogger<LiveExecutionEngine> logger) : ILiveExecutionEngine
{

    public async Task ExecuteSignalAsync(
        StrategyInstance instance,
        SignalResult signal,
        string correlationId,
        CancellationToken ct)
    {
        // 1. Kill switch check
        if (await killSwitch.IsActiveAsync(ct))
        {
            logger.LogWarning("[LiveExecution] Kill switch active — signal suppressed for {Instance}", instance.Name);
            return;
        }

        // 2. Idempotency key = hash of instance + signal + candle timestamp
        var idempotencyKey = $"{instance.Id}:{signal.Signal}:{clock.NowInstant().ToUnixTimeMilliseconds()}";
        var idempotencyCheck = await idempotency.CheckAsync(idempotencyKey, ct);
        if (idempotencyCheck.IsDuplicate)
        {
            logger.LogDebug("[LiveExecution] Duplicate signal suppressed by idempotency: {Key}", idempotencyKey);
            return;
        }

        // 3. Determine direction and quantity
        var direction = signal.Signal == "BUY" ? OrderDirection.Buy : OrderDirection.Sell;
        var quantity = instance.LotSize > 0 ? instance.LotSize : 1;

        // 4. Capital reservation
        var orderValue = (signal.EntryPrice ?? 0) * quantity;
        var reserved = await capitalAllocator.TryReserveAsync(instance.Id, orderValue, ct);
        if (!reserved)
        {
            logger.LogWarning("[LiveExecution] Insufficient capital for {Instance}", instance.Name);
            return;
        }

        // 5. Place order
        var brokerClient = brokerFactory.GetOrderClient(instance.BrokerName ?? "Zerodha");
        var orderRequest = new OrderRequest(
            instance.InternalSymbol,
            instance.BrokerToken ?? instance.InternalSymbol,
            "MARKET",
            signal.Signal,
            quantity,
            signal.EntryPrice,
            null,
            instance.Exchange,
            instance.ProductType,
            idempotencyKey,
            instance.CurrentRunId,
            correlationId);

        var brokerResult = await brokerClient.PlaceOrderAsync(orderRequest, ct);
        var now = clock.NowInstant();

        // 6. Save order record using the entity factory method
        var order = Order.Create(
            instance.BrokerName ?? "Zerodha",
            instance.InternalSymbol,
            OrderType.Market,
            direction,
            quantity,
            signal.EntryPrice,
            null,
            idempotencyKey,
            correlationId,
            instance.CurrentRunId,
            now);

        if (brokerResult.Success && brokerResult.BrokerOrderId != null)
            order.MarkPlaced(brokerResult.BrokerOrderId, now);
        else
            order.MarkRejected(now);

        await orderRepo.AddAsync(order, ct);

        if (brokerResult.Success)
        {
            await idempotency.StoreAsync(idempotencyKey, brokerResult, ct);
            await bus.Publish(new OrderPlaced(
                order.Id, instance.BrokerName ?? "Zerodha", brokerResult.BrokerOrderId!,
                instance.InternalSymbol, "MARKET", signal.Signal,
                quantity, signal.EntryPrice,
                instance.CurrentRunId, correlationId, clock.NowIst()), ct);

            logger.LogInformation("[LiveExecution] Order placed: {OrderId} for {Instance} ({Signal})",
                brokerResult.BrokerOrderId, instance.Name, signal.Signal);
        }
        else
        {
            // Release capital on rejection
            await capitalAllocator.ReleaseAsync(instance.Id, orderValue, ct);
            logger.LogError("[LiveExecution] Order rejected for {Instance}: {Reason}",
                instance.Name, brokerResult.RejectionReason);
        }
    }
}
