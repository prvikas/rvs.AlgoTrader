using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.Services;
using StackExchange.Redis;
using System.Text.Json;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Redis-backed idempotency store. 
/// TTL: 24 hours (matching broker order idempotency window).
/// Key pattern: algotrader:idempotency:{key}
/// </summary>
public sealed class IdempotencyService(IConnectionMultiplexer redis, ILogger<IdempotencyService> logger) : IIdempotencyService
{
    private const string KeyPrefix = "algotrader:idempotency:";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async Task<IdempotencyResult> CheckAsync(string key, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var redisKey = $"{KeyPrefix}{key}";
        var value = await db.StringGetAsync(redisKey);

        if (value.IsNull)
            return new IdempotencyResult(false, null);

        logger.LogDebug("Idempotency hit for key {Key}", key);
        // Deserialize stored response — caller must check type
        var cached = JsonSerializer.Deserialize<JsonElement>(value!);
        return new IdempotencyResult(true, cached);
    }

    public async Task StoreAsync(string key, object response, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var redisKey = $"{KeyPrefix}{key}";
        var json = JsonSerializer.Serialize(response);
        await db.StringSetAsync(redisKey, json, Ttl);
    }
}
