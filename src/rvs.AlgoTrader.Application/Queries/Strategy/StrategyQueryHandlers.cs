using MediatR;
using rvs.AlgoTrader.Application.DTOs.Strategy;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Services;
using NodaTime;

namespace rvs.AlgoTrader.Application.Queries.Strategy;

public class GetStrategyInstanceByIdHandler(IStrategyInstanceRepository repo) : IRequestHandler<GetStrategyInstanceByIdQuery, StrategyInstanceDto?>
{
    public async Task<StrategyInstanceDto?> Handle(GetStrategyInstanceByIdQuery request, CancellationToken ct)
    {
        var inst = await repo.GetByIdAsync(request.Id, ct);
        if (inst is null) return null;
        return MapToDto(inst);
    }

    internal static StrategyInstanceDto MapToDto(Domain.Entities.StrategyInstance inst) => new(
        inst.Id, inst.Name, inst.StrategyType, inst.InternalSymbol, inst.Timeframe,
        inst.Status.ToString(), inst.Mode.ToString(), inst.BrokerName, inst.AllocatedCapital,
        inst.RuntimeState?.AutoResumeOnRestart ?? false, inst.ScheduleJson, inst.ParametersJson,
        inst.FailureBehaviorJson, inst.CreatedAt.ToDateTimeOffset(), inst.UpdatedAt.ToDateTimeOffset());
}

public class GetAllStrategyInstancesHandler(IStrategyInstanceRepository repo) : IRequestHandler<GetAllStrategyInstancesQuery, IReadOnlyList<StrategyInstanceDto>>
{
    public async Task<IReadOnlyList<StrategyInstanceDto>> Handle(GetAllStrategyInstancesQuery request, CancellationToken ct)
    {
        var all = await repo.GetAllActiveAsync(ct);
        return all.Select(GetStrategyInstanceByIdHandler.MapToDto).ToList();
    }
}

public class GetStrategyInstancesHandler(IStrategyInstanceRepository repo) : IRequestHandler<GetStrategyInstancesQuery, PagedResult<StrategyInstanceDto>>
{
    public async Task<PagedResult<StrategyInstanceDto>> Handle(GetStrategyInstancesQuery request, CancellationToken ct)
    {
        var all = await repo.GetAllActiveAsync(ct);
        var total = all.Count;
        var items = all.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(GetStrategyInstanceByIdHandler.MapToDto).ToList();
        return new PagedResult<StrategyInstanceDto>(items, total, request.Page, request.PageSize);
    }
}

public class GetSignalJournalHandler(ISignalJournalRepository repo) : IRequestHandler<GetSignalJournalQuery, PagedResult<SignalJournalEntryDto>>
{
    public async Task<PagedResult<SignalJournalEntryDto>> Handle(GetSignalJournalQuery request, CancellationToken ct)
    {
        var (items, total) = await repo.GetPagedAsync(
            request.StrategyId, request.Symbol, request.Signal, request.SkippedReason,
            request.Page, request.PageSize, ct);
        return new PagedResult<SignalJournalEntryDto>(items, total, request.Page, request.PageSize);
    }
}

public class GetCapitalAllocationHandler(ICapitalAllocationRepository repo) : IRequestHandler<GetCapitalAllocationQuery, CapitalAllocationDto?>
{
    public async Task<CapitalAllocationDto?> Handle(GetCapitalAllocationQuery request, CancellationToken ct)
    {
        var alloc = await repo.GetByInstanceAsync(request.StrategyInstanceId, ct);
        if (alloc is null) return null;
        return new CapitalAllocationDto(
            alloc.Id, alloc.StrategyInstanceId, alloc.BrokerName,
            alloc.AllocatedCapital, alloc.ReservedCapital, alloc.AvailableCapital,
            alloc.UpdatedAt.ToDateTimeOffset());
    }
}
