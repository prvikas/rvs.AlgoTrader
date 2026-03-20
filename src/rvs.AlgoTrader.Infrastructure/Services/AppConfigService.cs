using System.Text.Json;
using StackExchange.Redis;
using rvs.AlgoTrader.Application.Services;
namespace rvs.AlgoTrader.Infrastructure.Services;

public sealed class AppConfigService(IConnectionMultiplexer redis, IAuditService audit) : IAppConfigService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var cached = await db.StringGetAsync($"config:{key}");
        if (cached.HasValue) return JsonSerializer.Deserialize<T>(cached!);
        // TODO: DB fallback (EF Core via repository)
        return default;
    }

    public async Task SetAsync<T>(string key, T value, string actor, string correlationId, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var json = JsonSerializer.Serialize(value);
        await db.StringSetAsync($"config:{key}", json, CacheTtl);
        await audit.LogAsync("CONFIG_CHANGED", actor, "AppConfig", key, new { value }, correlationId, ct);
    }
}
