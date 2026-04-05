using FluentValidation;
using NodaTime;

namespace rvs.AlgoTrader.Application.DTOs.Backtest;

/// <summary>
/// #130: Validates the backtest request DTO before any engine work starts.
/// Catches bad date ranges, zero capital, and unsupported timeframes early,
/// preventing wasted compute and confusing internal errors downstream.
/// </summary>
public class BacktestRequestDtoValidator : AbstractValidator<BacktestRequestDto>
{
    private static readonly HashSet<string> ValidTimeframes =
        ["1m", "3m", "5m", "15m", "30m", "60m", "1d"];

    public BacktestRequestDtoValidator()
    {
        RuleFor(x => x.StrategyName)
            .NotEmpty().WithMessage("StrategyName is required.");

        RuleFor(x => x.InternalSymbol)
            .NotEmpty().WithMessage("InternalSymbol is required.")
            .MaximumLength(50).WithMessage("InternalSymbol must be ≤ 50 characters.");

        RuleFor(x => x.Timeframe)
            .NotEmpty()
            .Must(tf => ValidTimeframes.Contains(tf))
            .WithMessage($"Timeframe must be one of: {string.Join(", ", ValidTimeframes)}.");

        RuleFor(x => x.FromDate)
            .NotEqual(default(LocalDate)).WithMessage("FromDate is required.");

        RuleFor(x => x.ToDate)
            .NotEqual(default(LocalDate)).WithMessage("ToDate is required.")
            .GreaterThan(x => x.FromDate).WithMessage("ToDate must be after FromDate.");

        // Must have at least 30 days of data to be meaningful
        RuleFor(x => x)
            .Must(x => (x.ToDate - x.FromDate).Days >= 30)
            .WithMessage("Backtest window must be at least 30 days.")
            .OverridePropertyName("DateRange");

        RuleFor(x => x.InitialCapital)
            .GreaterThan(0).WithMessage("InitialCapital must be positive.");

        RuleFor(x => x.RiskPerTradePercent)
            .InclusiveBetween(0.01m, 10m)
            .WithMessage("RiskPerTradePercent must be between 0.01 and 10.");

        RuleFor(x => x.SlippageBasisPoints)
            .GreaterThanOrEqualTo(0).WithMessage("SlippageBasisPoints must be ≥ 0.");

        RuleFor(x => x.BrokerageFlatPerSide)
            .GreaterThanOrEqualTo(0).WithMessage("BrokerageFlatPerSide must be ≥ 0.");

        RuleFor(x => x.CircuitBreakerPct)
            .InclusiveBetween(0m, 1m)
            .WithMessage("CircuitBreakerPct must be between 0 (disabled) and 1.0 (100%).");

        RuleFor(x => x.FillModel)
            .InclusiveBetween(0, 2)
            .WithMessage("FillModel must be 0 (NextBarOpen), 1 (NextBarOpen+Slippage), or 2 (SignalBarClose).");
    }
}
