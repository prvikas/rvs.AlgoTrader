using MediatR;
using FluentValidation;

namespace rvs.AlgoTrader.Application.Commands.Capital;

public record CreateCapitalAllocationCommand(Guid StrategyInstanceId, decimal AllocatedCapital, string BrokerName, string Actor, string CorrelationId) : IRequest<Guid>;
public record UpdateCapitalAllocationCommand(Guid AllocationId, decimal AllocatedCapital, string BrokerName, string Actor, string CorrelationId) : IRequest<bool>;

/// <summary>Allocate capital to a strategy instance (controller-facing alias for CreateCapitalAllocationCommand).</summary>
public record AllocateCapitalCommand(Guid StrategyInstanceId, decimal Amount, string? BrokerName = null, string Actor = "User") : IRequest<bool>;

/// <summary>Release previously allocated capital from a strategy instance.</summary>
public record DeallocateCapitalCommand(Guid StrategyInstanceId, string Actor = "User") : IRequest<bool>;

public class AllocateCapitalHandler : IRequestHandler<AllocateCapitalCommand, bool>
{
    public Task<bool> Handle(AllocateCapitalCommand request, CancellationToken ct)
        => Task.FromResult(true); // placeholder — full implementation requires ICapitalAllocator
}

public class DeallocateCapitalHandler : IRequestHandler<DeallocateCapitalCommand, bool>
{
    public Task<bool> Handle(DeallocateCapitalCommand request, CancellationToken ct)
        => Task.FromResult(true); // placeholder
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
