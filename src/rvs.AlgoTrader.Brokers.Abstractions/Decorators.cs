using Microsoft.Extensions.Logging;
using MassTransit;
using NodaTime;
using rvs.AlgoTrader.Domain.Events;
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.Brokers.Abstractions;

/// <summary>
/// Transparent session token refresh decorator.
/// Wraps IFullBrokerClient — refreshes token on 401 before retrying.
/// Upstox: auto-refresh OAuth2 (expires 3:30 AM IST). mStock Type B: re-exchange session on 401/403. Zerodha: alert with login URL, block orders.
/// </summary>
public class SessionAwareBrokerClient(
    IFullBrokerClient inner,
    IBrokerSessionManager sessionManager,
    ILogger<SessionAwareBrokerClient> logger) : IFullBrokerClient
{
    public string BrokerName             => inner.BrokerName;
    public string Market                 => inner.Market;
    public Domain.Enums.BrokerAuthFlowType AuthFlowType => inner.AuthFlowType;

    public async Task<LoginResult> AuthenticateAsync(BrokerCredentials creds, CancellationToken ct)
        => await inner.AuthenticateAsync(creds, ct);

    public void RestoreToken(string accessToken, string? feedToken = null)
        => inner.RestoreToken(accessToken, feedToken);

    public async Task<LatencyReport> MeasureLatencyAsync(CancellationToken ct)
        => await inner.MeasureLatencyAsync(ct);

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct)
    {
        var result = await inner.PlaceOrderAsync(request, ct);
        if (!result.Success && IsAuthError(result.RejectionReason))
        {
            logger.LogWarning("[{Broker}] Auth error on PlaceOrder — attempting session refresh", BrokerName);
            await sessionManager.RefreshSessionAsync(BrokerName, ct);
            result = await inner.PlaceOrderAsync(request, ct);
        }
        return result;
    }

    public async Task<OrderResult> ModifyOrderAsync(string brokerId, OrderModification mod, CancellationToken ct)
    {
        var result = await inner.ModifyOrderAsync(brokerId, mod, ct);
        if (!result.Success && IsAuthError(result.RejectionReason))
        {
            await sessionManager.RefreshSessionAsync(BrokerName, ct);
            result = await inner.ModifyOrderAsync(brokerId, mod, ct);
        }
        return result;
    }

    public async Task<bool> CancelOrderAsync(string brokerId, CancellationToken ct)
        => await inner.CancelOrderAsync(brokerId, ct);

    public async Task<IReadOnlyList<BrokerOrder>> GetOrderBookAsync(CancellationToken ct)
        => await inner.GetOrderBookAsync(ct);

    public async Task<BrokerQuote> GetQuoteAsync(string brokerToken, CancellationToken ct)
        => await inner.GetQuoteAsync(brokerToken, ct);

    public async Task<IReadOnlyDictionary<string, OptionQuote>> GetOptionQuotesAsync(
        IEnumerable<string> brokerTokens, CancellationToken ct)
        => await inner.GetOptionQuotesAsync(brokerTokens, ct);

    public async Task<IReadOnlyList<OhlcvBar>> GetHistoricalDataAsync(HistoricalDataQuery query, CancellationToken ct)
        => await inner.GetHistoricalDataAsync(query, ct);

    public async Task<MarketDepth> GetDepthAsync(string brokerToken, CancellationToken ct)
        => await inner.GetDepthAsync(brokerToken, ct);

    public async Task<IReadOnlyList<InstrumentTokenMapping>> GetInstrumentMasterAsync(CancellationToken ct)
        => await inner.GetInstrumentMasterAsync(ct);

    public async Task<AccountFunds> GetFundsAsync(CancellationToken ct)
        => await inner.GetFundsAsync(ct);

    public async Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct)
        => await inner.GetPositionsAsync(ct);

    public async Task<IReadOnlyList<BrokerHolding>> GetHoldingsAsync(CancellationToken ct)
        => await inner.GetHoldingsAsync(ct);

    public IAsyncEnumerable<BrokerTick> StreamAsync(IEnumerable<string> brokerTokens, CancellationToken ct)
        => inner.StreamAsync(brokerTokens, ct);

    public async Task SubscribeAsync(IEnumerable<string> brokerTokens, CancellationToken ct)
        => await inner.SubscribeAsync(brokerTokens, ct);

    public async Task UnsubscribeAsync(IEnumerable<string> brokerTokens, CancellationToken ct)
        => await inner.UnsubscribeAsync(brokerTokens, ct);

    private static bool IsAuthError(string? reason)
        => reason is not null && (reason.Contains("401") || reason.Contains("403") || reason.Contains("auth", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Exponential backoff WebSocket reconnect decorator.
/// On disconnect: publishes StreamDisconnected, reconnects, re-subscribes, publishes StreamReconnected.
/// Reconnect delays: 1s → 2s → 4s → 8s → 16s → 30s (cap).
/// </summary>
public class ReconnectingBrokerStreamClient(
    IBrokerStreamClient inner,
    IPublishEndpoint bus,
    IClock clock,
    ILogger<ReconnectingBrokerStreamClient> logger,
    string brokerName) : IBrokerStreamClient
{
    private IEnumerable<string> _subscribedTokens = [];
    private static readonly int[] BackoffSeconds = [1, 2, 4, 8, 16, 30];

    public async IAsyncEnumerable<BrokerTick> StreamAsync(
        IEnumerable<string> brokerTokens,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        _subscribedTokens = brokerTokens.ToList();
        int attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            await foreach (var tick in inner.StreamAsync(_subscribedTokens, ct))
            {
                attempt = 0; // reset on successful tick
                yield return tick;
            }

            if (ct.IsCancellationRequested) break;

            var delaySeconds = BackoffSeconds[Math.Min(attempt, BackoffSeconds.Length - 1)];
            logger.LogWarning("[{Broker}] Stream disconnected — reconnecting in {Delay}s (attempt {Attempt})", brokerName, delaySeconds, attempt + 1);

            await bus.Publish(new StreamDisconnected(
                brokerName,
                "CONNECTION_LOST",
                attempt + 1,
                Guid.NewGuid().ToString(),
                clock.NowInstant().InZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"])), ct);

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
            attempt++;

            try
            {
                await inner.SubscribeAsync(_subscribedTokens, ct);
                await bus.Publish(new StreamReconnected(
                    brokerName,
                    delaySeconds * attempt,
                    _subscribedTokens.ToList().AsReadOnly(),
                    Guid.NewGuid().ToString(),
                    clock.NowInstant().InZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"])), ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{Broker}] Re-subscribe failed", brokerName);
            }
        }
    }

    public async Task SubscribeAsync(IEnumerable<string> brokerTokens, CancellationToken ct)
    {
        _subscribedTokens = brokerTokens.ToList();
        await inner.SubscribeAsync(_subscribedTokens, ct);
    }

    public async Task UnsubscribeAsync(IEnumerable<string> brokerTokens, CancellationToken ct)
        => await inner.UnsubscribeAsync(brokerTokens, ct);
}
