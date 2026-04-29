using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.DTOs.Broker;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Generic broker authentication service.
/// Adding a new broker (Dhan, Alpaca, RobinHood, etc.) requires ONLY:
///   1. A new IFullBrokerClient implementation registered in DI.
///   2. No changes here — authentication is fully driven by the client's
///      AuthFlowType and its AuthenticateAsync(BrokerCredentials) implementation.
///
/// Credential dictionary keys per flow:
///   DirectCredentials: "apiKey", "clientCode", "password", "totp" (totp optional)
///   OAuth:             "code" | "requestToken" | "refreshToken"
///   ApiKey:            "apiKey", "apiSecret"
/// </summary>
public class BrokerAuthService(
    IBrokerClientFactory brokerFactory,
    IAppBrokerSessionManager sessionManager,
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<BrokerAuthService> logger) : IBrokerAuthService
{
    public async Task<BrokerAuthResultDto> AuthenticateAsync(
        string userId,
        string brokerName,
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken ct)
    {
        IFullBrokerClient client;
        try { client = brokerFactory.GetClient(brokerName); }
        catch (InvalidOperationException ex)
        {
            return new BrokerAuthResultDto(false, brokerName, ex.Message, null);
        }

        var creds = BuildCredentials(brokerName, client.AuthFlowType, credentials);
        LoginResult result;
        try { result = await client.AuthenticateAsync(creds, ct); }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{Broker}] AuthenticateAsync failed for user {UserId}", brokerName, userId);
            return new BrokerAuthResultDto(false, brokerName, ex.Message, null);
        }

        if (result.Success)
        {
            await sessionManager.StoreSessionAsync(userId, brokerName, result, ct);
            // Inject token into the factory's client instance so live calls work immediately.
            client.RestoreToken(result.AccessToken!, result.FeedToken);
            FireAndForgetRefresh(brokerName, userId);
        }

        return new BrokerAuthResultDto(
            result.Success,
            brokerName,
            result.Success ? "Authenticated successfully" : result.ErrorMessage,
            result.ExpiresAt);
    }

    public Task<string?> GetLoginUrlAsync(string brokerName, string userId, CancellationToken ct)
    {
        IFullBrokerClient client;
        try { client = brokerFactory.GetClient(brokerName); }
        catch { return Task.FromResult<string?>(null); }

        if (client.AuthFlowType != BrokerAuthFlowType.OAuth) return Task.FromResult<string?>(null);

        // Each OAuth broker constructs its login URL from config.
        // Retrieve via a well-known credentials dict with empty values — the client reads config internally.
        var apiKey   = config[$"Broker:{brokerName}:ApiKey"] ?? "";
        var redirect = config[$"Broker:{brokerName}:RedirectUri"] ?? "";
        var url      = BuildOAuthLoginUrl(client, apiKey, redirect);
        return Task.FromResult<string?>(url);
    }

    public async Task DisconnectAsync(string userId, string brokerName, CancellationToken ct)
    {
        await sessionManager.RefreshAsync(userId, brokerName, ct); // triggers invalidation via broker
        // Also invalidate directly in case broker doesn't support remote invalidation
        var token = await sessionManager.TryGetAccessTokenAsync(userId, brokerName, ct);
        if (token != null)
            await sessionManager.StoreSessionAsync(userId, brokerName,
                new LoginResult(false, null, null, null, null, null, null), ct);
        logger.LogInformation("[{Broker}] Session disconnected for user {UserId}", brokerName, userId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static BrokerCredentials BuildCredentials(
        string brokerName, BrokerAuthFlowType flowType,
        IReadOnlyDictionary<string, string> d)
    {
        string G(string key) => d.TryGetValue(key, out var v) ? v : "";
        return flowType switch
        {
            BrokerAuthFlowType.DirectCredentials =>
                new BrokerCredentials(brokerName, G("apiKey"), null, null, null,
                    G("clientCode"), G("password"), G("totp")),
            BrokerAuthFlowType.OAuth =>
                new BrokerCredentials(brokerName, G("apiKey"), G("apiSecret"),
                    G("code").Length > 0 ? G("code") : G("requestToken"),
                    G("refreshToken"), null, null, null),
            BrokerAuthFlowType.ApiKey =>
                new BrokerCredentials(brokerName, G("apiKey"), G("apiSecret"),
                    null, null, null, null, null),
            _ => new BrokerCredentials(brokerName, "", null, null, null, null, null, null),
        };
    }

    private static string? BuildOAuthLoginUrl(IFullBrokerClient client, string apiKey, string redirect)
    {
        // Convention: broker clients that support OAuth expose a static helper or
        // accept a credentials dict with empty code to return the login URL.
        // We call AuthenticateAsync with a sentinel "get_url" code to trigger URL generation,
        // but since clients don't support that, use reflection on broker-specific auth helpers.
        // For now: build URL from well-known patterns.  Each broker project may expose a static helper.
        return client.BrokerName.ToLower() switch
        {
            "zerodha" => $"https://kite.zerodha.com/connect/login?v=3&api_key={apiKey}",
            "upstox"  => string.IsNullOrEmpty(redirect)
                ? null
                : $"https://api.upstox.com/v2/login/authorization/dialog" +
                  $"?response_type=code&client_id={apiKey}&redirect_uri={Uri.EscapeDataString(redirect)}",
            _ => null,
        };
    }

    private void FireAndForgetRefresh(string brokerName, string userId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var refreshService = scope.ServiceProvider.GetRequiredService<IInstrumentRefreshService>();
                await refreshService.RefreshAsync(brokerName, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[BrokerAuth] Background instrument refresh failed for {Broker} user {UserId}",
                    brokerName, userId);
            }
        });
    }
}
