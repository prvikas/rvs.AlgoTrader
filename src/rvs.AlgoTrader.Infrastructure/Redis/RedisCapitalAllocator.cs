using StackExchange.Redis;
using rvs.AlgoTrader.Domain.Interfaces;
namespace rvs.AlgoTrader.Infrastructure.Redis;

/// <summary>
/// Redis Lua-script based atomic capital reservation.
/// Prevents over-allocation by using compare-and-swap Lua script.
/// </summary>
public sealed class RedisCapitalAllocator(IConnectionMultiplexer redis) : ICapitalAllocator
{
    // Lua: atomically reserve amount if available >= amount
    private const string ReserveLua = @"
local available = tonumber(redis.call('GET', KEYS[1]) or '0')
local amount = tonumber(ARGV[1])
if available >= amount then
    redis.call('SET', KEYS[1], available - amount)
    return 1
else
    return 0
end";

    private static RedisKey AvailableKey(Guid id) => $"capital:{id}:available";

    public async Task<bool> TryReserveAsync(Guid strategyInstanceId, decimal amount, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var result = await db.ScriptEvaluateAsync(ReserveLua,
            keys: [AvailableKey(strategyInstanceId)],
            values: [(double)amount]);
        return (long)result == 1;
    }

    public async Task ReleaseAsync(Guid strategyInstanceId, decimal amount, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        await db.StringIncrementAsync(AvailableKey(strategyInstanceId), (double)amount);
    }

    public async Task<decimal> GetAvailableAsync(Guid strategyInstanceId, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var val = await db.StringGetAsync(AvailableKey(strategyInstanceId));
        return val.HasValue ? (decimal)(double)val : 0m;
    }

    public async Task ReconcileFromPositionsAsync(CancellationToken ct)
    {
        // Called by IStartupOrchestrator Step 8 — re-loads from DB positions + capital allocations
        // Implementation in StartupOrchestrator
        await Task.CompletedTask;
    }
}
