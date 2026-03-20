using FluentValidation;
using rvs.AlgoTrader.Application.Commands.Strategy;

namespace rvs.AlgoTrader.Application.Validators;

public class PauseStrategyInstanceValidator : AbstractValidator<PauseStrategyInstanceCommand>
{
    public PauseStrategyInstanceValidator()
    {
        RuleFor(x => x.InstanceId).NotEmpty().WithMessage("InstanceId is required");
        RuleFor(x => x.Actor).NotEmpty().WithMessage("Actor is required");
        RuleFor(x => x.CorrelationId).NotEmpty().WithMessage("CorrelationId is required");
    }
}

public class StopStrategyInstanceValidator : AbstractValidator<StopStrategyInstanceCommand>
{
    public StopStrategyInstanceValidator()
    {
        RuleFor(x => x.InstanceId).NotEmpty().WithMessage("InstanceId is required");
        RuleFor(x => x.Actor).NotEmpty().WithMessage("Actor is required");
        RuleFor(x => x.CorrelationId).NotEmpty().WithMessage("CorrelationId is required");
    }
}
