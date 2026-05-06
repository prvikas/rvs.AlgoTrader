using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Clock;

/// <summary>
/// Resolves <see cref="IMarketTimezone"/> for a broker by reading
/// <c>broker_credentials.market_timezone_id</c> from the database.
///
/// Results are cached in-memory for 5 minutes so per-tick calls are cheap.
/// Cache is invalidated via <see cref="InvalidateCache"/> when the broker row
/// is updated through the API (no restart needed).
/// </summary>
public sealed class BrokerTimezoneResolver : IBrokerTimezoneResolver
{
    private readonly IDbContextFactory<AlgoTraderDbContext> _dbFactory;
    private readonly IMemoryCache                           _cache;
    private readonly NodaTime.IClock                        _clock;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public BrokerTimezoneResolver(
        IDbContextFactory<AlgoTraderDbContext> dbFactory,
        IMemoryCache cache,
        NodaTime.IClock clock)
    {
        _dbFactory = dbFactory;
        _cache     = cache;
        _clock     = clock;
    }

    public async Task<IMarketTimezone> ResolveAsync(
        string brokerName, CancellationToken ct = default)
    {
        var cacheKey = CacheKey(brokerName);
        if (_cache.TryGetValue(cacheKey, out IMarketTimezone? cached) && cached is not null)
            return cached;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var broker = await db.Brokers
            .Include(b => b.Timezone)
            .FirstOrDefaultAsync(b => b.Name == brokerName, ct);

        if (broker?.Timezone?.IanaId == null)
            throw new InvalidOperationException(
                $"Broker '{brokerName}' not found or has no timezone configured. " +
                $"Ensure the broker exists in the brokers table with a valid timezone FK.");

        var tz = new BrokerMarketTimezone(broker.Timezone.IanaId, _clock);
        _cache.Set(cacheKey, tz, CacheTtl);
        return tz;
    }

    public IMarketTimezone Resolve(string brokerName)
    {
        var cacheKey = CacheKey(brokerName);
        if (_cache.TryGetValue(cacheKey, out IMarketTimezone? cached) && cached is not null)
            return cached;

        // Fallback sync path — should rarely hit in production (cache is warm)
        using var db = _dbFactory.CreateDbContext();
        var broker = db.Brokers
            .Include(b => b.Timezone)
            .FirstOrDefault(b => b.Name == brokerName);

        if (broker?.Timezone?.IanaId == null)
            throw new InvalidOperationException(
                $"Broker '{brokerName}' not found or has no timezone configured. " +
                $"Ensure the broker exists in the brokers table with a valid timezone FK.");

        var tz = new BrokerMarketTimezone(broker.Timezone.IanaId, _clock);
        _cache.Set(cacheKey, tz, CacheTtl);
        return tz;
    }

    public void InvalidateCache(string brokerName) =>
        _cache.Remove(CacheKey(brokerName));

    private static string CacheKey(string brokerName) => $"broker_tz:{brokerName}";
}
