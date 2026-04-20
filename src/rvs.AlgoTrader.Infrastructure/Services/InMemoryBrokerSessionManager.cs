using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// In-memory broker session manager. Used when Redis is not available (local dev / single-instance).
/// Stores sessions in ConcurrentDictionary — augmented with DB write-through via
/// DbBrokerSessionPersistence so tokens survive app restarts (same-day tokens still valid).
/// StartupOrchestrator.Step7 calls DbBrokerSessionPersistence.LoadAllValidAsync to pre-populate
/// the in-memory dict before any requests arrive.
/// </summary>
public class InMemoryBrokerSessionManager(
    IClock clock,
    ILogger<InMemoryBrokerSessionManager> logger,
    DbBrokerSessionPersistence dbPersistence)
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
            return entry.ExpiresAt.Value > clock.NowInstant().ToDateTimeOffset().AddMinutes(5);
        return !string.IsNullOrEmpty(entry.AccessToken);
    }

    public Task StoreSessionAsync(string brokerName, LoginResult result, CancellationToken ct)
    {
        if (!result.Success || result.AccessToken == null) return Task.CompletedTask;
        var entry = new SessionEntry(result.AccessToken, result.ExpiresAt, result.RefreshToken, result.FeedToken);
        _sessions[brokerName] = entry;
        logger.LogInformation("[{Broker}] In-memory session stored. Expires: {Expiry}",
            brokerName, result.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "no expiry set");

        // Write-through to DB (best-effort, fire-and-forget — in-memory is already updated)
        _ = dbPersistence.UpsertAsync(brokerName, result.AccessToken, result.FeedToken, result.RefreshToken, result.ExpiresAt, CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Restores a session from a pre-loaded DB record (called by StartupOrchestrator.Step7).
    /// Populates the in-memory dict so IsSessionValid/GetAccessToken work immediately.
    /// </summary>
    public void RestoreFromDb(StoredBrokerSession stored)
    {
        var entry = new SessionEntry(stored.AccessToken, stored.ExpiresAt, stored.RefreshToken, stored.FeedToken);
        _sessions[stored.BrokerName] = entry;
        logger.LogInformation("[{Broker}] Session restored from DB. Expires: {Expiry}",
            stored.BrokerName, stored.ExpiresAt?.ToString("yyyy-MM-dd HH:mm zzz") ?? "no expiry");
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
        _ = dbPersistence.DeleteAsync(brokerName, CancellationToken.None);
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
