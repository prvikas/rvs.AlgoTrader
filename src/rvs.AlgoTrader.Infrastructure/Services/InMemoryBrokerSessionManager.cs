using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// In-memory broker session manager. Used when Redis is not available (local dev / single-instance).
/// Stores sessions in ConcurrentDictionary — lost on restart.
/// </summary>
public class InMemoryBrokerSessionManager(ILogger<InMemoryBrokerSessionManager> logger)
    : IBrokerSessionManager, IAppBrokerSessionManager
{
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new(StringComparer.OrdinalIgnoreCase);

    private record SessionEntry(string AccessToken, DateTimeOffset? ExpiresAt, string? RefreshToken, string? FeedToken = null);

    public Task<string> GetAccessTokenAsync(string brokerName, CancellationToken ct)
    {
        if (_sessions.TryGetValue(brokerName, out var entry) && entry.AccessToken != null)
            return Task.FromResult(entry.AccessToken);
        throw new InvalidOperationException($"No active session for broker '{brokerName}'. Login required.");
    }

    public bool IsSessionValid(string brokerName)
    {
        if (!_sessions.TryGetValue(brokerName, out var entry)) return false;
        if (entry.ExpiresAt.HasValue)
            return entry.ExpiresAt.Value > DateTimeOffset.UtcNow.AddMinutes(5);
        return !string.IsNullOrEmpty(entry.AccessToken);
    }

    public Task StoreSessionAsync(string brokerName, LoginResult result, CancellationToken ct)
    {
        if (!result.Success || result.AccessToken == null) return Task.CompletedTask;
        var entry = new SessionEntry(result.AccessToken, result.ExpiresAt, result.RefreshToken, result.FeedToken);
        _sessions[brokerName] = entry;
        logger.LogInformation("[{Broker}] In-memory session stored. Expires: {Expiry}",
            brokerName, result.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "no expiry set");
        return Task.CompletedTask;
    }

    public Task<string?> TryGetAccessTokenAsync(string brokerName, CancellationToken ct)
    {
        _sessions.TryGetValue(brokerName, out var entry);
        return Task.FromResult(entry?.AccessToken);
    }

    public Task<string?> TryGetFeedTokenAsync(string brokerName, CancellationToken ct)
    {
        _sessions.TryGetValue(brokerName, out var entry);
        return Task.FromResult(entry?.FeedToken);
    }

    public Task RefreshSessionAsync(string brokerName, CancellationToken ct)
    {
        logger.LogWarning("[{Broker}] Session refresh not supported in InMemoryBrokerSessionManager — re-login required", brokerName);
        return Task.CompletedTask;
    }

    public Task InvalidateSessionAsync(string brokerName, CancellationToken ct)
    {
        _sessions.TryRemove(brokerName, out _);
        logger.LogInformation("[{Broker}] In-memory session invalidated", brokerName);
        return Task.CompletedTask;
    }

    public Task EnsureValidSessionAsync(string brokerName, CancellationToken ct)
    {
        if (!IsSessionValid(brokerName))
            logger.LogWarning("[{Broker}] Session invalid — re-login required", brokerName);
        return Task.CompletedTask;
    }

    public Task RefreshAsync(string brokerName, CancellationToken ct)
        => RefreshSessionAsync(brokerName, ct);

    public Task<bool> IsAuthenticatedAsync(string brokerName, CancellationToken ct)
        => Task.FromResult(IsSessionValid(brokerName));
}
