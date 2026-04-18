using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StackExchange.Redis;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Redis-first config store with write-through to Postgres (migration 040).
/// GetAsync: Redis (L1, 5-min TTL) → DB (L2, persistent). DB hit writes back to Redis.
/// SetAsync: writes to DB first (durable), then Redis (fast read path).
/// This ensures config survives Redis restarts and application cold-boots.
/// </summary>
public sealed class AppConfigService(
    IConnectionMultiplexer redis,
    IAuditService audit,
    IConfiguration config) : IAppConfigService
{
    // 5-minute TTL: long enough to avoid hammering DB, short enough to pick up changes quickly.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private string Cs => config.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Database=algotrader;Username=postgres;Password=postgres";

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct)
    {
        // L1: Redis
        var db = redis.GetDatabase();
        var cached = await db.StringGetAsync($"config:{key}");
        if (cached.HasValue) return JsonSerializer.Deserialize<T>(cached!);

        // L2: DB fallback (AC-1)
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT value_json FROM app_config WHERE key = @k LIMIT 1", conn);
        cmd.Parameters.AddWithValue("k", key);
        var row = await cmd.ExecuteScalarAsync(ct) as string;
        if (row is null) return default;

        // Warm Redis cache from DB hit
        await db.StringSetAsync($"config:{key}", row, CacheTtl);
        return JsonSerializer.Deserialize<T>(row);
    }

    public async Task SetAsync<T>(string key, T value, string actor, string correlationId, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value);

        // Write to DB first (durable) — AC-1
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO app_config (key, value_json, actor, updated_at)
            VALUES (@k, @v, @actor, now())
            ON CONFLICT (key) DO UPDATE
                SET value_json = EXCLUDED.value_json,
                    actor      = EXCLUDED.actor,
                    updated_at = now()
            """, conn);
        cmd.Parameters.AddWithValue("k",     key);
        cmd.Parameters.AddWithValue("v",     json);
        cmd.Parameters.AddWithValue("actor", actor);
        await cmd.ExecuteNonQueryAsync(ct);

        // Then Redis (fast read path)
        var db = redis.GetDatabase();
        await db.StringSetAsync($"config:{key}", json, CacheTtl);

        await audit.LogAsync("CONFIG_CHANGED", actor, "AppConfig", key, new { value }, correlationId, ct);
    }
}
