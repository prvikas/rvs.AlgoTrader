using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace rvs.AlgoTrader.API.HealthChecks;

public class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer? _redis;

    public RedisHealthCheck(IServiceProvider sp)
    {
        // IConnectionMultiplexer is only registered when Redis is available at startup.
        // If not registered, the health check reports degraded rather than throwing.
        try { _redis = sp.GetService<IConnectionMultiplexer>(); }
        catch { _redis = null; }
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_redis == null)
            return HealthCheckResult.Degraded("Redis is not configured (in-memory fallback active)");

        try
        {
            var db = _redis.GetDatabase();
            await db.PingAsync();
            return HealthCheckResult.Healthy("Redis is reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis is unreachable", ex);
        }
    }
}
