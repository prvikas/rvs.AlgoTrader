using MediatR;
using FluentValidation;
using rvs.AlgoTrader.Application.DTOs.Orders;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Commands.Orders;

public record PlaceOrderCommand(
    string BrokerName, string InternalSymbol, string OrderType, string Direction,
    int Quantity, decimal? Price, decimal? TriggerPrice, Guid? StrategyRunId,
    string IdempotencyKey, string CorrelationId) : IRequest<PlaceOrderResult>;

public record PlaceOrderResult(bool Success, Guid? OrderId, string? BrokerOrderId, string? Error);

public record CancelOrderCommand(string BrokerOrderId, string BrokerName) : IRequest<bool>;

public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, bool>
{
    // Cancellation requires live broker integration — placeholder
    public Task<bool> Handle(CancelOrderCommand request, CancellationToken ct) => Task.FromResult(true);
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

public class PlaceOrderCommandHandler(IOrderRepository orders, IKillSwitchService killSwitch, IIdempotencyService idempotency, IRiskManagementService risk, Domain.Interfaces.IClock clock) : IRequestHandler<PlaceOrderCommand, PlaceOrderResult>
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

        // 3. Risk check
        var riskResult = await risk.CheckAsync(request.StrategyRunId ?? Guid.Empty, request, ct);
        if (!riskResult.Allowed)
            return new PlaceOrderResult(false, null, null, riskResult.BlockReason);

        // 4. Create order record
        var orderType = Enum.Parse<Domain.Enums.OrderType>(request.OrderType.Replace("-", ""), true);
        var direction = request.Direction == "BUY" ? Domain.Enums.OrderDirection.Buy : Domain.Enums.OrderDirection.Sell;
        var order = Domain.Entities.Order.Create(
            request.BrokerName, request.InternalSymbol, orderType, direction,
            request.Quantity, request.Price, request.TriggerPrice,
            request.IdempotencyKey, request.CorrelationId, request.StrategyRunId, clock.NowInstant());

        await orders.AddAsync(order, ct);
        var result = new PlaceOrderResult(true, order.Id, null, null);
        await idempotency.StoreAsync(request.IdempotencyKey, result, ct);
        return result;
    }
}
