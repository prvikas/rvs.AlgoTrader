using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// In-memory broker session manager — user-scoped.
/// Used when Redis is not available (local dev / single-instance).
/// Sessions are keyed by "{userId}:{brokerName}" so each user holds independent tokens.
/// Write-through to DB via DbBrokerSessionPersistence so tokens survive app restarts.
/// </summary>
public class InMemoryBrokerSessionManager(
    IClock clock,
    ILogger<InMemoryBrokerSessionManager> logger,
    DbBrokerSessionPersistence dbPersistence)
    : IBrokerSessionManager, IAppBrokerSessionManager
{
    // Key: "{userId}:{brokerName}" — case-insensitive on brokerName part
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new(StringComparer.OrdinalIgnoreCase);

    private record SessionEntry(string AccessToken, DateTimeOffset? ExpiresAt, string? RefreshToken, string? FeedToken = null);

    private const string SystemUserId = "00000000-0000-0000-0000-000000000001";
    private static string Key(string userId, string broker) => $"{userId}:{broker.ToLower()}";

    // ── IAppBrokerSessionManager (user-scoped) ────────────────────────────────

    public Task StoreSessionAsync(string userId, string brokerName, LoginResult result, CancellationToken ct)
    {
        if (!result.Success || result.AccessToken == null) return Task.CompletedTask;
        var entry = new SessionEntry(result.AccessToken, result.ExpiresAt, result.RefreshToken, result.FeedToken);
        _sessions[Key(userId, brokerName)] = entry;
        logger.LogInformation("[{Broker}] In-memory session stored for user {UserId}. Expires: {Expiry}",
            brokerName, userId, result.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "no expiry");
        _ = dbPersistence.UpsertAsync(brokerName, result.AccessToken, result.FeedToken,
            result.RefreshToken, result.ExpiresAt, CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task<bool> IsAuthenticatedAsync(string userId, string brokerName, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(Key(userId, brokerName), out var entry)) return Task.FromResult(false);
        if (entry.ExpiresAt.HasValue)
            return Task.FromResult(entry.ExpiresAt.Value > clock.NowInstant().ToDateTimeOffset().AddMinutes(5));
        return Task.FromResult(!string.IsNullOrEmpty(entry.AccessToken));
    }

    public Task<string?> TryGetAccessTokenAsync(string userId, string brokerName, CancellationToken ct)
    {
        _sessions.TryGetValue(Key(userId, brokerName), out var entry);
        return Task.FromResult(entry?.AccessToken);
    }

    public Task EnsureValidSessionAsync(string userId, string brokerName, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(Key(userId, brokerName), out _))
            logger.LogWarning("[{Broker}] No session for user {UserId} — re-login required", brokerName, userId);
        return Task.CompletedTask;
    }

    public Task RefreshAsync(string userId, string brokerName, CancellationToken ct)
    {
        logger.LogWarning("[{Broker}] Session refresh not supported in InMemoryBrokerSessionManager for user {UserId}",
            brokerName, userId);
        return Task.CompletedTask;
    }

    // ── Startup restore (system user) ────────────────────────────────────────

    public void RestoreFromDb(StoredBrokerSession stored)
    {
        var entry = new SessionEntry(stored.AccessToken, stored.ExpiresAt, stored.RefreshToken, stored.FeedToken);
        _sessions[Key(SystemUserId, stored.BrokerName)] = entry;
        logger.LogInformation("[{Broker}] Session restored from DB (system user). Expires: {Expiry}",
            stored.BrokerName, stored.ExpiresAt?.ToString("yyyy-MM-dd HH:mm zzz") ?? "no expiry");
    }

    public Task<string?> TryGetFeedTokenAsync(string userId, string brokerName, CancellationToken ct)
    {
        _sessions.TryGetValue(Key(userId, brokerName), out var entry);
        return Task.FromResult(entry?.FeedToken);
    }

    // ── IBrokerSessionManager (legacy, system-user compat) ───────────────────

    public Task<string> GetAccessTokenAsync(string brokerName, CancellationToken ct)
    {
        if (_sessions.TryGetValue(Key(SystemUserId, brokerName), out var entry) && entry.AccessToken != null)
            return Task.FromResult(entry.AccessToken);
        throw new InvalidOperationException($"No active session for broker '{brokerName}'.");
    }

    public bool IsSessionValid(string brokerName)
        => IsAuthenticatedAsync(SystemUserId, brokerName, CancellationToken.None).GetAwaiter().GetResult();

    public Task RefreshSessionAsync(string brokerName, CancellationToken ct)
        => RefreshAsync(SystemUserId, brokerName, ct);

    public Task InvalidateSessionAsync(string brokerName, CancellationToken ct)
    {
        _sessions.TryRemove(Key(SystemUserId, brokerName), out _);
        _ = dbPersistence.DeleteAsync(brokerName, CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task EnsureValidSessionAsync(string brokerName, CancellationToken ct)
        => EnsureValidSessionAsync(SystemUserId, brokerName, ct);

    public Task RefreshAsync(string brokerName, CancellationToken ct)
        => RefreshAsync(SystemUserId, brokerName, ct);
}
