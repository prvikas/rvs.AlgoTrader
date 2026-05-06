using MediatR;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.Portfolio;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Application.Queries.Portfolio;

public class GetPortfolioSummaryHandler(IStrategyInstanceRepository repo)
    : IRequestHandler<GetPortfolioSummaryQuery, PortfolioSummaryDto>
{
    public async Task<PortfolioSummaryDto> Handle(GetPortfolioSummaryQuery request, CancellationToken ct)
    {
        var all = await repo.GetAllAsync(ct);

        var rows = all.Select(i =>
        {
            var runtime = i.RuntimeState;
            var total = (runtime?.TodayRealizedPnl ?? 0m) + (runtime?.TodayUnrealizedPnl ?? 0m);
            var capital = i.AllocatedCapital;
            var pct = capital > 0 ? total / capital * 100m : 0m;
            return new StrategyPnlRowDto(
                InstanceId: i.Id.ToString(),
                Name: i.Name,
                StrategyType: i.StrategyType,
                InternalSymbol: i.InternalSymbol,
                Mode: i.Mode.ToString(),
                Status: i.Status.ToString(),
                AllocatedCapital: capital,
                TodayRealizedPnl: runtime?.TodayRealizedPnl ?? 0m,
                TodayUnrealizedPnl: runtime?.TodayUnrealizedPnl ?? 0m,
                TodayTotalPnl: total,
                PnlPercent: pct);
        }).ToList();

        return new PortfolioSummaryDto(
            TodayTotalRealizedPnl: rows.Sum(r => r.TodayRealizedPnl),
            TodayTotalUnrealizedPnl: rows.Sum(r => r.TodayUnrealizedPnl),
            TodayTotalPnl: rows.Sum(r => r.TodayTotalPnl),
            TotalAllocatedCapital: rows.Sum(r => r.AllocatedCapital),
            RunningCount: all.Count(i => i.Status == StrategyStatus.Running && i.Mode == StrategyMode.Live),
            PausedCount: all.Count(i => i.Status == StrategyStatus.Paused),
            StoppedCount: all.Count(i => i.Status == StrategyStatus.Stopped),
            ForwardTestCount: all.Count(i => i.Mode == StrategyMode.Forward && i.Status == StrategyStatus.Running),
            ByStrategy: rows);
    }
}

public class GetOpenPositionsHandler(IPositionRepository positionRepo)
    : IRequestHandler<GetOpenPositionsQuery, IReadOnlyList<PositionDto>>
{
    public async Task<IReadOnlyList<PositionDto>> Handle(GetOpenPositionsQuery request, CancellationToken ct)
    {
        var positions = request.BrokerName != null
            ? await positionRepo.GetOpenPositionsAsync(request.BrokerName, ct)
            : await positionRepo.GetOpenAsync(ct);

        if (request.StrategyRunId.HasValue)
            positions = positions.Where(p => p.StrategyRunId == request.StrategyRunId).ToList();

        return positions.Select(p => new PositionDto(
            Id: p.Id.ToString(),
            BrokerName: p.Broker?.Name ?? "Unknown",
            InternalSymbol: p.InternalSymbol,
            Quantity: p.Quantity,
            AvgPrice: p.AvgPrice,
            CurrentPrice: p.CurrentPrice,
            StopLoss: p.StopLoss,
            TakeProfit: p.TakeProfit,
            UnrealizedPnl: p.UnrealizedPnl,
            RealizedPnl: p.RealizedPnl,
            ProductType: p.ProductType?.Code ?? "Unknown",
            StrategyRunId: p.StrategyRunId?.ToString(),
            OpenedAt: p.OpenedAt?.ToDateTimeOffset())).ToList();
    }
}

public class GetRecentOrdersHandler(IOrderRepository orderRepo)
    : IRequestHandler<GetRecentOrdersQuery, IReadOnlyList<OrderDto>>
{
    public async Task<IReadOnlyList<OrderDto>> Handle(GetRecentOrdersQuery request, CancellationToken ct)
    {
        var orders = request.StrategyRunId.HasValue
            ? await orderRepo.GetByStrategyRunAsync(request.StrategyRunId.Value, ct)
            : await orderRepo.GetRecentAsync(request.Page * request.PageSize, ct);

        return orders
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(o => new OrderDto(
                Id: o.Id.ToString(),
                BrokerName: o.Broker?.Name ?? "Unknown",
                BrokerOrderId: o.BrokerOrderId,
                InternalSymbol: o.InternalSymbol,
                OrderType: o.OrderType,
                Direction: o.Direction,
                Status: o.Status,
                Quantity: o.Quantity,
                FilledQuantity: o.FilledQuantity,
                Price: o.Price,
                FillPrice: o.FillPrice,
                TriggerPrice: o.TriggerPrice,
                Exchange: o.Exchange?.Code ?? "Unknown",
                ProductType: o.ProductType?.Code ?? "Unknown",
                StrategyRunId: o.StrategyRunId?.ToString(),
                RejectionReason: o.RejectionReason,
                PlacedAt: o.PlacedAt?.ToDateTimeOffset(),
                FilledAt: o.FilledAt?.ToDateTimeOffset(),
                CreatedAt: o.CreatedAt.ToDateTimeOffset()))
            .ToList();
    }
}
