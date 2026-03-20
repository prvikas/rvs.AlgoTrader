using rvs.AlgoTrader.Application.Services;
using NodaTime;
namespace rvs.AlgoTrader.Infrastructure.Services;

public sealed class SymbolDataPreferencesService : ISymbolDataPreferencesService
{
    // TODO: inject ISymbolDataPreferencesRepository once EF Core entity is created
    private readonly Dictionary<string, SymbolDataPreferences> _cache = new();

    public Task<SymbolDataPreferences?> GetPreferencesAsync(string symbol, CancellationToken ct)
    {
        _cache.TryGetValue(symbol, out var prefs);
        if (prefs is null)
        {
            // Default: all common timeframes, 1 year back
            var defaultFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1));
            prefs = new SymbolDataPreferences(Guid.NewGuid(), symbol, ["1m", "5m", "15m", "1h", "1d"], defaultFrom, 5, true);
        }
        return Task.FromResult<SymbolDataPreferences?>(prefs);
    }

    public Task UpsertAsync(SymbolDataPreferences preferences, CancellationToken ct)
    {
        _cache[preferences.InternalSymbol] = preferences;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SymbolDataPreferences>> GetAllActiveAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<SymbolDataPreferences>>(_cache.Values.Where(p => p.IsActive).ToList());
}
