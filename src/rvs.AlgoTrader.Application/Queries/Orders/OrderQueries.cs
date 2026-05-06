using MediatR;
using rvs.AlgoTrader.Application.DTOs.Orders;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Queries.Orders;

public record GetOrderByIdQuery(Guid Id) : IRequest<OrderDto?>;
public record GetOrdersQuery(string? BrokerName = null, string? Status = null, int? Page = null, int? PageSize = null) : IRequest<PagedResult<OrderDto>>;

public class GetOrderByIdHandler(IOrderRepository repo) : IRequestHandler<GetOrderByIdQuery, OrderDto?>
{
    public async Task<OrderDto?> Handle(GetOrderByIdQuery request, CancellationToken ct)
    {
        var order = await repo.GetByIdAsync(request.Id, ct);
        if (order is null) return null;

        return new OrderDto(
            order.Id, order.Broker?.Name ?? "Unknown", order.BrokerOrderId, order.InternalSymbol,
            order.OrderType.ToString().ToUpper(), order.Direction.ToString().ToUpper(),
            order.Quantity, order.Price, order.TriggerPrice, order.Status.ToString().ToUpper(),
            order.StrategyRunId, order.PlacedAt?.ToDateTimeOffset(), order.FilledAt?.ToDateTimeOffset(),
            order.FillPrice, order.TrailingSl, order.TrailingTp, order.CreatedAt.ToDateTimeOffset());
    }
}
