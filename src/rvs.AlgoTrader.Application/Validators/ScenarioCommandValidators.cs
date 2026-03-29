using FluentValidation;
using rvs.AlgoTrader.Application.Commands.Strategy;

namespace rvs.AlgoTrader.Application.Validators;

/// <summary>
/// Validates <see cref="CreateScenarioCommand"/> before the handler executes.
/// Rules enforced here are purely structural; business-logic guards live in the handler.
/// </summary>
public class CreateScenarioCommandValidator : AbstractValidator<CreateScenarioCommand>
{
    public CreateScenarioCommandValidator()
    {
        RuleFor(x => x.StrategyInstanceId)
            .NotEmpty().WithMessage("StrategyInstanceId is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(100).WithMessage("Name must be 100 characters or fewer.");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description must be 500 characters or fewer.")
            .When(x => x.Description is not null);

        RuleFor(x => x.ParametersJsonOverride)
            .Must(BeValidJson).WithMessage("ParametersJsonOverride must be valid JSON.")
            .When(x => !string.IsNullOrWhiteSpace(x.ParametersJsonOverride));

        RuleFor(x => x.AllocatedCapital)
            .GreaterThan(0).WithMessage("AllocatedCapital must be greater than zero.")
            .When(x => x.AllocatedCapital.HasValue);
    }

    private static bool BeValidJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}

/// <summary>
/// Validates <see cref="UpdateScenarioCommand"/> before the handler executes.
/// </summary>
public class UpdateScenarioCommandValidator : AbstractValidator<UpdateScenarioCommand>
{
    public UpdateScenarioCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Scenario Id is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name must not be empty when provided.")
            .MaximumLength(100).WithMessage("Name must be 100 characters or fewer.")
            .When(x => x.Name is not null);

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description must be 500 characters or fewer.")
            .When(x => x.Description is not null);

        RuleFor(x => x.ParametersJsonOverride)
            .Must(BeValidJson).WithMessage("ParametersJsonOverride must be valid JSON.")
            .When(x => !string.IsNullOrWhiteSpace(x.ParametersJsonOverride));

        RuleFor(x => x.AllocatedCapital)
            .GreaterThan(0).WithMessage("AllocatedCapital must be greater than zero.")
            .When(x => x.AllocatedCapital.HasValue);
    }

    private static bool BeValidJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}

/// <summary>
/// Validates <see cref="PromoteScenarioCommand"/> before the handler executes.
/// </summary>
public class PromoteScenarioCommandValidator : AbstractValidator<PromoteScenarioCommand>
{
    public PromoteScenarioCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Scenario Id is required.");

        RuleFor(x => x.TargetStatus)
            .IsInEnum().WithMessage("TargetStatus must be a valid ScenarioStatus value.");
    }
}
