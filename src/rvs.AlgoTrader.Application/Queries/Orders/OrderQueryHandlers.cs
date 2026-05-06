using MediatR;
using rvs.AlgoTrader.Application.DTOs.Orders;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Queries.Orders;

public class GetOrdersHandler(IOrderRepository repo) : IRequestHandler<GetOrdersQuery, PagedResult<OrderDto>>
{
    public async Task<PagedResult<OrderDto>> Handle(GetOrdersQuery request, CancellationToken ct)
    {
        var page = request.Page ?? 1;
        var pageSize = request.PageSize ?? 50;
        var recent = await repo.GetRecentAsync(page * pageSize, ct);
        var total = recent.Count;
        var items = recent.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(order => new OrderDto(
                order.Id, order.Broker?.Name ?? "Unknown", order.BrokerOrderId, order.InternalSymbol,
                order.OrderType.ToString().ToUpper(), order.Direction.ToString().ToUpper(),
                order.Quantity, order.Price, order.TriggerPrice, order.Status.ToString().ToUpper(),
                order.StrategyRunId, order.PlacedAt?.ToDateTimeOffset(), order.FilledAt?.ToDateTimeOffset(),
                order.FillPrice, order.TrailingSl, order.TrailingTp, order.CreatedAt.ToDateTimeOffset()))
            .ToList();
        return new PagedResult<OrderDto>(items, total, page, pageSize);
    }
}
