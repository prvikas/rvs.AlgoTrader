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

public class SetAppConfigHandler(IAppConfigRepository repo, IAuditService audit, Domain.Interfaces.IClock clock) : IRequestHandler<SetAppConfigCommand, bool>
{
    public async Task<bool> Handle(SetAppConfigCommand request, CancellationToken ct)
    {
        await repo.SetAsync(request.Key, request.Value, clock.NowInstant(), ct);
        await audit.LogAsync(
            "APP_CONFIG_SET", request.Actor, "AppConfig", request.Key,
            new { request.Value }, request.CorrelationId, ct);
        return true;
    }
}
