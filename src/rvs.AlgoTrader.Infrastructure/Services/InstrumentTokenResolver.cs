using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Brokers.Abstractions;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Resolves instrument tokens via the broker's IBrokerInstrumentClient.
/// In-memory cache is populated by calling RefreshAsync (on login + daily Hangfire job).
/// Thread-safe: uses ConcurrentDictionary with per-broker keyed lookups.
/// </summary>
public class InstrumentTokenResolver(
    IBrokerClientFactory factory,
    ILogger<InstrumentTokenResolver> logger) : IInstrumentTokenResolver
{
    // Cache: brokerName → internalSymbol → brokerToken (case-insensitive symbol lookup)
    private readonly ConcurrentDictionary<string, Dictionary<string, InstrumentTokenMapping>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Task<string?> ResolveAsync(string internalSymbol, string brokerName, CancellationToken ct)
    {
        if (_cache.TryGetValue(brokerName, out var map) &&
            map.TryGetValue(internalSymbol, out var mapping))
            return Task.FromResult<string?>(mapping.BrokerToken);

        logger.LogDebug("[TokenResolver] No token found for {Symbol} on {Broker} — run RefreshAsync first", internalSymbol, brokerName);
        return Task.FromResult<string?>(null);
    }

    public Task<IReadOnlyList<InstrumentTokenMapping>> GetAllMappingsAsync(string brokerName, CancellationToken ct)
    {
        if (_cache.TryGetValue(brokerName, out var map))
            return Task.FromResult<IReadOnlyList<InstrumentTokenMapping>>(map.Values.ToList());
        return Task.FromResult<IReadOnlyList<InstrumentTokenMapping>>(Array.Empty<InstrumentTokenMapping>());
    }

    /// <summary>
    /// Downloads the full instrument master from the broker and rebuilds the in-memory cache.
    /// Called: (1) after successful login, (2) daily at 8:00 AM IST via Hangfire.
    /// </summary>
    public async Task RefreshAsync(string brokerName, CancellationToken ct)
    {
        logger.LogInformation("[TokenResolver] Refreshing instrument master for {Broker}", brokerName);
        try
        {
            var client = factory.GetClient(brokerName);
            var mappings = await client.GetInstrumentMasterAsync(ct);

            var map = new Dictionary<string, InstrumentTokenMapping>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in mappings)
                map[m.InternalSymbol] = m;

            _cache[brokerName] = map;
            logger.LogInformation("[TokenResolver] Loaded {Count} instruments for {Broker}", map.Count, brokerName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[TokenResolver] Failed to refresh instrument master for {Broker}", brokerName);
            throw;
        }
    }
}
