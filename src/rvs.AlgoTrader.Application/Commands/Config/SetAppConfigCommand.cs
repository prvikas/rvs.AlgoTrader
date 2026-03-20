using MediatR;
using FluentValidation;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Commands.Config;

// Used by SetAppConfigHandler (Validators/) — direct repo + audit path
public record SetAppConfigCommand(string Key, string Value, string Actor, string CorrelationId) : IRequest<bool>;

// Used by SetAppConfigValueHandler — service abstraction path
public record SetAppConfigValueCommand(string Key, string Value, string Actor, string CorrelationId) : IRequest<bool>;

public class SetAppConfigValueValidator : AbstractValidator<SetAppConfigValueCommand>
{
    public SetAppConfigValueValidator()
    {
        RuleFor(x => x.Key).NotEmpty();
        RuleFor(x => x.Actor).NotEmpty();
    }
}

public class SetAppConfigValueHandler(IAppConfigService config) : IRequestHandler<SetAppConfigValueCommand, bool>
{
    public async Task<bool> Handle(SetAppConfigValueCommand request, CancellationToken ct)
    {
        await config.SetAsync(request.Key, request.Value, request.Actor, request.CorrelationId, ct);
        return true;
    }
}
