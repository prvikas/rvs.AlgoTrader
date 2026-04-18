using MediatR;
using FluentValidation;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using DomainClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.Application.Commands.Capital;

public record CreateCapitalAllocationCommand(Guid StrategyInstanceId, decimal AllocatedCapital, string BrokerName, string Actor, string CorrelationId) : IRequest<Guid>;
public record UpdateCapitalAllocationCommand(Guid AllocationId, decimal AllocatedCapital, string BrokerName, string Actor, string CorrelationId) : IRequest<bool>;

/// <summary>Allocate capital to a strategy instance (controller-facing alias for CreateCapitalAllocationCommand).</summary>
public record AllocateCapitalCommand(Guid StrategyInstanceId, decimal Amount, string? BrokerName = null, string Actor = "User") : IRequest<bool>;

/// <summary>Release previously allocated capital from a strategy instance.</summary>
public record DeallocateCapitalCommand(Guid StrategyInstanceId, string Actor = "User") : IRequest<bool>;

/// <summary>
/// CAP-1: Creates or updates a CapitalAllocation record in the DB for the given strategy instance.
/// If an allocation already exists it is updated (upsert semantics).
/// </summary>
public class AllocateCapitalHandler(
    ICapitalAllocationRepository repo,
    DomainClock clock) : IRequestHandler<AllocateCapitalCommand, bool>
{
    private const string DefaultBroker = "mstock";

    public async Task<bool> Handle(AllocateCapitalCommand request, CancellationToken ct)
    {
        var now    = clock.NowInstant();
        var broker = request.BrokerName ?? DefaultBroker;

        var existing = await repo.GetByInstanceAsync(request.StrategyInstanceId, ct);
        if (existing is null)
        {
            var alloc = CapitalAllocation.Create(request.StrategyInstanceId, broker, request.Amount, now);
            await repo.AddAsync(alloc, ct);
        }
        else
        {
            existing.UpdateAllocation(request.Amount, broker, now);
            await repo.UpdateAsync(existing, ct);
        }
        return true;
    }
}

/// <summary>
/// CAP-2: Removes the CapitalAllocation record for the given strategy instance.
/// No-op when no allocation exists.
/// </summary>
public class DeallocateCapitalHandler(
    ICapitalAllocationRepository repo) : IRequestHandler<DeallocateCapitalCommand, bool>
{
    public async Task<bool> Handle(DeallocateCapitalCommand request, CancellationToken ct)
    {
        await repo.DeleteByInstanceAsync(request.StrategyInstanceId, ct);
        return true;
    }
}

public class CreateCapitalAllocationValidator : AbstractValidator<CreateCapitalAllocationCommand>
{
    public CreateCapitalAllocationValidator()
    {
        RuleFor(x => x.StrategyInstanceId).NotEmpty();
        RuleFor(x => x.AllocatedCapital).GreaterThan(0);
        RuleFor(x => x.BrokerName).NotEmpty();
    }
}

public class UpdateCapitalAllocationValidator : AbstractValidator<UpdateCapitalAllocationCommand>
{
    public UpdateCapitalAllocationValidator()
    {
        RuleFor(x => x.AllocationId).NotEmpty();
        RuleFor(x => x.AllocatedCapital).GreaterThan(0);
    }
}

public class AllocateCapitalValidator : AbstractValidator<AllocateCapitalCommand>
{
    public AllocateCapitalValidator()
    {
        RuleFor(x => x.StrategyInstanceId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}
