using FluentValidation;
using rvs.AlgoTrader.Application.Commands.Strategy;

namespace rvs.AlgoTrader.Application.Validators;

public class PauseStrategyCommandValidator : AbstractValidator<PauseStrategyCommand>
{
    public PauseStrategyCommandValidator()
    {
        RuleFor(x => x.InstanceId).NotEmpty().WithMessage("InstanceId is required");
    }
}

public class StopStrategyCommandValidator : AbstractValidator<StopStrategyCommand>
{
    public StopStrategyCommandValidator()
    {
        RuleFor(x => x.InstanceId).NotEmpty().WithMessage("InstanceId is required");
    }
}
