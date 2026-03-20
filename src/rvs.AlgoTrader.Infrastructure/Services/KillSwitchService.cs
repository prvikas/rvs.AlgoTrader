using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using StackExchange.Redis;
using System.Text.Json;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Redis-backed kill switch with dual-write (hash + pub/sub).
/// Dual-write ensures both persistence (hash) and real-time fan-out (pub/sub).
/// Key: algotrader:kill_switch   Fields: is_active, activated_by, reason, activated_at
/// </summary>
public sealed class KillSwitchService(IConnectionMultiplexer redis, Domain.Interfaces.IClock clock, ILogger<KillSwitchService> logger) : IKillSwitchService
{
    private const string KillSwitchKey = "algotrader:kill_switch";
    private const string KillSwitchChannel = "algotrader:kill_switch:events";

    public async Task<bool> IsActiveAsync(CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var value = await db.HashGetAsync(KillSwitchKey, "is_active");
        return value == "true";
    }

    public async Task ActivateAsync(string actor, string reason, string correlationId, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var now = clock.NowInstant().ToDateTimeOffset().ToString("O");

        await db.HashSetAsync(KillSwitchKey, [
            new HashEntry("is_active", "true"),
            new HashEntry("activated_by", actor),
            new HashEntry("reason", reason),
            new HashEntry("activated_at", now),
            new HashEntry("correlation_id", correlationId)
        ]);

        // Pub/sub for immediate fan-out to all connected instances
        var pub = redis.GetSubscriber();
        var payload = JsonSerializer.Serialize(new { Action = "ACTIVATE", Actor = actor, Reason = reason, OccurredAt = now });
        await pub.PublishAsync(RedisChannel.Literal(KillSwitchChannel), payload);

        logger.LogWarning("Kill switch ACTIVATED by {Actor}: {Reason} [{CorrelationId}]", actor, reason, correlationId);
    }

    public async Task DeactivateAsync(string actor, string correlationId, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var now = clock.NowInstant().ToDateTimeOffset().ToString("O");

        await db.HashSetAsync(KillSwitchKey, [
            new HashEntry("is_active", "false"),
            new HashEntry("deactivated_by", actor),
            new HashEntry("deactivated_at", now),
            new HashEntry("correlation_id", correlationId)
        ]);

        var pub = redis.GetSubscriber();
        var payload = JsonSerializer.Serialize(new { Action = "DEACTIVATE", Actor = actor, OccurredAt = now });
        await pub.PublishAsync(RedisChannel.Literal(KillSwitchChannel), payload);

        logger.LogInformation("Kill switch DEACTIVATED by {Actor} [{CorrelationId}]", actor, correlationId);
    }

    public async Task<KillSwitchStatus> GetStatusAsync(CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var fields = await db.HashGetAllAsync(KillSwitchKey);
        var dict = fields.ToDictionary(f => f.Name.ToString(), f => f.Value.ToString());

        var isActive = dict.TryGetValue("is_active", out var v) && v == "true";
        dict.TryGetValue("activated_by", out var activatedBy);
        dict.TryGetValue("reason", out var reason);
        DateTimeOffset? activatedAt = null;
        if (dict.TryGetValue("activated_at", out var atStr) && DateTimeOffset.TryParse(atStr, out var parsed))
            activatedAt = parsed;

        return new KillSwitchStatus(isActive, activatedBy, reason, activatedAt);
    }
}
