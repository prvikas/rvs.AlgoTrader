using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Brokers.Upstox.Auth;

namespace rvs.AlgoTrader.Brokers.Upstox;

public class UpstoxOptions
{
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "http://localhost:5000/auth/upstox/callback";
}

public class UpstoxClient(
    HttpClient http,
    UpstoxAuth auth,
    IOptions<UpstoxOptions> options) : IFullBrokerClient
{
    private readonly DateTimeZone _ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
    private string? _accessToken;
    private const string BaseUrl = "https://api.upstox.com/v2";

    public string BrokerName => "Upstox";

    private void SetAuthHeader()
    {
        if (!string.IsNullOrEmpty(_accessToken))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
    }

    public async Task<LoginResult> AuthenticateAsync(BrokerCredentials creds, CancellationToken ct)
    {
        LoginResult result;
        if (!string.IsNullOrEmpty(creds.RefreshToken))
            result = await auth.RefreshTokenAsync(creds.RefreshToken, creds.ApiKey, creds.ApiSecret ?? "", ct);
        else if (!string.IsNullOrEmpty(creds.RequestToken))
            result = await auth.ExchangeCodeAsync(creds, options.Value.RedirectUri, ct);
        else
        {
            var loginUrl = auth.GetLoginUrl(creds.ApiKey, options.Value.RedirectUri);
            return new LoginResult(false, null, null, null, loginUrl, "Authorization code required", null);
        }

        if (result.Success)
        {
            _accessToken = result.AccessToken;
            SetAuthHeader();
        }
        return result;
    }

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct)
    {
        SetAuthHeader();
        var body = JsonSerializer.Serialize(new
        {
            quantity = request.Quantity,
            product = request.ProductType,
            validity = "DAY",
            price = request.Price ?? 0,
            tag = request.IdempotencyKey[..Math.Min(20, request.IdempotencyKey.Length)],
            instrument_token = $"{request.Exchange}_EQ|{request.BrokerToken}",
            order_type = request.OrderType,
            transaction_type = request.Direction,
            disclosed_quantity = 0,
            trigger_price = request.TriggerPrice ?? 0,
            is_amo = false
        });

        var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var response = await http.PostAsync($"{BaseUrl}/order/place", content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return new OrderResult(false, null, $"HTTP {response.StatusCode}: {json}", request.CorrelationId);

        var doc = JsonDocument.Parse(json);
        var orderId = doc.RootElement.GetProperty("data").GetProperty("order_id").GetString()!;
        return new OrderResult(true, orderId, null, request.CorrelationId);
    }

    public async Task<OrderResult> ModifyOrderAsync(string brokerId, OrderModification mod, CancellationToken ct)
    {
        SetAuthHeader();
        var body = JsonSerializer.Serialize(new
        {
            order_id = brokerId,
            quantity = mod.Quantity,
            price = mod.Price,
            trigger_price = mod.TriggerPrice,
            validity = "DAY"
        });
        var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var response = await http.PutAsync($"{BaseUrl}/order/modify", content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return new OrderResult(false, brokerId, $"HTTP {response.StatusCode}: {json}", brokerId);
        return new OrderResult(true, brokerId, null, brokerId);
    }

    public async Task<bool> CancelOrderAsync(string brokerId, CancellationToken ct)
    {
        SetAuthHeader();
        var response = await http.DeleteAsync($"{BaseUrl}/order/cancel?order_id={brokerId}", ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<BrokerOrder>> GetOrderBookAsync(CancellationToken ct)
    {
        SetAuthHeader();
        var response = await http.GetAsync($"{BaseUrl}/order/retrieve-all", ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return [];
        var doc = JsonDocument.Parse(json);
        var orders = new List<BrokerOrder>();
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            orders.Add(new BrokerOrder(
                item.GetProperty("order_id").GetString()!,
                item.GetProperty("tradingsymbol").GetString()!,
                item.GetProperty("order_type").GetString()!,
                item.GetProperty("transaction_type").GetString()!,
                item.GetProperty("quantity").GetInt32(),
                item.GetProperty("filled_quantity").GetInt32(),
                item.TryGetProperty("price", out var p) && p.GetDecimal() > 0 ? (decimal?)p.GetDecimal() : null,
                item.TryGetProperty("trigger_price", out var tp) && tp.GetDecimal() > 0 ? (decimal?)tp.GetDecimal() : null,
                item.GetProperty("status").GetString()!,
                DateTimeOffset.UtcNow));
        }
        return orders;
    }

    public async Task<BrokerQuote> GetQuoteAsync(string brokerToken, CancellationToken ct)
    {
        SetAuthHeader();
        var response = await http.GetAsync($"{BaseUrl}/market-quote/quotes?instrument_key=NSE_EQ|{brokerToken}", ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        var d = doc.RootElement.GetProperty("data").EnumerateObject().First().Value;
        var now = SystemClock.Instance.GetCurrentInstant().InZone(_ist);
        return new BrokerQuote(brokerToken, d.GetProperty("last_price").GetDecimal(),
            d.GetProperty("ohlc").GetProperty("open").GetDecimal(),
            d.GetProperty("ohlc").GetProperty("high").GetDecimal(),
            d.GetProperty("ohlc").GetProperty("low").GetDecimal(),
            d.GetProperty("ohlc").GetProperty("close").GetDecimal(),
            d.GetProperty("volume").GetInt64(), 0, 0, now);
    }

    public async Task<IReadOnlyList<OhlcvBar>> GetHistoricalDataAsync(HistoricalDataQuery query, CancellationToken ct)
    {
        SetAuthHeader();
        var from = query.FromDate.ToString("yyyy-MM-dd");
        var to = query.ToDate.ToString("yyyy-MM-dd");
        var url = $"{BaseUrl}/historical-candle/NSE_EQ|{query.BrokerToken}/{query.Timeframe}/{to}/{from}";
        var response = await http.GetAsync(url, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return [];

        var doc = JsonDocument.Parse(json);
        var bars = new List<OhlcvBar>();
        foreach (var c in doc.RootElement.GetProperty("data").GetProperty("candles").EnumerateArray())
        {
            var ts = Instant.FromDateTimeUtc(DateTime.Parse(c[0].GetString()!).ToUniversalTime()).InZone(_ist);
            bars.Add(new OhlcvBar(query.InternalSymbol, query.Timeframe, ts,
                c[1].GetDecimal(), c[2].GetDecimal(), c[3].GetDecimal(), c[4].GetDecimal(), c[5].GetInt64()));
        }
        return bars;
    }

    public async Task<MarketDepth> GetDepthAsync(string brokerToken, CancellationToken ct)
    {
        var quote = await GetQuoteAsync(brokerToken, ct);
        return new MarketDepth(brokerToken, [], [], quote.Timestamp);
    }

    public async Task<AccountFunds> GetFundsAsync(CancellationToken ct)
    {
        SetAuthHeader();
        var response = await http.GetAsync($"{BaseUrl}/user/get-funds-and-margin?segment=SEC", ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        var d = doc.RootElement.GetProperty("data").GetProperty("equity");
        return new AccountFunds("Upstox",
            d.GetProperty("available_margin").GetDecimal(),
            d.GetProperty("used_margin").GetDecimal(),
            d.GetProperty("net").GetDecimal(),
            SystemClock.Instance.GetCurrentInstant().InZone(_ist));
    }

    public async Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct)
    {
        SetAuthHeader();
        var response = await http.GetAsync($"{BaseUrl}/portfolio/short-term-positions", ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return [];
        var doc = JsonDocument.Parse(json);
        var positions = new List<BrokerPosition>();
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            positions.Add(new BrokerPosition("Upstox",
                item.GetProperty("tradingsymbol").GetString()!,
                item.GetProperty("quantity").GetInt32(),
                item.GetProperty("average_price").GetDecimal(),
                item.GetProperty("last_price").GetDecimal(),
                item.GetProperty("pnl").GetDecimal(),
                item.GetProperty("product").GetString()!));
        }
        return positions;
    }

    public async Task<IReadOnlyList<BrokerHolding>> GetHoldingsAsync(CancellationToken ct)
    {
        SetAuthHeader();
        var response = await http.GetAsync($"{BaseUrl}/portfolio/long-term-holdings", ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return [];
        var doc = JsonDocument.Parse(json);
        var holdings = new List<BrokerHolding>();
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            holdings.Add(new BrokerHolding(
                item.GetProperty("tradingsymbol").GetString()!,
                item.GetProperty("quantity").GetInt32(),
                item.GetProperty("average_price").GetDecimal(),
                item.GetProperty("last_price").GetDecimal(),
                item.GetProperty("pnl").GetDecimal()));
        }
        return holdings;
    }

    public async IAsyncEnumerable<BrokerTick> StreamAsync(
        IEnumerable<string> brokerTokens,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Upstox v3 WebSocket: wss://api.upstox.com/v3/feed/market-data-streamer
        // Binary protobuf MarketDataFeed — simplified implementation
        SetAuthHeader();
        var wsUrl = "wss://api.upstox.com/v3/feed/market-data-streamer";
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", $"Bearer {_accessToken}");
        await ws.ConnectAsync(new Uri(wsUrl), ct);

        var tokens = brokerTokens.ToList();
        var subscribeMsg = JsonSerializer.Serialize(new
        {
            guid = Guid.NewGuid().ToString(),
            method = "sub",
            data = new { mode = "full", instrumentKeys = tokens.Select(t => $"NSE_EQ|{t}").ToArray() }
        });
        await ws.SendAsync(System.Text.Encoding.UTF8.GetBytes(subscribeMsg), WebSocketMessageType.Text, true, ct);

        var buffer = new byte[65536];
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Binary)
            {
                // Protobuf decode — simplified: yield placeholder tick
                // Production: use Google.Protobuf to decode MarketDataFeed proto
                var now = SystemClock.Instance.GetCurrentInstant().InZone(_ist);
                foreach (var token in tokens)
                    yield return new BrokerTick(token, 0m, 0, now);
            }
        }
    }

    public async Task SubscribeAsync(IEnumerable<string> brokerTokens, CancellationToken ct)
        => await Task.CompletedTask;

    public async Task UnsubscribeAsync(IEnumerable<string> brokerTokens, CancellationToken ct)
        => await Task.CompletedTask;

    public async Task<LatencyReport> MeasureLatencyAsync(CancellationToken ct)
    {
        var samples = new List<double>();
        for (int i = 0; i < 10; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await GetFundsAsync(ct);
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }
        samples.Sort();
        return new LatencyReport("Upstox",
            samples[samples.Count / 2], samples[(int)(samples.Count * 0.95)], samples[^1],
            samples.Count, SystemClock.Instance.GetCurrentInstant().InZone(_ist));
    }

    // ── Instrument Master ─────────────────────────────────────────────────────

    /// <summary>
    /// Upstox provides instrument master as a JSON/CSV file per exchange.
    /// Endpoint: GET https://assets.upstox.com/market-quote/instruments/exchange/{exchange}.json.gz
    /// Alternatively: https://api.upstox.com/v2/instruments/exchanges/{exchange}
    /// Fields: instrument_key (e.g. NSE_EQ|INE001A01036), name, exchange, trading_symbol, lot_size, expiry, strike_price, option_type
    /// </summary>
    public async Task<IReadOnlyList<InstrumentTokenMapping>> GetInstrumentMasterAsync(CancellationToken ct)
    {
        var results = new List<InstrumentTokenMapping>();
        foreach (var exchange in new[] { "NSE", "BSE", "NFO", "BFO", "MCX", "CDS" })
        {
            try
            {
                // Upstox publishes JSON instrument files publicly
                var url = $"https://assets.upstox.com/market-quote/instruments/exchange/{exchange.ToLower()}.json.gz";
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                var response = await http.SendAsync(req, ct);
                if (!response.IsSuccessStatusCode) continue;

                // Upstox returns gzip-compressed JSON
                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var gzip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
                var doc = await JsonDocument.ParseAsync(gzip, cancellationToken: ct);

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var instrKey    = item.TryGetProperty("instrument_key",  out var k)  ? k.GetString()  ?? "" : "";
                    var tradingSym  = item.TryGetProperty("trading_symbol",  out var ts) ? ts.GetString() ?? "" : "";
                    var exch        = item.TryGetProperty("exchange",        out var ex) ? ex.GetString() ?? "" : exchange;

                    if (string.IsNullOrEmpty(instrKey) || string.IsNullOrEmpty(tradingSym)) continue;
                    results.Add(new InstrumentTokenMapping($"{exch}:{tradingSym}", instrKey, exch.ToUpper(), "Upstox"));
                }
            }
            catch { /* fail silently per exchange */ }
        }
        return results;
    }
}
