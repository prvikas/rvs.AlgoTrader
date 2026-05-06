using MediatR;
using FluentValidation;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.DTOs.Orders;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Application.Commands.Orders;

public record PlaceOrderCommand(
    string BrokerName, string InternalSymbol, string OrderType, string Direction,
    int Quantity, decimal? Price, decimal? TriggerPrice, Guid? StrategyRunId,
    string IdempotencyKey, string CorrelationId) : IRequest<PlaceOrderResult>;

public record PlaceOrderResult(bool Success, Guid? OrderId, string? BrokerOrderId, string? Error);

public record CancelOrderCommand(string BrokerOrderId, string BrokerName) : IRequest<bool>;

public class CancelOrderCommandHandler(
    IBrokerClientFactory brokerFactory,
    IOrderRepository orders,
    IClock clock,
    ILogger<CancelOrderCommandHandler> logger) : IRequestHandler<CancelOrderCommand, bool>
{
    public async Task<bool> Handle(CancelOrderCommand request, CancellationToken ct)
    {
        try
        {
            var client = brokerFactory.GetOrderClient(request.BrokerName);
            var cancelled = await client.CancelOrderAsync(request.BrokerOrderId, ct);

            if (cancelled)
            {
                // Best-effort DB status update via BrokerOrderId index
                var order = await orders.GetByBrokerOrderIdAsync(request.BrokerOrderId, ct);
                if (order != null)
                {
                    order.MarkCancelled(clock.NowInstant());
                    await orders.UpdateAsync(order, ct);
                }
                logger.LogInformation("[CancelOrder] BrokerOrderId={Id} Broker={Broker} cancelled",
                    request.BrokerOrderId, request.BrokerName);
            }
            return cancelled;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[CancelOrder] Failed to cancel BrokerOrderId={Id} Broker={Broker}",
                request.BrokerOrderId, request.BrokerName);
            return false;
        }
    }
}

public class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(x => x.BrokerName).NotEmpty();
        RuleFor(x => x.InternalSymbol).NotEmpty();
        RuleFor(x => x.OrderType).Must(t => new[] { "MARKET", "LIMIT", "SL", "SL-M" }.Contains(t)).WithMessage("Invalid order type");
        RuleFor(x => x.Direction).Must(d => d is "BUY" or "SELL").WithMessage("Direction must be BUY or SELL");
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.Price).GreaterThan(0).When(x => x.OrderType is "LIMIT" or "SL");
        RuleFor(x => x.TriggerPrice).GreaterThan(0).When(x => x.OrderType is "SL" or "SL-M");
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.CorrelationId).NotEmpty();
    }
}

public class PlaceOrderCommandHandler(
    IOrderRepository orders,
    IBrokerRepository brokerRepo,
    IInstrumentRepository instruments,
    IExchangeRepository exchanges,
    IProductTypeRepository productTypes,
    IKillSwitchService killSwitch,
    IIdempotencyService idempotency,
    IRiskManagementService risk,
    IMarketCalendarService calendar,
    Domain.Interfaces.IClock clock) : IRequestHandler<PlaceOrderCommand, PlaceOrderResult>
{
    public async Task<PlaceOrderResult> Handle(PlaceOrderCommand request, CancellationToken ct)
    {
        // 1. Idempotency check
        var idempotencyResult = await idempotency.CheckAsync(request.IdempotencyKey, ct);
        if (idempotencyResult.IsDuplicate && idempotencyResult.CachedResponse is PlaceOrderResult cached)
            return cached;

        // 2. Kill switch check
        if (await killSwitch.IsActiveAsync(ct))
            return new PlaceOrderResult(false, null, null, "Kill switch is active");

        // 3. Market calendar check (AP-011) — reject orders on holidays/weekends
        var today = clock.TodayIst();
        if (!await calendar.IsTradingDayAsync(DateOnly.FromDateTime(today.ToDateTimeUnspecified()), ct))
            return new PlaceOrderResult(false, null, null,
                $"Market is closed today ({today:yyyy-MM-dd}). Orders cannot be placed on holidays or weekends.");

        // 5. Risk check
        var riskResult = await risk.CheckAsync(request.StrategyRunId ?? Guid.Empty, request, ct);
        if (!riskResult.Allowed)
            return new PlaceOrderResult(false, null, null, riskResult.BlockReason);

        // 6. Resolve broker, exchange, and product type IDs
        var broker = await brokerRepo.GetByNameAsync(request.BrokerName, ct)
            ?? throw new InvalidOperationException($"Broker '{request.BrokerName}' not found");

        var instrument = await instruments.GetByInternalSymbolAsync(request.InternalSymbol, ct)
            ?? throw new InvalidOperationException($"Instrument '{request.InternalSymbol}' not found");

        // For now, use defaults for exchange/product type; can be extended to get from instrument metadata
        var exchange = await exchanges.GetByCodeAsync("NSE", ct)
            ?? throw new InvalidOperationException("Exchange 'NSE' not found");
        var productType = await productTypes.GetByCodeAsync("EQ", ct)
            ?? throw new InvalidOperationException("Product type 'EQ' not found");

        // 7. Create order record
        var orderType = Enum.Parse<Domain.Enums.OrderType>(request.OrderType.Replace("-", ""), true);
        var direction = request.Direction == "BUY" ? Domain.Enums.OrderDirection.Buy : Domain.Enums.OrderDirection.Sell;
        var order = Domain.Entities.Order.Create(
            broker.Id, request.InternalSymbol, orderType, direction,
            request.Quantity, request.Price, request.TriggerPrice,
            exchange.Id, productType.Id,
            request.IdempotencyKey, request.CorrelationId, request.StrategyRunId, clock.NowInstant());

        await orders.AddAsync(order, ct);
        var result = new PlaceOrderResult(true, order.Id, null, null);
        await idempotency.StoreAsync(request.IdempotencyKey, result, ct);
        return result;
    }
}
