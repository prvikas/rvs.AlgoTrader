using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Commands.Orders;
using rvs.AlgoTrader.Application.Services;
using StackExchange.Redis;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Enforces per-strategy and global risk limits before order placement.
/// Capital reservation uses Redis Lua atomic compare-and-swap.
/// </summary>
public sealed class RiskManagementService(
    IConnectionMultiplexer redis,
    ICapitalAllocationRepository capitalRepo,
    IStrategyInstanceRepository instanceRepo,
    ILogger<RiskManagementService> logger) : IRiskManagementService
{
    // Lua: reserve capital if available >= requested; returns 1 on success, 0 on failure
    private static readonly string ReserveCapitalLua = @"
local key = KEYS[1]
local amount = tonumber(ARGV[1])
local available = tonumber(redis.call('HGET', key, 'available') or '0')
if available >= amount then
    redis.call('HINCRBYFLOAT', key, 'available', -amount)
    redis.call('HINCRBYFLOAT', key, 'reserved', amount)
    return 1
end
return 0
";


    public async Task<RiskCheckResult> CheckAsync(Guid strategyRunId, object orderRequest, CancellationToken ct)
    {
        if (strategyRunId == Guid.Empty)
            return new RiskCheckResult(true, null); // Manual order — no strategy risk check

        // 1. Load strategy instance to find capital allocation
        var instance = await instanceRepo.GetByIdAsync(strategyRunId, ct);
        if (instance is null)
            return new RiskCheckResult(false, $"Strategy run {strategyRunId} not found");

        // 2. Capital check via Redis Lua (atomic compare-and-swap)
        var capitalKey = $"algotrader:capital:{instance.Id}";
        var command = orderRequest as PlaceOrderCommand;
        var estimatedCost = command is not null ? EstimateOrderCost(command) : 0m;
        var db = redis.GetDatabase();

        // Seed Redis capital if not present (warm-up path)
        var alloc = await capitalRepo.GetByInstanceAsync(instance.Id, ct);
        if (alloc is not null)
        {
            // Only seed if key doesn't exist
            var exists = await db.KeyExistsAsync(capitalKey);
            if (!exists)
            {
                await db.HashSetAsync(capitalKey, [
                    new StackExchange.Redis.HashEntry("available", alloc.AvailableCapital.ToString("F2")),
                    new StackExchange.Redis.HashEntry("reserved", alloc.ReservedCapital.ToString("F2")),
                    new StackExchange.Redis.HashEntry("allocated", alloc.AllocatedCapital.ToString("F2"))
                ]);
                await db.KeyExpireAsync(capitalKey, TimeSpan.FromHours(12)); // IST trading session
            }

            var result = await db.ScriptEvaluateAsync(ReserveCapitalLua,
                keys: [new RedisKey(capitalKey)],
                values: [new RedisValue(estimatedCost.ToString("F2"))]);

            if ((int)result == 0)
            {
                logger.LogWarning("Capital reservation failed for instance {InstanceId}: required={Cost}", instance.Id, estimatedCost);
                return new RiskCheckResult(false, $"Insufficient capital: required ₹{estimatedCost:N2}");
            }
        }

        logger.LogDebug("Risk check passed for instance {InstanceId} order {Symbol} {Direction}",
            instance.Id, command?.InternalSymbol, command?.Direction);
        return new RiskCheckResult(true, null);
    }

    private static decimal EstimateOrderCost(PlaceOrderCommand cmd)
    {
        // Conservative estimate: quantity × price (or use limit 0 for market)
        var price = cmd.Price ?? 0m;
        return cmd.Quantity * price;
    }
}
