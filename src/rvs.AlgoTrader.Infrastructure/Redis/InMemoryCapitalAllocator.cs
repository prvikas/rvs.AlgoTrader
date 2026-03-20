using System.Collections.Concurrent;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Redis;

/// <summary>
/// In-memory capital allocator. Used when Redis is not available (local dev / single-instance).
/// Uses locking to prevent over-allocation.
/// </summary>
public sealed class InMemoryCapitalAllocator : ICapitalAllocator
{
    private readonly ConcurrentDictionary<Guid, decimal> _available = new();
    private readonly object _lock = new();

    public Task<bool> TryReserveAsync(Guid strategyInstanceId, decimal amount, CancellationToken ct)
    {
        lock (_lock)
        {
            var current = _available.GetOrAdd(strategyInstanceId, 0m);
            if (current >= amount)
            {
                _available[strategyInstanceId] = current - amount;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }

    public Task ReleaseAsync(Guid strategyInstanceId, decimal amount, CancellationToken ct)
    {
        lock (_lock)
        {
            _available.AddOrUpdate(strategyInstanceId, amount, (_, existing) => existing + amount);
        }
        return Task.CompletedTask;
    }

    public Task<decimal> GetAvailableAsync(Guid strategyInstanceId, CancellationToken ct)
        => Task.FromResult(_available.GetOrAdd(strategyInstanceId, 0m));

    public Task ReconcileFromPositionsAsync(CancellationToken ct)
        => Task.CompletedTask;
}
