# Skill: Broker Integration — AlgoTrader

## Purpose
Patterns and rules for implementing broker client adapters.
Load when writing ZerodhaClient, UpstoxClient, MStockClient, or any IBrokerXxxClient implementation.

---

## 🔑 OpenAlgo — Canonical Reference for All Broker API Details

**Repository:** https://github.com/marketcalls/openalgo

OpenAlgo is a Python-based open-source trading platform that has already solved broker integration
for the same three brokers (Zerodha/Kite, Upstox, mStock). Before writing ANY broker-specific code,
read the corresponding adapter in OpenAlgo to get the exact API contract right.

### Where to Look in OpenAlgo

| What you need | OpenAlgo path |
|---|---|
| Zerodha auth (request_token → access_token) | `openalgo/broker/zerodha/auth.py` |
| Upstox OAuth2 flow (code → token, refresh) | `openalgo/broker/upstox/auth.py` |
| mStock login + session management | `openalgo/broker/mstock/auth.py` |
| Zerodha order placement request shape | `openalgo/broker/zerodha/order.py` |
| Upstox order placement request shape | `openalgo/broker/upstox/order.py` |
| mStock order placement request shape | `openalgo/broker/mstock/order.py` |
| Zerodha historical data endpoint + params | `openalgo/broker/zerodha/data.py` |
| Upstox historical data endpoint + params | `openalgo/broker/upstox/data.py` |
| mStock historical data endpoint + params | `openalgo/broker/mstock/data.py` |
| Zerodha instrument master download (CSV) | `openalgo/broker/zerodha/master.py` |
| Upstox instrument master download (JSON) | `openalgo/broker/upstox/master.py` |
| mStock instrument master | `openalgo/broker/mstock/master.py` |
| Zerodha WebSocket subscribe message format | `openalgo/broker/zerodha/streaming.py` |
| Upstox protobuf WebSocket streaming | `openalgo/broker/upstox/streaming.py` |
| Position + holding endpoints | `openalgo/broker/*/api.py` |

### Translation Guide (Python → C#)

```python
# OpenAlgo Python — Zerodha order placement (reference)
def place_order(data):
    payload = {
        "tradingsymbol": data["symbol"],
        "exchange": data["exchange"],
        "transaction_type": data["action"].upper(),
        "order_type": data["pricetype"].upper(),
        "quantity": int(data["quantity"]),
        "product": map_product_type(data["product"]),
        "price": float(data.get("price", 0)),
        "trigger_price": float(data.get("trigger_price", 0)),
    }
    response = requests.post(f"{BASE_URL}/orders/regular",
        headers=get_auth_headers(), json=payload)
```

```csharp
// AlgoTrader C# equivalent — ZerodhaClient.PlaceOrderAsync
private record ZerodhaOrderPayload(
    [property: JsonPropertyName("tradingsymbol")] string TradingSymbol,
    [property: JsonPropertyName("exchange")] string Exchange,
    [property: JsonPropertyName("transaction_type")] string TransactionType,
    [property: JsonPropertyName("order_type")] string OrderType,
    [property: JsonPropertyName("quantity")] int Quantity,
    [property: JsonPropertyName("product")] string Product,
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("trigger_price")] decimal TriggerPrice
);

public async Task<OrderResult> PlaceOrderAsync(OrderRequest req, CancellationToken ct) =>
    await _latency.MeasureAsync("PlaceOrder", async () =>
    {
        var payload = new ZerodhaOrderPayload(
            TradingSymbol: req.BrokerToken,
            Exchange: req.Exchange,
            TransactionType: req.Direction,  // BUY / SELL
            OrderType: MapOrderType(req.OrderType),
            Quantity: req.Quantity,
            Product: MapProductType(req.ProductType),
            Price: req.Price ?? 0,
            TriggerPrice: req.TriggerPrice ?? 0
        );
        var response = await _http.PostAsJsonAsync("/orders/regular", payload, ct);
        // ... parse response
    }, ct);
```

### Key Broker-Specific Facts (from OpenAlgo)

**Zerodha (Kite Connect):**
- Auth: `POST https://api.kite.trade/session/token` with `api_key` + `request_token` + `checksum`
- Checksum = `SHA256(api_key + request_token + api_secret)` as hex string (no separators, UTF-8 encoded) ✓ **verified against official .NET SDK `dotnetkiteconnect`**
- Official .NET SDK: `zerodha/dotnetkiteconnect` on GitHub — use as reference for C# checksum generation; `Utils.SHA256Hash(apiKey + requestToken + appSecret)` is the exact call
- All API calls: `Authorization: token {api_key}:{access_token}` header
- Access token valid until midnight IST; no programmatic refresh — manual TOTP login required daily before market open
- Historical data: `GET /instruments/historical/{instrument_token}/{interval}` — max 60 days per chunk for minute data
- WebSocket: Binary frames (custom Kite binary protocol, NOT JSON); subscribe via text JSON `{"a": "subscribe", "v": [token1, token2]}`
- Instrument master: CSV download from `https://api.kite.trade/instruments` — refresh daily pre-market

**Upstox:**
- Auth: OAuth2 authorization_code flow → `POST /login/authorization/token` → `access_token` + optional extended token
- Token validity: `access_token` expires at **3:30 AM IST the following day** regardless of when it was generated (NOT "6h" — that was incorrect)
- Extended token: valid for 1 year from generation, read-only API access only, does NOT work for order placement
- All API calls: `Authorization: Bearer {access_token}` header
- On daily expiry: re-run full OAuth2 flow; no standard `refresh_token` grant — use `IBrokerSessionManager` to trigger re-auth alert
- Historical data: `GET /historical-candle/{instrument_key}/{interval}/{to_date}/{from_date}` — max 30 days per chunk
- WebSocket: Binary **protobuf** frames (Upstox `MarketDataFeed` proto schema); auth via `Authorization: Bearer {access_token}` header on WebSocket connect; prefer v3 WebSocket endpoint over v2

**mStock (Mirae Asset):**
- AlgoTrader uses the **Type B** API variant
- Auth: session exchange to obtain `jwtToken`; `POST /rest/login` was **incorrect** — verify exact Type B session endpoint from OpenAlgo source or mStock Type B docs
- All API calls: `Authorization: Bearer {jwtToken}` + `X-PrivateKey: {api_key}`
- Token refresh: re-run full session exchange when 401/403 received; no refresh_token
- Historical data: verify exact endpoint and chunk limit via OpenAlgo source (30 days assumed)
- WebSocket: JSON frames

---

---

## Broker Client Architecture

```
IBrokerOrderClient   ─┐
IBrokerMarketDataClient─┤─ IFullBrokerClient
IBrokerAccountClient ─┤     │
IBrokerStreamClient  ─┘     │
                             │
                    SessionAwareBrokerClient (decorator — token refresh)
                             │
                    ReconnectingBrokerStreamClient (decorator — WebSocket reconnect)
                             │
                    ZerodhaClient / UpstoxClient / MStockClient
```

**Factory:** `BrokerClientFactory.Resolve(brokerName)` returns decorated `IFullBrokerClient`

---

## Polly Policy Registration (REQUIRED on all broker HTTP clients)

```csharp
// Program.cs / infrastructure extension methods
services.AddHttpClient<ZerodhaClient>(client =>
{
    client.BaseAddress = new Uri(config["Brokers:Zerodha:BaseUrl"]!);
    client.Timeout = TimeSpan.FromSeconds(30); // outer timeout
})
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy())
.AddPolicyHandler(GetTimeoutPolicy());

private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
    HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: (attempt, outcome, _) =>
            {
                // Respect Retry-After header if present (broker 429 responses)
                if (outcome.Result?.Headers.RetryAfter?.Delta is { } delta)
                    return delta;
                return TimeSpan.FromSeconds(Math.Pow(2, attempt)); // exponential
            },
            onRetry: (outcome, timespan, attempt, _) =>
                Log.Warning("Broker retry {Attempt} after {Delay}ms. Status: {Status}",
                    attempt, timespan.TotalMilliseconds, outcome.Result?.StatusCode)
        );

private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy() =>
    HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromSeconds(30),
            onBreak: (_, _) =>
            {
                Log.Error("Circuit breaker OPEN — pausing all broker calls");
                // Publish BrokerCircuitOpenEvent → pause affected strategy instances
            },
            onReset: () => Log.Information("Circuit breaker CLOSED — broker calls resuming")
        );

private static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy() =>
    Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(10));
```

---

## Latency Measurement (Required on Every Call)

```csharp
// BrokerLatencyMiddleware — wrap every broker call
public async Task<T> MeasureAsync<T>(
    string operation,
    Func<Task<T>> call,
    CancellationToken ct)
{
    var sw = Stopwatch.StartNew();
    var sentAt = _clock.NowInstant();
    try
    {
        var result = await call();
        sw.Stop();
        
        await _latencyRepo.RecordAsync(new BrokerLatencyRecord(
            BrokerName: _brokerName,
            Operation: operation,
            RequestSentAt: sentAt,
            LatencyMs: (int)sw.ElapsedMilliseconds,
            RecordedAt: _clock.NowInstant()
        ), ct);
        
        return result;
    }
    catch
    {
        sw.Stop();
        // Record failed call latency too
        throw;
    }
}

// Usage in ZerodhaClient:
public async Task<OrderResult> PlaceOrderAsync(OrderRequest req, CancellationToken ct) =>
    await _latency.MeasureAsync("PlaceOrder", () => InternalPlaceOrderAsync(req, ct), ct);
```

---

## SessionAwareBrokerClient Decorator

```csharp
// Transparent token refresh before each call
public class SessionAwareBrokerClient : IFullBrokerClient
{
    private readonly IFullBrokerClient _inner;
    private readonly IBrokerSessionManager _sessionManager;
    private readonly string _brokerName;

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest req, CancellationToken ct)
    {
        await _sessionManager.EnsureValidSessionAsync(_brokerName, ct);
        try
        {
            return await _inner.PlaceOrderAsync(req, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Token expired mid-request — refresh and retry once
            await _sessionManager.RefreshAsync(_brokerName, ct);
            return await _inner.PlaceOrderAsync(req, ct);
        }
    }
    // Same pattern on all other methods
}
```

---

## ReconnectingBrokerStreamClient Decorator

```csharp
public class ReconnectingBrokerStreamClient : IBrokerStreamClient
{
    private readonly IBrokerStreamClient _inner;
    private readonly IPublisher _publisher;
    private int _reconnectAttempts = 0;
    private readonly int _maxReconnectDelaySeconds = 60;

    public async IAsyncEnumerable<BrokerTick> StreamAsync(
        IEnumerable<string> tokens, 
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await _publisher.Publish(new StreamDisconnectedEvent(BrokerName));
            
            var delay = Math.Min(Math.Pow(2, _reconnectAttempts), _maxReconnectDelaySeconds);
            await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            _reconnectAttempts++;
            
            Log.Warning("Reconnecting broker stream {Broker}, attempt {Attempt}, delay {Delay}s",
                BrokerName, _reconnectAttempts, delay);
            
            try
            {
                await foreach (var tick in _inner.StreamAsync(tokens, ct))
                {
                    _reconnectAttempts = 0; // Reset on successful tick
                    yield return tick;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Log.Error(ex, "Stream disconnected from {Broker}", BrokerName);
            }
        }
        
        await _publisher.Publish(new StreamReconnectedEvent(BrokerName));
    }
}
```

---

## Zerodha-Specific: Token Handling

```csharp
// Zerodha requires daily manual login (TOTP) — no auto-refresh
// On token expiry (401):
public async Task HandleTokenExpiryAsync(CancellationToken ct)
{
    var loginUrl = $"{_config.BaseUrl}/login?api_key={_apiKey}";
    
    await _alerts.SendAsync(new Alert(
        Level: AlertLevel.Critical,
        Category: AlertCategory.System,
        Message: $"Zerodha session expired. Login required: {loginUrl}",
        SentVia: ["IN_APP", "TELEGRAM", "EMAIL"]
    ), ct);
    
    // Block all Zerodha orders until resolved
    await _killSwitch.BlockBrokerAsync("Zerodha", ct);
    
    await _auditLog.LogAsync("TOKEN_EXPIRED", actor: "SYSTEM", entityType: "Broker", 
        entityId: "Zerodha", ct);
}

// After user completes login and pastes request_token:
public async Task<LoginResult> AuthenticateWithRequestTokenAsync(
    string requestToken, CancellationToken ct)
{
    // Exchange request_token for access_token via KiteConnect API
    // Store encrypted in Redis (AOF) + DB
    // Unblock broker orders
    await _auditLog.LogAsync("TOKEN_REFRESHED", actor: userId, entityType: "Broker", 
        entityId: "Zerodha", ct);
}
```

---

## Upstox WebSocket: Protobuf Streaming

```csharp
// Upstox v2 uses binary protobuf frames — NOT JSON
// Install: Google.Protobuf NuGet package
// Proto definition from Upstox API docs

public async IAsyncEnumerable<BrokerTick> StreamAsync(
    IEnumerable<string> tokens,
    [EnumeratorCancellation] CancellationToken ct)
{
    using var ws = new ClientWebSocket();
    ws.Options.SetRequestHeader("Authorization", $"Bearer {_accessToken}");
    await ws.ConnectAsync(new Uri(_wsUrl), ct);
    
    // Subscribe to instruments
    var subscribeMsg = new MarketDataFeedRequest
    {
        Data = new { mode = "full", instrumentKeys = tokens.ToList() }
    };
    await ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(subscribeMsg), 
        WebSocketMessageType.Text, true, ct);
    
    var buffer = new byte[4096];
    while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
    {
        var result = await ws.ReceiveAsync(buffer, ct);
        if (result.MessageType == WebSocketMessageType.Binary)
        {
            // Deserialize protobuf
            var feed = FeedResponse.Parser.ParseFrom(buffer.AsSpan(0, result.Count));
            foreach (var tick in ParseTicks(feed))
                yield return tick;
        }
    }
}
```

---

## Broker Rate Limit Configuration

```json
// appsettings.json — per broker, no secrets here
{
  "Brokers": {
    "Zerodha": {
      "Historical": {
        "MaxChunkDays": 60,
        "RateLimitPerSecond": 3,
        "MaxRetries": 5,
        "MaxParallelDownloads": 2
      }
    },
    "Upstox": {
      "Historical": {
        "MaxChunkDays": 30,
        "RateLimitPerSecond": 2,
        "MaxRetries": 5,
        "MaxParallelDownloads": 2
      }
    },
    "MStock": {
      "Historical": {
        "MaxChunkDays": 30,
        "RateLimitPerSecond": 2,
        "MaxRetries": 5,
        "MaxParallelDownloads": 1
      }
    }
  }
}
```

---

## Historical Data: OHLCV Normalization

```csharp
// Each broker returns different field names and formats
// Always normalize to internal Candle domain model

// Zerodha response:
// { date: "2024-01-15 09:15:00", open: 1234.5, high: 1250.0, low: 1230.0, close: 1245.0, volume: 12345 }

// Upstox response:
// { timestamp: "2024-01-15T09:15:00+05:30", open: 1234.5, high: 1250.0, low: 1230.0, close: 1245.0, volume: 12345 }

// Internal Candle domain object (ALWAYS use this):
public record Candle(
    string Symbol,
    string Timeframe,
    Instant Timestamp,   // Always UTC Instant for persistence
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume
)
{
    // Validation invariants
    public bool IsValid() => High >= Low && High >= Open && High >= Close 
                          && Low <= Open && Low <= Close && Volume >= 0;
}

// Normalization in broker adapter:
private Candle NormalizeZerodhaCandle(ZerodhaOhlcv raw, string symbol, string timeframe)
{
    // Parse IST time string → Instant
    var istTime = LocalDateTime.FromDateTime(DateTime.Parse(raw.Date))
        .InZoneLeniently(Ist).ToInstant();
    
    return new Candle(symbol, timeframe, istTime, raw.Open, raw.High, raw.Low, raw.Close, raw.Volume);
}
```

---

## Testing Broker Adapters

```csharp
// Integration test pattern — mock broker HTTP server
public class ZerodhaClientIntegrationTests
{
    private readonly WireMockServer _server;
    
    [Fact]
    public async Task PlaceOrderAsync_WhenBrokerReturns429_RetryWithBackoff()
    {
        // Arrange
        _server.Given(Request.Create().WithPath("/orders/regular").UsingPost())
            .InScenario("RateLimit")
            .WillSetStateTo("after-429")
            .RespondWith(Response.Create().WithStatusCode(429)
                .WithHeader("Retry-After", "2"));
        
        _server.Given(Request.Create().WithPath("/orders/regular").UsingPost())
            .InScenario("RateLimit")
            .WhenStateIs("after-429")
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { order_id = "123" }));
        
        // Act
        var result = await _client.PlaceOrderAsync(new OrderRequest(...), CancellationToken.None);
        
        // Assert
        Assert.Equal("123", result.BrokerOrderId);
        Assert.Equal(2, _server.LogEntries.Count()); // 1 retry happened
    }
}
```
