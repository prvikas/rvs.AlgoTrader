using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.DTOs.Broker;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Brokers.Upstox.Auth;
using rvs.AlgoTrader.Brokers.Zerodha.Auth;
using rvs.AlgoTrader.Domain.Constants;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Infrastructure implementation of IBrokerAuthService.
/// Authenticates with brokers, stores sessions, and triggers instrument master refresh
/// immediately after each successful login — mirroring OpenAlgo's on-login behaviour.
///
/// Authentication is performed through IBrokerClientFactory so the token is set on the
/// factory's cached client instance (the same instance used for all subsequent API calls).
/// Injecting MStockClient directly creates a separate transient instance that is discarded
/// after the request, leaving the factory's instance unauthenticated.
///
/// Background refresh uses IServiceScopeFactory to create a fresh DI scope so we don't
/// capture a disposed request scope (IInstrumentRefreshService is scoped).
/// </summary>
public class BrokerAuthService(
    IBrokerClientFactory brokerFactory,
    ZerodhaAuth zerodhaAuth,
    UpstoxAuth upstoxAuth,
    IAppBrokerSessionManager sessionManager,
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<BrokerAuthService> logger) : IBrokerAuthService
{
    // ── mStock Type B ─────────────────────────────────────────────────────────
    public async Task<BrokerAuthResultDto> AuthenticateMStockAsync(
        string apiKey, string clientCode, string password, string totp, CancellationToken ct)
    {
        // Use the factory's cached client so _jwtToken is set on the same instance
        // that HistoricalDownloadService, LiveExecutionEngine, etc. will use.
        var mStockClient = brokerFactory.GetClient(BrokerNames.MStock);
        var creds = new BrokerCredentials(BrokerNames.MStock, apiKey, null, null, null, clientCode, password, totp);
        var result = await mStockClient.AuthenticateAsync(creds, ct);

        if (result.Success)
        {
            await sessionManager.StoreSessionAsync(BrokerNames.MStock, result, ct);
            FireAndForgetRefresh(BrokerNames.MStock);
        }

        return new BrokerAuthResultDto(
            result.Success, BrokerNames.MStock,
            result.Success ? "Authenticated successfully" : result.ErrorMessage,
            result.ExpiresAt);
    }

    // ── Zerodha ───────────────────────────────────────────────────────────────
    public Task<string> GetZerodhaLoginUrlAsync(CancellationToken ct)
    {
        var apiKey = config["Broker:Zerodha:ApiKey"]
            ?? throw new InvalidOperationException("Broker:Zerodha:ApiKey not configured");
        return Task.FromResult($"https://kite.zerodha.com/connect/login?v=3&api_key={apiKey}");
    }

    public async Task<BrokerAuthResultDto> AuthenticateZerodhaAsync(string requestToken, CancellationToken ct)
    {
        var creds = new BrokerCredentials(
            BrokerNames.Zerodha,
            config["Broker:Zerodha:ApiKey"]!,
            config["Broker:Zerodha:ApiSecret"]!,
            requestToken, null, null, null, null);
        var result = await zerodhaAuth.GenerateSessionAsync(creds, ct);

        if (result.Success)
        {
            await sessionManager.StoreSessionAsync(BrokerNames.Zerodha, result, ct);
            // Inject the token directly into the factory's cached client instance so it's
            // immediately usable for market-data / order calls without requiring a restart.
            brokerFactory.GetClient(BrokerNames.Zerodha).RestoreToken(result.AccessToken!, null);
            FireAndForgetRefresh(BrokerNames.Zerodha);
        }

        return new BrokerAuthResultDto(
            result.Success, BrokerNames.Zerodha,
            result.Success ? "Authenticated successfully" : result.ErrorMessage,
            result.ExpiresAt);
    }

    // ── Upstox ────────────────────────────────────────────────────────────────
    public Task<string> GetUpstoxLoginUrlAsync(CancellationToken ct)
    {
        var apiKey   = config["Broker:Upstox:ApiKey"]
            ?? throw new InvalidOperationException("Broker:Upstox:ApiKey not configured");
        var redirect = config["Broker:Upstox:RedirectUri"]
            ?? throw new InvalidOperationException("Broker:Upstox:RedirectUri not configured");
        return Task.FromResult(upstoxAuth.GetLoginUrl(apiKey, redirect));
    }

    public async Task<BrokerAuthResultDto> AuthenticateUpstoxAsync(string authCode, CancellationToken ct)
    {
        var creds = new BrokerCredentials(
            BrokerNames.Upstox,
            config["Broker:Upstox:ApiKey"]!,
            config["Broker:Upstox:ApiSecret"]!,
            authCode, null, null, null, null);
        var redirect = config["Broker:Upstox:RedirectUri"]!;
        var result = await upstoxAuth.ExchangeCodeAsync(creds, redirect, ct);

        if (result.Success)
        {
            await sessionManager.StoreSessionAsync(BrokerNames.Upstox, result, ct);
            // Inject the token directly into the factory's cached client instance.
            brokerFactory.GetClient(BrokerNames.Upstox).RestoreToken(result.AccessToken!, null);
            FireAndForgetRefresh(BrokerNames.Upstox);
        }

        return new BrokerAuthResultDto(
            result.Success, BrokerNames.Upstox,
            result.Success ? "Authenticated successfully" : result.ErrorMessage,
            result.ExpiresAt);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts instrument master download in a background task using a new DI scope.
    /// This avoids ObjectDisposedException when the originating HTTP request scope is disposed
    /// before the background work completes (typically 10–60 s for large scrip masters).
    /// </summary>
    private void FireAndForgetRefresh(string brokerName)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Create an independent DI scope so we get fresh scoped services
                await using var scope = scopeFactory.CreateAsyncScope();
                var refreshService = scope.ServiceProvider.GetRequiredService<IInstrumentRefreshService>();
                await refreshService.RefreshAsync(brokerName, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[BrokerAuth] Background instrument refresh failed for {Broker}", brokerName);
            }
        });
    }
}
