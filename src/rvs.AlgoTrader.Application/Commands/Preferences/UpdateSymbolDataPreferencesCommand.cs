using MediatR;
using FluentValidation;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Commands.Preferences;

public record UpdateSymbolDataPreferencesCommand(
    string InternalSymbol, string[]? Timeframes, DateOnly? FromDate, int? Priority, bool? IsActive,
    string Actor, string CorrelationId) : IRequest<bool>;

public class UpdateSymbolDataPreferencesValidator : AbstractValidator<UpdateSymbolDataPreferencesCommand>
{
    public UpdateSymbolDataPreferencesValidator()
    {
        RuleFor(x => x.InternalSymbol).NotEmpty();
    }
}
