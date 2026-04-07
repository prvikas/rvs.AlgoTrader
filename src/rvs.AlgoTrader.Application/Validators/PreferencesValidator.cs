using MediatR;
using rvs.AlgoTrader.Application.Commands.Preferences;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Application.Validators;

// Validator for UpdateSymbolDataPreferencesCommand is defined in the command file itself.

public class UpdateSymbolDataPreferencesHandler(
    ISymbolDataPreferencesService service,
    IClock clock) : IRequestHandler<UpdateSymbolDataPreferencesCommand, bool>
{
    public async Task<bool> Handle(UpdateSymbolDataPreferencesCommand request, CancellationToken ct)
    {
        var existing = await service.GetPreferencesAsync(request.InternalSymbol, ct);

        // Default from-date: 1 year ago in IST — uses IClock (AP-001, no DateTime.Now)
        var oneYearAgo = clock.TodayIst().PlusYears(-1);
        var defaultFromDate = new DateOnly(oneYearAgo.Year, oneYearAgo.Month, oneYearAgo.Day);

        var prefs = new SymbolDataPreferences(
            existing?.Id ?? Guid.NewGuid(),
            request.InternalSymbol,
            request.Timeframes ?? existing?.Timeframes ?? ["1m", "5m", "15m"],
            request.FromDate ?? existing?.FromDate ?? defaultFromDate,
            request.Priority ?? existing?.Priority ?? 5,
            request.IsActive ?? existing?.IsActive ?? true);
        await service.UpsertAsync(prefs, ct);
        return true;
    }
}
