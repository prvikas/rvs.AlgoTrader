using FluentValidation;
using MediatR;
using rvs.AlgoTrader.Application.Commands.Capital;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using NodaTime;

namespace rvs.AlgoTrader.Application.Validators;

// ── Handlers ──────────────────────────────────────────────────────────────────

public class CreateCapitalAllocationHandler(
    ICapitalAllocationRepository repo,
    IBrokerRepository brokerRepo,
    IStrategyInstanceRepository instances,
    IAuditService audit,
    Domain.Interfaces.IClock clock) : IRequestHandler<CreateCapitalAllocationCommand, Guid>
{
    public async Task<Guid> Handle(CreateCapitalAllocationCommand request, CancellationToken ct)
    {
        // Verify strategy instance exists
        var instance = await instances.GetByIdAsync(request.StrategyInstanceId, ct)
            ?? throw new InvalidOperationException($"Strategy instance {request.StrategyInstanceId} not found");

        // Resolve broker name to broker ID
        var broker = await brokerRepo.GetByNameAsync(request.BrokerName, ct)
            ?? throw new InvalidOperationException($"Broker '{request.BrokerName}' not found");

        var alloc = CapitalAllocation.Create(
            request.StrategyInstanceId,
            broker.Id,
            request.AllocatedCapital,
            clock.NowInstant());

        await repo.AddAsync(alloc, ct);
        await audit.LogAsync(
            "CAPITAL_ALLOCATION_CREATED", request.Actor,
            "CapitalAllocation", alloc.Id.ToString(),
            new { request.AllocatedCapital, BrokerId = broker.Id },
            request.CorrelationId, ct);

        return alloc.Id;
    }
}

public class UpdateCapitalAllocationHandler(
    ICapitalAllocationRepository repo,
    IBrokerRepository brokerRepo,
    IAuditService audit,
    Domain.Interfaces.IClock clock) : IRequestHandler<UpdateCapitalAllocationCommand, bool>
{
    public async Task<bool> Handle(UpdateCapitalAllocationCommand request, CancellationToken ct)
    {
        var alloc = await repo.GetByIdAsync(request.AllocationId, ct)
            ?? throw new InvalidOperationException($"Capital allocation {request.AllocationId} not found");

        // Resolve broker name to broker ID
        var broker = await brokerRepo.GetByNameAsync(request.BrokerName, ct)
            ?? throw new InvalidOperationException($"Broker '{request.BrokerName}' not found");

        alloc.UpdateAllocation(request.AllocatedCapital, broker.Id, clock.NowInstant());
        await repo.UpdateAsync(alloc, ct);
        await audit.LogAsync(
            "CAPITAL_ALLOCATION_UPDATED", request.Actor,
            "CapitalAllocation", alloc.Id.ToString(),
            new { request.AllocatedCapital, BrokerId = broker.Id },
            request.CorrelationId, ct);

        return true;
    }
}
