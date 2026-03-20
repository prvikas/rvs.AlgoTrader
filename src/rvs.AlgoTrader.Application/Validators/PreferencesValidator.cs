using MediatR;
using rvs.AlgoTrader.Application.Commands.Preferences;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Validators;

// Validator for UpdateSymbolDataPreferencesCommand is defined in the command file itself.

public class UpdateSymbolDataPreferencesHandler(ISymbolDataPreferencesService service) : IRequestHandler<UpdateSymbolDataPreferencesCommand, bool>
{
    public async Task<bool> Handle(UpdateSymbolDataPreferencesCommand request, CancellationToken ct)
    {
        var existing = await service.GetPreferencesAsync(request.InternalSymbol, ct);
        var prefs = new SymbolDataPreferences(
            existing?.Id ?? Guid.NewGuid(),
            request.InternalSymbol,
            request.Timeframes ?? existing?.Timeframes ?? ["1m", "5m", "15m"],
            request.FromDate ?? existing?.FromDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
            request.Priority ?? existing?.Priority ?? 5,
            request.IsActive ?? existing?.IsActive ?? true);
        await service.UpsertAsync(prefs, ct);
        return true;
    }
}
