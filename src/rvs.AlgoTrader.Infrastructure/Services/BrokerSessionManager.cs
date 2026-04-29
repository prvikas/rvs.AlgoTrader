using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.Constants;
using StackExchange.Redis;

namespace rvs.AlgoTrader.Infrastructure.Services;

public class BrokerSessionOptions
{
    public Dictionary<string, BrokerSessionConfig> Brokers { get; set; } = new();
}

public class BrokerSessionConfig
{
    public string ApiKey { get; set; } = "";
    public string? ApiSecret { get; set; }
    public string? RefreshToken { get; set; }
}

/// <summary>
/// Manages per-user broker sessions: token storage in Redis, refresh on expiry.
/// Redis key pattern: broker:session:{userId}:{brokerName}:{field}
/// This ensures User A's MStock token cannot be read or overwritten by User B.
/// </summary>
public class BrokerSessionManager(
    IConnectionMultiplexer redis,
    IBrokerClientFactory factory,
    INotificationService notifications,
    IClock clock,
    ILogger<BrokerSessionManager> logger)
    : rvs.AlgoTrader.Brokers.Abstractions.IBrokerSessionManager,
      rvs.AlgoTrader.Application.Services.IAppBrokerSessionManager
{
    private readonly IDatabase _redis = redis.GetDatabase();
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    // User-scoped Redis key helpers
    private static string TokenKey(string userId, string broker)     => $"broker:session:{userId}:{broker.ToLower()}:access_token";
    private static string ExpiryKey(string userId, string broker)    => $"broker:session:{userId}:{broker.ToLower()}:expires_at";
    private static string RefreshKey(string userId, string broker)   => $"broker:session:{userId}:{broker.ToLower()}:refresh_token";
    private static string FeedTokenKey(string userId, string broker) => $"broker:session:{userId}:{broker.ToLower()}:feed_token";

    // ── IAppBrokerSessionManager ──────────────────────────────────────────────

    public async Task StoreSessionAsync(string userId, string brokerName, LoginResult result, CancellationToken ct)
    {
        if (!result.Success || result.AccessToken == null) return;

        var ttl = result.ExpiresAt.HasValue
            ? result.ExpiresAt.Value - clock.NowInstant().ToDateTimeOffset()
            : TimeSpan.FromHours(8);

        await _redis.StringSetAsync(TokenKey(userId, brokerName), result.AccessToken, ttl);

        if (result.ExpiresAt.HasValue)
            await _redis.StringSetAsync(ExpiryKey(userId, brokerName), result.ExpiresAt.Value.ToString("O"));

        if (!string.IsNullOrEmpty(result.RefreshToken))
            await _redis.StringSetAsync(RefreshKey(userId, brokerName), result.RefreshToken);

        if (!string.IsNullOrEmpty(result.FeedToken))
            await _redis.StringSetAsync(FeedTokenKey(userId, brokerName), result.FeedToken, ttl);

        logger.LogInformation("[{Broker}] Session stored for user {UserId}. Expires: {Expiry}",
            brokerName, userId, result.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "unknown");
    }

    public Task<bool> IsAuthenticatedAsync(string userId, string brokerName, CancellationToken ct)
    {
        var expiry = _redis.StringGet(ExpiryKey(userId, brokerName));
        if (!expiry.HasValue)
        {
            // No expiry stored — check if access token exists at all
            var token = _redis.StringGet(TokenKey(userId, brokerName));
            return Task.FromResult(token.HasValue);
        }
        if (!DateTimeOffset.TryParse(expiry.ToString(), out var expiresAt))
            return Task.FromResult(true);
        return Task.FromResult(expiresAt > clock.NowInstant().ToDateTimeOffset().AddMinutes(5));
    }

    public async Task<string?> TryGetAccessTokenAsync(string userId, string brokerName, CancellationToken ct)
    {
        var token = await _redis.StringGetAsync(TokenKey(userId, brokerName));
        return token.HasValue ? token.ToString() : null;
    }

    public async Task<string?> TryGetFeedTokenAsync(string userId, string brokerName, CancellationToken ct)
    {
        var token = await _redis.StringGetAsync(FeedTokenKey(userId, brokerName));
        return token.HasValue ? token.ToString() : null;
    }

    public async Task EnsureValidSessionAsync(string userId, string brokerName, CancellationToken ct)
    {
        if (!await IsAuthenticatedAsync(userId, brokerName, ct))
        {
            logger.LogWarning("[{Broker}] Session invalid for user {UserId} — triggering refresh", brokerName, userId);
            await RefreshAsync(userId, brokerName, ct);
        }
    }

    public async Task RefreshAsync(string userId, string brokerName, CancellationToken ct)
    {
        logger.LogInformation("[{Broker}] Refreshing session for user {UserId}", brokerName, userId);
        var client = factory.GetClient(brokerName);

        switch (client.AuthFlowType)
        {
            case Domain.Enums.BrokerAuthFlowType.OAuth:
                await RefreshOAuthSessionAsync(userId, brokerName, client, ct);
                break;
            case Domain.Enums.BrokerAuthFlowType.DirectCredentials:
                // Cannot silently refresh without user credentials — notify
                await notifications.SendAsync("TELEGRAM", "CRITICAL",
                    $"[{brokerName}] Session expired for user {userId}. Manual re-login required.", ct);
                break;
            default:
                logger.LogWarning("[{Broker}] No auto-refresh for user {UserId}", brokerName, userId);
                break;
        }
    }

    private async Task RefreshOAuthSessionAsync(
        string userId, string brokerName, IFullBrokerClient client, CancellationToken ct)
    {
        var refreshToken = await _redis.StringGetAsync(RefreshKey(userId, brokerName));
        if (!refreshToken.HasValue)
        {
            logger.LogError("[{Broker}] No refresh_token in Redis for user {UserId}", brokerName, userId);
            await notifications.SendAsync("TELEGRAM", "CRITICAL",
                $"[{brokerName}] Session expired for user {userId} — no refresh_token. Manual login required.", ct);
            return;
        }

        var creds = new BrokerCredentials(brokerName, "", null, null, refreshToken.ToString(), null, null, null);
        var result = await client.AuthenticateAsync(creds, ct);

        if (result.Success)
        {
            await StoreSessionAsync(userId, brokerName, result, ct);
            logger.LogInformation("[{Broker}] Session refreshed for user {UserId}", brokerName, userId);
        }
        else
        {
            logger.LogError("[{Broker}] Token refresh failed for user {UserId}: {Error}", brokerName, userId, result.ErrorMessage);
            await notifications.SendAsync("TELEGRAM", "CRITICAL",
                $"[{brokerName}] Session refresh failed for user {userId}: {result.ErrorMessage}", ct);
        }
    }

    // ── IBrokerSessionManager (legacy single-user interface for StartupOrchestrator) ──
    // These use the system user ID so they remain compatible with background job callers.

    private const string SystemUserId = "00000000-0000-0000-0000-000000000001";

    public async Task<string> GetAccessTokenAsync(string brokerName, CancellationToken ct)
    {
        // Background/startup path — iterates all users; returns first found token.
        // For production multi-user trading, callers should use IAppBrokerSessionManager with explicit userId.
        var token = await _redis.StringGetAsync(TokenKey(SystemUserId, brokerName));
        if (token.HasValue) return token.ToString();
        throw new InvalidOperationException($"No active session for broker '{brokerName}'. Login required.");
    }

    public bool IsSessionValid(string brokerName)
        => IsAuthenticatedAsync(SystemUserId, brokerName, CancellationToken.None).GetAwaiter().GetResult();

    public Task RefreshSessionAsync(string brokerName, CancellationToken ct)
        => RefreshAsync(SystemUserId, brokerName, ct);

    public Task InvalidateSessionAsync(string brokerName, CancellationToken ct)
    {
        _redis.KeyDelete(TokenKey(SystemUserId, brokerName));
        _redis.KeyDelete(ExpiryKey(SystemUserId, brokerName));
        return Task.CompletedTask;
    }

    public Task EnsureValidSessionAsync(string brokerName, CancellationToken ct)
        => EnsureValidSessionAsync(SystemUserId, brokerName, ct);

    public Task RefreshAsync(string brokerName, CancellationToken ct)
        => RefreshAsync(SystemUserId, brokerName, ct);
}
