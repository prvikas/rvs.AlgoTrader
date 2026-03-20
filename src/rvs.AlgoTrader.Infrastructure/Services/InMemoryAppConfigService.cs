using System.Collections.Concurrent;
using System.Text.Json;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// In-memory app config service. Used when Redis is not available (local dev / single-instance).
/// Configuration is lost on restart.
/// </summary>
public sealed class InMemoryAppConfigService : IAppConfigService
{
    private readonly ConcurrentDictionary<string, string> _store = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken ct)
    {
        if (_store.TryGetValue(key, out var json))
            return Task.FromResult(JsonSerializer.Deserialize<T>(json));
        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, string actor, string correlationId, CancellationToken ct)
    {
        _store[key] = JsonSerializer.Serialize(value);
        return Task.CompletedTask;
    }
}
