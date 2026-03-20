using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// In-memory idempotency store with 24-hour TTL cleanup.
/// Used when Redis is not available (local dev / single-instance).
/// </summary>
public sealed class InMemoryIdempotencyService(ILogger<InMemoryIdempotencyService> logger) : IIdempotencyService
{
    private readonly ConcurrentDictionary<string, (string Json, DateTimeOffset ExpiresAt)> _store = new();
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public Task<IdempotencyResult> CheckAsync(string key, CancellationToken ct)
    {
        if (_store.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > DateTimeOffset.UtcNow)
            {
                logger.LogDebug("Idempotency hit for key {Key}", key);
                var cached = JsonSerializer.Deserialize<JsonElement>(entry.Json);
                return Task.FromResult(new IdempotencyResult(true, cached));
            }
            _store.TryRemove(key, out _);
        }
        return Task.FromResult(new IdempotencyResult(false, null));
    }

    public Task StoreAsync(string key, object response, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(response);
        _store[key] = (json, DateTimeOffset.UtcNow.Add(Ttl));
        return Task.CompletedTask;
    }
}
