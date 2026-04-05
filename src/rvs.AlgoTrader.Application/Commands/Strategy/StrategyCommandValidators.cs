using FluentValidation;

namespace rvs.AlgoTrader.Application.Commands.Strategy;

/// <summary>
/// #130: Validators for strategy lifecycle commands.
/// Registered automatically by AddValidatorsFromAssembly scanning the Application assembly.
/// </summary>
public class CreateStrategyInstanceCommandValidator : AbstractValidator<CreateStrategyInstanceCommand>
{
    private static readonly HashSet<string> ValidModes =
        ["Live", "ForwardTest", "Backtest", "Paper"];

    public CreateStrategyInstanceCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Strategy name is required.")
            .MaximumLength(200).WithMessage("Strategy name must be ≤ 200 characters.");

        RuleFor(x => x.StrategyType)
            .NotEmpty().WithMessage("StrategyType is required.");

        RuleFor(x => x.Mode)
            .NotEmpty()
            .Must(m => ValidModes.Contains(m))
            .WithMessage($"Mode must be one of: {string.Join(", ", ValidModes)}.");

        // Symbol is required for non-watchlist strategies
        RuleFor(x => x.InternalSymbol)
            .NotEmpty().When(x => x.WatchlistId == null)
            .WithMessage("InternalSymbol is required when no WatchlistId is provided.");

        RuleFor(x => x.Timeframe)
            .NotEmpty().When(x => x.WatchlistId == null)
            .WithMessage("Timeframe is required when no WatchlistId is provided.")
            .Must(tf => tf is null || new[] { "1m","3m","5m","15m","30m","60m","1d" }.Contains(tf))
            .WithMessage("Timeframe must be one of: 1m, 3m, 5m, 15m, 30m, 60m, 1d.")
            .When(x => x.Timeframe != null);
    }
}
