using FluentValidation;
using MediatR;
using rvs.AlgoTrader.Application.Commands.Config;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Validators;

public class SetAppConfigValidator : AbstractValidator<SetAppConfigCommand>
{
    public SetAppConfigValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Value).NotNull();
        RuleFor(x => x.Actor).NotEmpty();
    }
}

/// <summary>
/// DEAD-1 fix: Previously used IAppConfigRepository (in-memory stub), so config changes were
/// silently discarded. Now routes through IAppConfigService which writes to DB + Redis.
/// </summary>
public class SetAppConfigHandler(IAppConfigService config) : IRequestHandler<SetAppConfigCommand, bool>
{
    public async Task<bool> Handle(SetAppConfigCommand request, CancellationToken ct)
    {
        await config.SetAsync(request.Key, request.Value, request.Actor, request.CorrelationId, ct);
        return true;
    }
}
