# Scaffolding: Broker Models & Interfaces — rvs.AlgoTrader

All broker abstractions live in `rvs.AlgoTrader.Brokers.Abstractions`.
Concrete implementations (`ZerodhaClient`, `UpstoxClient`, `MStockClient`) live in their
respective projects and implement `IFullBrokerClient`.

**Rule:** Application layer and Domain layer must NEVER reference any concrete broker client —
only `IBrokerOrderClient`, `IBrokerMarketDataClient`, `IBrokerAccountClient`, `IBrokerStreamClient`,
or `IFullBrokerClient` from Abstractions.

---

## Core Tick Model

```csharp
namespace rvs.AlgoTrader.Brokers.Abstractions;

/// <summary>
/// Raw tick data from broker WebSocket stream.
/// Immutable struct — high-frequency allocation path, kept as small as possible.
/// </summary>
public readonly record struct BrokerTick(
    string Symbol,             // broker token or internal symbol (normalized by adapter)
    decimal Ltp,               // Last Traded Price
    long Volume,               // cumulative volume for the day
    ZonedDateTime Timestamp    // NodaTime — never DateTime
);
```

---

## Order Request & Result

```csharp
namespace rvs.AlgoTrader.Brokers.Abstractions;

/// <summary>
/// Canonical order request sent to any broker via IBrokerOrderClient.
/// Broker-specific adapters translate this to the broker's own API format.
/// </summary>
public record OrderRequest(
    string InternalSymbol,
    string BrokerToken,        // broker-specific instrument token (from InstrumentTokenResolver)
    string OrderType,          // MARKET, LIMIT, SL, SL-M
    string Direction,          // BUY, SELL
    int Quantity,
    decimal? Price,            // null for MARKET orders
    decimal? TriggerPrice,     // required for SL and SL-M
    string Exchange,           // NSE, BSE
    string ProductType,        // MIS (intraday), CNC (delivery), NRML (F&O)
    string IdempotencyKey,     // UUID — checked by middleware before broker call
    Guid? StrategyRunId,
    string CorrelationId
);

/// <summary>
/// Canonical result returned by IBrokerOrderClient.PlaceOrderAsync.
/// Success = broker accepted the order (not yet filled).
/// BrokerOrderId is the broker's own order identifier used for modify/cancel.
/// </summary>
public record OrderResult(
    bool Success,
    string? BrokerOrderId,
    string? RejectionReason,   // populated if Success = false
    string CorrelationId
);

/// <summary>
/// Modification payload for IBrokerOrderClient.ModifyOrderAsync.
/// Only non-null fields are sent to the broker.
/// </summary>
public record OrderModification(
    int? Quantity,
    decimal? Price,
    decimal? TriggerPrice
);

/// <summary>
/// Broker's own representation of an order (from order book).
/// Mapped to internal OrderDto by Mapperly in the Application layer.
/// </summary>
public record BrokerOrder(
    string BrokerOrderId,
    string InternalSymbol,
    string OrderType,
    string Direction,
    int Quantity,
    int FilledQuantity,
    decimal? Price,
    decimal? TriggerPrice,
    string Status,             // broker-native status string, normalized in mapper
    DateTimeOffset PlacedAt
);
```

---

## Market Data Models

```csharp
namespace rvs.AlgoTrader.Brokers.Abstractions;

public record BrokerQuote(
    string InternalSymbol,
    decimal Ltp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    decimal BidPrice,
    decimal AskPrice,
    ZonedDateTime Timestamp
);

public record OhlcvBar(
    string InternalSymbol,
    string Timeframe,
    ZonedDateTime OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume
);

public record HistoricalDataQuery(
    string BrokerToken,
    string InternalSymbol,
    string Timeframe,
    DateOnly FromDate,
    DateOnly ToDate
);

public record MarketDepth(
    string InternalSymbol,
    IReadOnlyList<DepthLevel> Bids,
    IReadOnlyList<DepthLevel> Asks,
    ZonedDateTime Timestamp
);

public record DepthLevel(
    decimal Price,
    int Quantity,
    int Orders
);
```

---

## Account Models

```csharp
namespace rvs.AlgoTrader.Brokers.Abstractions;

public record AccountFunds(
    string BrokerName,
    decimal AvailableBalance,
    decimal UsedMargin,
    decimal TotalBalance,
    ZonedDateTime FetchedAt
);

public record BrokerPosition(
    string BrokerName,
    string InternalSymbol,
    int Quantity,
    decimal AveragePrice,
    decimal LastPrice,
    decimal PnL,
    string ProductType        // MIS, CNC, NRML
);

public record BrokerHolding(
    string InternalSymbol,
    int Quantity,
    decimal AveragePrice,
    decimal LastPrice,
    decimal PnL
);
```

---

## Authentication Models

```csharp
namespace rvs.AlgoTrader.Brokers.Abstractions;

public record BrokerCredentials(
    string BrokerName,
    string ApiKey,
    string? ApiSecret,
    string? RequestToken,      // Zerodha: from OAuth redirect
    string? RefreshToken       // Upstox only: for re-auth flow; mStock Type B has no refresh token
);

public record LoginResult(
    bool Success,
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    string? LoginUrl,          // Zerodha: URL for manual login redirect
    string? ErrorMessage
);

public record LatencyReport(
    string BrokerName,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    int SampleCount,
    ZonedDateTime MeasuredAt
);
```

---

## Interface Segregation

```csharp
namespace rvs.AlgoTrader.Brokers.Abstractions;

/// <summary>
/// Order placement, modification, and cancellation.
/// Used by LiveExecutionEngine — injected by IStrategyInstanceManager.
/// </summary>
public interface IBrokerOrderClient
{
    Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct);
    Task<OrderResult> ModifyOrderAsync(string brokerId, OrderModification mod, CancellationToken ct);
    Task<bool> CancelOrderAsync(string brokerId, CancellationToken ct);
    Task<IReadOnlyList<BrokerOrder>> GetOrderBookAsync(CancellationToken ct);
}

/// <summary>
/// Market data: quotes, historical bars, market depth.
/// Used by HistoricalDownloadService and real-time quote display.
/// NEVER used by IStrategy implementations — strategies read from ICandleCache only.
/// </summary>
public interface IBrokerMarketDataClient
{
    Task<BrokerQuote> GetQuoteAsync(string brokerToken, CancellationToken ct);
    Task<IReadOnlyList<OhlcvBar>> GetHistoricalDataAsync(HistoricalDataQuery query, CancellationToken ct);
    Task<MarketDepth> GetDepthAsync(string brokerToken, CancellationToken ct);
}

/// <summary>
/// Account funds, positions, and holdings.
/// Used by ICapitalAllocator, PositionReconciliationJob, and AccountSummary API.
/// </summary>
public interface IBrokerAccountClient
{
    Task<AccountFunds> GetFundsAsync(CancellationToken ct);
    Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct);
    Task<IReadOnlyList<BrokerHolding>> GetHoldingsAsync(CancellationToken ct);
}

/// <summary>
/// WebSocket streaming for real-time tick data.
/// Used exclusively by CandleAggregatorService and BrokerWebSocketHealthCheck.
/// Wrapped by ReconnectingBrokerStreamClient decorator.
/// </summary>
public interface IBrokerStreamClient
{
    IAsyncEnumerable<BrokerTick> StreamAsync(IEnumerable<string> brokerTokens, CancellationToken ct);
    Task SubscribeAsync(IEnumerable<string> brokerTokens, CancellationToken ct);
    Task UnsubscribeAsync(IEnumerable<string> brokerTokens, CancellationToken ct);
}

/// <summary>
/// Full broker client — implemented by ZerodhaClient, UpstoxClient, MStockClient.
/// Resolved by BrokerClientFactory by broker name from appsettings.
/// Wrapped by:
///   - SessionAwareBrokerClient (transparent token refresh decorator)
///   - ReconnectingBrokerStreamClient (WebSocket reconnect decorator)
/// </summary>
public interface IFullBrokerClient
    : IBrokerOrderClient, IBrokerMarketDataClient, IBrokerAccountClient, IBrokerStreamClient
{
    string BrokerName { get; }
    Task<LoginResult> AuthenticateAsync(BrokerCredentials creds, CancellationToken ct);
    Task<LatencyReport> MeasureLatencyAsync(CancellationToken ct);
}
```

---

## Decorator Pattern

```csharp
namespace rvs.AlgoTrader.Brokers.Abstractions;

/// <summary>
/// Transparent session token refresh decorator.
/// Wraps IFullBrokerClient — refreshes token on 401 before retrying.
/// Upstox: auto-refresh OAuth2 (expires 3:30 AM IST). mStock Type B: re-exchange session on 401/403 (no OAuth2). Zerodha: alert with login URL, block orders.
/// </summary>
public class SessionAwareBrokerClient : IFullBrokerClient
{
    private readonly IFullBrokerClient _inner;
    private readonly IBrokerSessionManager _sessionManager;

    // Intercepts 401 → refresh → retry once → re-throw if still failing
    // All auth events → audit_log via IAuditLogger
}

/// <summary>
/// Exponential backoff WebSocket reconnect decorator.
/// On disconnect: publishes StreamDisconnected event, reconnects with backoff,
/// re-subscribes all symbols, publishes StreamReconnected event.
/// </summary>
public class ReconnectingBrokerStreamClient : IBrokerStreamClient
{
    private readonly IBrokerStreamClient _inner;
    private readonly IPublishEndpoint _bus;

    // Reconnect delays: 1s → 2s → 4s → 8s → 16s → 30s (cap)
    // Re-subscribe: IEnumerable<string> maintained from last SubscribeAsync call
}
```

---

## BrokerClientFactory

```csharp
namespace rvs.AlgoTrader.Brokers.Abstractions;

/// <summary>
/// Resolves broker client by name from appsettings ActiveBroker or per-instance broker_name.
/// Returns SessionAwareBrokerClient-wrapped IFullBrokerClient.
/// </summary>
public interface IBrokerClientFactory
{
    IFullBrokerClient GetClient(string brokerName);
    IBrokerOrderClient GetOrderClient(string brokerName);
    IBrokerMarketDataClient GetMarketDataClient(string brokerName);
    IBrokerStreamClient GetStreamClient(string brokerName);
}
```

---

## DI Registration (Infrastructure)

```csharp
// In rvs.AlgoTrader.Infrastructure ServiceCollectionExtensions
services.AddHttpClient<ZerodhaClient>()
    .AddPolicyHandler(GetBrokerRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());

services.AddHttpClient<UpstoxClient>()
    .AddPolicyHandler(GetBrokerRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());

services.AddHttpClient<MStockClient>()
    .AddPolicyHandler(GetBrokerRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());

// Polly: 3× exponential backoff retry; circuit open after 5 consecutive failures
// Latency measured on every call → broker_latency_log → p50/p95/p99 Grafana dashboard

services.AddSingleton<IBrokerClientFactory, BrokerClientFactory>();
```

---

## Architecture Test (NetArchTest)

```csharp
// Must be enforced — blocks merge on violation
[Trait("Category", "Architecture")]
[Fact]
public void IStrategy_Must_Not_Depend_On_BrokerInterfaces()
{
    var result = Types.InAssembly(typeof(IStrategy).Assembly)
        .That().ImplementInterface(typeof(IStrategy))
        .ShouldNot().HaveDependencyOn("rvs.AlgoTrader.Brokers")
        .GetResult();

    result.IsSuccessful.Should().BeTrue();
}
```
