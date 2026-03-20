using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.DTOs.Broker;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Brokers.MStock;
using rvs.AlgoTrader.Brokers.Upstox.Auth;
using rvs.AlgoTrader.Brokers.Zerodha.Auth;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Infrastructure implementation of IBrokerAuthService.
/// Authenticates with brokers, stores sessions, and triggers instrument master refresh
/// immediately after each successful login — mirroring OpenAlgo's on-login behaviour.
///
/// Background refresh uses IServiceScopeFactory to create a fresh DI scope so we don't
/// capture a disposed request scope (IInstrumentRefreshService is scoped).
/// </summary>
public class BrokerAuthService(
    MStockClient mStockClient,
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
        var creds = new BrokerCredentials("MStock", apiKey, null, null, null, clientCode, password, totp);
        var result = await mStockClient.AuthenticateAsync(creds, ct);

        if (result.Success)
        {
            await sessionManager.StoreSessionAsync("MStock", result, ct);
            FireAndForgetRefresh("MStock");
        }

        return new BrokerAuthResultDto(
            result.Success, "MStock",
            result.Success ? "Authenticated successfully" : result.ErrorMessage,
            result.ExpiresAt);
    }

    // ── Zerodha ───────────────────────────────────────────────────────────────
    public Task<string> GetZerodhaLoginUrlAsync(CancellationToken ct)
    {
        var apiKey = config["Brokers:Zerodha:ApiKey"]
            ?? throw new InvalidOperationException("Brokers:Zerodha:ApiKey not configured");
        return Task.FromResult($"https://kite.zerodha.com/connect/login?v=3&api_key={apiKey}");
    }

    public async Task<BrokerAuthResultDto> AuthenticateZerodhaAsync(string requestToken, CancellationToken ct)
    {
        var creds = new BrokerCredentials(
            "Zerodha",
            config["Brokers:Zerodha:ApiKey"]!,
            config["Brokers:Zerodha:ApiSecret"]!,
            requestToken, null, null, null, null);
        var result = await zerodhaAuth.GenerateSessionAsync(creds, ct);

        if (result.Success)
        {
            await sessionManager.StoreSessionAsync("Zerodha", result, ct);
            FireAndForgetRefresh("Zerodha");
        }

        return new BrokerAuthResultDto(
            result.Success, "Zerodha",
            result.Success ? "Authenticated successfully" : result.ErrorMessage,
            result.ExpiresAt);
    }

    // ── Upstox ────────────────────────────────────────────────────────────────
    public Task<string> GetUpstoxLoginUrlAsync(CancellationToken ct)
    {
        var apiKey   = config["Brokers:Upstox:ApiKey"]
            ?? throw new InvalidOperationException("Brokers:Upstox:ApiKey not configured");
        var redirect = config["Brokers:Upstox:RedirectUri"]
            ?? throw new InvalidOperationException("Brokers:Upstox:RedirectUri not configured");
        return Task.FromResult(upstoxAuth.GetLoginUrl(apiKey, redirect));
    }

    public async Task<BrokerAuthResultDto> AuthenticateUpstoxAsync(string authCode, CancellationToken ct)
    {
        var creds = new BrokerCredentials(
            "Upstox",
            config["Brokers:Upstox:ApiKey"]!,
            config["Brokers:Upstox:ApiSecret"]!,
            authCode, null, null, null, null);
        var redirect = config["Brokers:Upstox:RedirectUri"]!;
        var result = await upstoxAuth.ExchangeCodeAsync(creds, redirect, ct);

        if (result.Success)
        {
            await sessionManager.StoreSessionAsync("Upstox", result, ct);
            FireAndForgetRefresh("Upstox");
        }

        return new BrokerAuthResultDto(
            result.Success, "Upstox",
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
