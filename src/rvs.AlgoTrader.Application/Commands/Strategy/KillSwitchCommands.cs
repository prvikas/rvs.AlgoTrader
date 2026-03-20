using MediatR;
using FluentValidation;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Commands.Strategy;

public record ActivateKillSwitchCommand(string Actor, string Reason, string CorrelationId) : IRequest<bool>;
public record DeactivateKillSwitchCommand(string Actor, string CorrelationId) : IRequest<bool>;
public record GetKillSwitchStatusQuery() : IRequest<bool>;

public class ActivateKillSwitchValidator : AbstractValidator<ActivateKillSwitchCommand>
{
    public ActivateKillSwitchValidator()
    {
        RuleFor(x => x.Actor).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty();
    }
}

public class ActivateKillSwitchHandler(IKillSwitchService ks) : IRequestHandler<ActivateKillSwitchCommand, bool>
{
    public async Task<bool> Handle(ActivateKillSwitchCommand request, CancellationToken ct)
    {
        await ks.ActivateAsync(request.Actor, request.Reason, request.CorrelationId, ct);
        return true;
    }
}

public class DeactivateKillSwitchHandler(IKillSwitchService ks) : IRequestHandler<DeactivateKillSwitchCommand, bool>
{
    public async Task<bool> Handle(DeactivateKillSwitchCommand request, CancellationToken ct)
    {
        await ks.DeactivateAsync(request.Actor, request.CorrelationId, ct);
        return true;
    }
}

public class GetKillSwitchStatusHandler(IKillSwitchService ks) : IRequestHandler<GetKillSwitchStatusQuery, bool>
{
    public async Task<bool> Handle(GetKillSwitchStatusQuery request, CancellationToken ct)
        => await ks.IsActiveAsync(ct);
}
