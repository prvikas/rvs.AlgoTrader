using MediatR;
using rvs.AlgoTrader.Application.DTOs.Strategy;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Queries.Capital;

public record GetCapitalAllocationsQuery() : IRequest<IReadOnlyList<CapitalAllocationDto>>;

public class GetCapitalAllocationsHandler(ICapitalAllocationRepository repo) : IRequestHandler<GetCapitalAllocationsQuery, IReadOnlyList<CapitalAllocationDto>>
{
    public async Task<IReadOnlyList<CapitalAllocationDto>> Handle(GetCapitalAllocationsQuery request, CancellationToken ct)
    {
        var allocations = await repo.GetAllAsync(ct);
        return allocations
            .Select(a => new CapitalAllocationDto(
                a.Id, a.StrategyInstanceId, a.BrokerName,
                a.AllocatedCapital, a.ReservedCapital, a.AvailableCapital,
                a.UpdatedAt.ToDateTimeOffset()))
            .ToList();
    }
}
