using FluentValidation;
using rvs.AlgoTrader.Application.Commands.Strategy;

namespace rvs.AlgoTrader.Application.Validators;

public class DeactivateKillSwitchValidator : AbstractValidator<DeactivateKillSwitchCommand>
{
    public DeactivateKillSwitchValidator()
    {
        RuleFor(x => x.Actor).NotEmpty().WithMessage("Actor is required");
        RuleFor(x => x.CorrelationId).NotEmpty().WithMessage("CorrelationId is required");
    }
}
