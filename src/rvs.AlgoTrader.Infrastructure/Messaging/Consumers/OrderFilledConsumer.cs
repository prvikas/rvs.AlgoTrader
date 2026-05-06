using MassTransit;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Events;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Messaging.Consumers;

public class OrderFilledConsumer(
    IOrderRepository orderRepo,
    IPositionRepository positionRepo,
    IBrokerRepository brokerRepo,
    IProductTypeRepository productTypeRepo,
    IPublishEndpoint bus,
    IClock clock,
    ILogger<OrderFilledConsumer> logger) : IConsumer<OrderFilled>
{
    public async Task Consume(ConsumeContext<OrderFilled> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;
        var now = clock.NowInstant();

        logger.LogInformation("[OrderFilled] {BrokerOrderId} {Symbol} qty={Qty} @ {Price}",
            evt.BrokerOrderId, evt.InternalSymbol, evt.FilledQuantity, evt.FillPrice);

        // Update order status using the entity's method
        var order = await orderRepo.GetByIdAsync(evt.OrderId, ct);
        if (order != null)
        {
            order.MarkFilled(evt.FillPrice, evt.FilledQuantity, now);
            await orderRepo.UpdateAsync(order, ct);
        }

        // Update or open position
        if (evt.Direction == OrderDirection.Buy)
        {
            var existing = await positionRepo.GetOpenPositionForSymbolAsync(
                evt.BrokerName, evt.InternalSymbol, ct);

            if (existing == null)
            {
                // Resolve broker name and product type to IDs
                var broker = await brokerRepo.GetByNameAsync(evt.BrokerName, ct);
                var productType = await productTypeRepo.GetByCodeAsync("EQ", ct);  // Default to Equity

                if (broker is null || productType is null)
                {
                    logger.LogWarning("[OrderFilled] Cannot open position: broker or productType not found for {Broker}/{Symbol}",
                        evt.BrokerName, evt.InternalSymbol);
                    return;
                }

                var position = Domain.Entities.Position.Open(
                    broker.Id,
                    evt.InternalSymbol,
                    evt.FilledQuantity,
                    evt.FillPrice,
                    null, null,
                    productType.Id,
                    null,  // brokerAccountId not available in OrderFilled event
                    evt.CorrelationId,
                    now);
                await positionRepo.AddAsync(position, ct);

                await bus.Publish(new PositionOpened(
                    position.Id, evt.BrokerName, evt.InternalSymbol,
                    evt.FilledQuantity, evt.FillPrice, null, null,
                    evt.StrategyRunId, evt.CorrelationId, clock.NowIst()), ct);
            }
        }
    }
}
