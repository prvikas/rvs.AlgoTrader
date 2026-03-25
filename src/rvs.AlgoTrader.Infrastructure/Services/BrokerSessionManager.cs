using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Interfaces;
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
/// Manages broker sessions: token storage in Redis, refresh on expiry, per-broker strategy.
/// Upstox: auto-refresh OAuth2 token (expires 3:30 AM IST daily).
/// mStock Type B: re-run session exchange on 401/403; no OAuth2, no refresh_token.
/// Zerodha: alert with login URL on expiry; blocks orders until re-login.
/// </summary>
public class BrokerSessionManager(
    IConnectionMultiplexer redis,
    IBrokerClientFactory factory,
    INotificationService notifications,
    ILogger<BrokerSessionManager> logger)
    : rvs.AlgoTrader.Brokers.Abstractions.IBrokerSessionManager,
      rvs.AlgoTrader.Application.Services.IAppBrokerSessionManager
{
    private readonly IDatabase _redis = redis.GetDatabase();
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    private static string TokenKey(string broker)   => $"broker:session:{broker.ToLower()}:access_token";
    private static string ExpiryKey(string broker)   => $"broker:session:{broker.ToLower()}:expires_at";
    private static string RefreshKey(string broker)  => $"broker:session:{broker.ToLower()}:refresh_token";
    private static string FeedTokenKey(string broker)=> $"broker:session:{broker.ToLower()}:feed_token";

    public async Task<string> GetAccessTokenAsync(string brokerName, CancellationToken ct)
    {
        var token = await _redis.StringGetAsync(TokenKey(brokerName));
        if (token.HasValue) return token.ToString();
        throw new InvalidOperationException($"No active session for broker '{brokerName}'. Login required.");
    }

    public bool IsSessionValid(string brokerName)
    {
        var expiry = _redis.StringGet(ExpiryKey(brokerName));
        if (!expiry.HasValue) return false;
        if (!DateTimeOffset.TryParse(expiry.ToString(), out var expiresAt)) return true;
        return expiresAt > DateTimeOffset.UtcNow.AddMinutes(5); // 5-min buffer
    }

    public async Task StoreSessionAsync(string brokerName, LoginResult result, CancellationToken ct)
    {
        if (!result.Success || result.AccessToken == null) return;

        await _redis.StringSetAsync(TokenKey(brokerName), result.AccessToken,
            result.ExpiresAt.HasValue ? result.ExpiresAt.Value - DateTimeOffset.UtcNow : TimeSpan.FromHours(8));

        if (result.ExpiresAt.HasValue)
            await _redis.StringSetAsync(ExpiryKey(brokerName), result.ExpiresAt.Value.ToString("O"));

        if (!string.IsNullOrEmpty(result.RefreshToken))
            await _redis.StringSetAsync(RefreshKey(brokerName), result.RefreshToken);

        // mStock feed token — needed by RestoreToken on process restart
        if (!string.IsNullOrEmpty(result.FeedToken))
            await _redis.StringSetAsync(FeedTokenKey(brokerName), result.FeedToken,
                result.ExpiresAt.HasValue ? result.ExpiresAt.Value - DateTimeOffset.UtcNow : TimeSpan.FromHours(8));

        logger.LogInformation("[{Broker}] Session stored. Expires: {Expiry}", brokerName,
            result.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "unknown");
    }

    public async Task RefreshSessionAsync(string brokerName, CancellationToken ct)
    {
        logger.LogInformation("[{Broker}] Refreshing session", brokerName);

        switch (brokerName.ToLower())
        {
            case "upstox":
                await RefreshUpstoxSessionAsync(ct);
                break;
            case "mstock":
                await RefreshMStockSessionAsync(ct);
                break;
            case "zerodha":
                await AlertZerodhaLoginRequiredAsync(ct);
                break;
            default:
                logger.LogWarning("[{Broker}] Unknown broker for session refresh", brokerName);
                break;
        }
    }

    private async Task RefreshUpstoxSessionAsync(CancellationToken ct)
    {
        var refreshToken = await _redis.StringGetAsync(RefreshKey("upstox"));
        if (!refreshToken.HasValue)
        {
            logger.LogError("[Upstox] No refresh_token in Redis — full re-auth required");
            await notifications.SendAsync("TELEGRAM", "CRITICAL",
                "Upstox session expired and no refresh_token available. Manual login required.", ct);
            return;
        }

        var client = factory.GetClient("Upstox");
        var creds = new BrokerCredentials("Upstox", "", null, null, refreshToken.ToString(), null, null, null);
        var result = await client.AuthenticateAsync(creds, ct);

        if (result.Success)
        {
            await StoreSessionAsync("Upstox", result, ct);
            logger.LogInformation("[Upstox] Session refreshed successfully");
        }
        else
        {
            logger.LogError("[Upstox] Token refresh failed: {Error}", result.ErrorMessage);
            await notifications.SendAsync("TELEGRAM", "CRITICAL",
                $"Upstox session refresh failed: {result.ErrorMessage}", ct);
        }
    }

    private async Task RefreshMStockSessionAsync(CancellationToken ct)
    {
        // mStock Type B: re-run full session exchange; no refresh_token concept
        var client = factory.GetClient("MStock");
        var apiKey = (await _redis.StringGetAsync("broker:config:mstock:api_key")).ToString();
        var creds = new BrokerCredentials("MStock", apiKey, null, null, null, null, null, null);
        var result = await client.AuthenticateAsync(creds, ct);

        if (result.Success)
        {
            await StoreSessionAsync("MStock", result, ct);
            logger.LogInformation("[mStock] Session re-exchanged successfully");
        }
        else
        {
            logger.LogError("[mStock] Session exchange failed: {Error}", result.ErrorMessage);
            await notifications.SendAsync("TELEGRAM", "CRITICAL",
                $"mStock session exchange failed: {result.ErrorMessage}", ct);
        }
    }

    private async Task AlertZerodhaLoginRequiredAsync(CancellationToken ct)
    {
        var loginUrl = "https://kite.zerodha.com/connect/login?v=3&api_key=YOUR_API_KEY";
        logger.LogWarning("[Zerodha] Session expired — manual login required. URL: {Url}", loginUrl);
        await notifications.SendAsync("TELEGRAM", "CRITICAL",
            $"Zerodha session expired. Manual login required.\nURL: {loginUrl}\nOrders are BLOCKED until re-login.", ct);
    }

    public async Task InvalidateSessionAsync(string brokerName, CancellationToken ct)
    {
        await _redis.KeyDeleteAsync(TokenKey(brokerName));
        await _redis.KeyDeleteAsync(ExpiryKey(brokerName));
        logger.LogInformation("[{Broker}] Session invalidated", brokerName);
    }

    // ── IAppBrokerSessionManager — token retrieval for Step 7 ─────────────────

    public async Task<string?> TryGetAccessTokenAsync(string brokerName, CancellationToken ct)
    {
        var token = await _redis.StringGetAsync(TokenKey(brokerName));
        return token.HasValue ? token.ToString() : null;
    }

    public async Task<string?> TryGetFeedTokenAsync(string brokerName, CancellationToken ct)
    {
        var token = await _redis.StringGetAsync(FeedTokenKey(brokerName));
        return token.HasValue ? token.ToString() : null;
    }

    // ── IBrokerSessionManager explicit implementations ────────────────────────

    public async Task EnsureValidSessionAsync(string brokerName, CancellationToken ct)
    {
        if (!IsSessionValid(brokerName))
        {
            logger.LogWarning("[{Broker}] Session invalid or expired — triggering refresh", brokerName);
            await RefreshSessionAsync(brokerName, ct);
        }
    }

    public Task RefreshAsync(string brokerName, CancellationToken ct)
        => RefreshSessionAsync(brokerName, ct);

    public Task<bool> IsAuthenticatedAsync(string brokerName, CancellationToken ct)
        => Task.FromResult(IsSessionValid(brokerName));
}
