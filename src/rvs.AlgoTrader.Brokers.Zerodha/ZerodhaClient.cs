using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Brokers.Zerodha.Auth;

namespace rvs.AlgoTrader.Brokers.Zerodha;

public class ZerodhaOptions
{
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
}

public class ZerodhaClient(
    HttpClient http,
    ZerodhaAuth auth,
    IOptions<ZerodhaOptions> options) : IFullBrokerClient
{
    private readonly DateTimeZone _ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
    private string? _accessToken;
    private const string BaseUrl = "https://api.kite.trade";

    public string BrokerName => "Zerodha";

    private void SetAuthHeader()
    {
        if (!string.IsNullOrEmpty(_accessToken))
        {
            http.DefaultRequestHeaders.Remove("Authorization");
            http.DefaultRequestHeaders.Add("Authorization", $"token {options.Value.ApiKey}:{_accessToken}");
        }
    }

    public async Task<LoginResult> AuthenticateAsync(BrokerCredentials creds, CancellationToken ct)
    {
        var result = await auth.GenerateSessionAsync(creds, ct);
        if (result.Success && result.AccessToken != null)
        {
            _accessToken = result.AccessToken;
            SetAuthHeader();
        }
        return result;
    }

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct)
    {
        SetAuthHeader();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["tradingsymbol"] = request.BrokerToken,
            ["exchange"] = request.Exchange,
            ["transaction_type"] = request.Direction,
            ["order_type"] = request.OrderType,
            ["quantity"] = request.Quantity.ToString(),
            ["product"] = request.ProductType,
            ["price"] = request.Price?.ToString("F2") ?? "0",
            ["trigger_price"] = request.TriggerPrice?.ToString("F2") ?? "0",
            ["validity"] = "DAY",
            ["tag"] = request.IdempotencyKey[..Math.Min(20, request.IdempotencyKey.Length)]
        });

        var response = await http.PostAsync($"{BaseUrl}/orders/regular", form, ct);
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
        var dict = new Dictionary<string, string> { ["order_id"] = brokerId };
        if (mod.Quantity.HasValue) dict["quantity"] = mod.Quantity.Value.ToString();
        if (mod.Price.HasValue) dict["price"] = mod.Price.Value.ToString("F2");
        if (mod.TriggerPrice.HasValue) dict["trigger_price"] = mod.TriggerPrice.Value.ToString("F2");

        var form = new FormUrlEncodedContent(dict);
        var response = await http.PutAsync($"{BaseUrl}/orders/regular/{brokerId}", form, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return new OrderResult(false, brokerId, $"HTTP {response.StatusCode}: {json}", brokerId);

        return new OrderResult(true, brokerId, null, brokerId);
    }

    public async Task<bool> CancelOrderAsync(string brokerId, CancellationToken ct)
    {
        SetAuthHeader();
        var response = await http.DeleteAsync($"{BaseUrl}/orders/regular/{brokerId}", ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<BrokerOrder>> GetOrderBookAsync(CancellationToken ct)
    {
        SetAuthHeader();
        var response = await http.GetAsync($"{BaseUrl}/orders", ct);
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
                item.TryGetProperty("price", out var p) ? (decimal?)p.GetDecimal() : null,
                item.TryGetProperty("trigger_price", out var tp) && tp.GetDecimal() > 0 ? (decimal?)tp.GetDecimal() : null,
                item.GetProperty("status").GetString()!,
                DateTimeOffset.UtcNow
            ));
        }
        return orders;
    }

    public async Task<BrokerQuote> GetQuoteAsync(string brokerToken, CancellationToken ct)
    {
        SetAuthHeader();
        var response = await http.GetAsync($"{BaseUrl}/quote?i=NSE:{brokerToken}", ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        var d = doc.RootElement.GetProperty("data").GetProperty($"NSE:{brokerToken}");
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
        var url = $"{BaseUrl}/instruments/historical/{query.BrokerToken}/{query.Timeframe}?from={from}&to={to}";
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
        var response = await http.GetAsync($"{BaseUrl}/user/margins", ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        var equity = doc.RootElement.GetProperty("data").GetProperty("equity");
        return new AccountFunds("Zerodha",
            equity.GetProperty("available").GetProperty("cash").GetDecimal(),
            equity.GetProperty("utilised").GetProperty("debits").GetDecimal(),
            equity.GetProperty("net").GetDecimal(),
            SystemClock.Instance.GetCurrentInstant().InZone(_ist));
    }

    public async Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct)
    {
        SetAuthHeader();
        var response = await http.GetAsync($"{BaseUrl}/portfolio/positions", ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return [];
        var doc = JsonDocument.Parse(json);
        var positions = new List<BrokerPosition>();
        foreach (var item in doc.RootElement.GetProperty("data").GetProperty("day").EnumerateArray())
        {
            positions.Add(new BrokerPosition("Zerodha",
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
        var response = await http.GetAsync($"{BaseUrl}/portfolio/holdings", ct);
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
        // Zerodha WebSocket: wss://ws.kite.trade?api_key=xxx&access_token=yyy
        // Binary protocol: mode=full subscription, decode binary tick packets
        // Simplified implementation — production should use full binary decoder
        var wsUrl = $"wss://ws.kite.trade?api_key={options.Value.ApiKey}&access_token={_accessToken}";
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(wsUrl), ct);

        // Subscribe to tokens
        var tokens = brokerTokens.Select(t => int.TryParse(t, out var n) ? n : 0).Where(n => n > 0).ToArray();
        var subscribeMsg = JsonSerializer.Serialize(new { a = "subscribe", v = tokens });
        var modeMsg = JsonSerializer.Serialize(new { a = "mode", v = new object[] { "full", tokens } });
        await ws.SendAsync(System.Text.Encoding.UTF8.GetBytes(subscribeMsg), WebSocketMessageType.Text, true, ct);
        await ws.SendAsync(System.Text.Encoding.UTF8.GetBytes(modeMsg), WebSocketMessageType.Text, true, ct);

        var buffer = new byte[65536];
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Binary && result.Count >= 8)
            {
                // Simplified binary decoder — parse first packet only
                var count = (buffer[0] << 8) | buffer[1];
                var offset = 2;
                for (int i = 0; i < count && offset + 4 <= result.Count; i++)
                {
                    var packetLen = (buffer[offset] << 8) | buffer[offset + 1];
                    offset += 2;
                    if (offset + packetLen > result.Count) break;
                    if (packetLen >= 8)
                    {
                        var token = (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3];
                        var ltpRaw = (buffer[offset + 4] << 24) | (buffer[offset + 5] << 16) | (buffer[offset + 6] << 8) | buffer[offset + 7];
                        var ltp = ltpRaw / 100m;
                        yield return new BrokerTick(token.ToString(), ltp, 0,
                            SystemClock.Instance.GetCurrentInstant().InZone(_ist));
                    }
                    offset += packetLen;
                }
            }
        }
    }

    public async Task SubscribeAsync(IEnumerable<string> brokerTokens, CancellationToken ct)
        => await Task.CompletedTask; // Handled in StreamAsync

    public async Task UnsubscribeAsync(IEnumerable<string> brokerTokens, CancellationToken ct)
        => await Task.CompletedTask;

    public async Task<LatencyReport> MeasureLatencyAsync(CancellationToken ct)
    {
        var samples = new List<double>();
        for (int i = 0; i < 10; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await GetQuoteAsync("NIFTY 50", ct);
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }
        samples.Sort();
        return new LatencyReport("Zerodha",
            samples[samples.Count / 2], samples[(int)(samples.Count * 0.95)], samples[^1],
            samples.Count, SystemClock.Instance.GetCurrentInstant().InZone(_ist));
    }

    // ── Instrument Master ─────────────────────────────────────────────────────

    /// <summary>
    /// Zerodha publishes instrument master as a public CSV:
    /// https://api.kite.trade/instruments — no auth needed, refreshed daily before market open.
    /// Columns: instrument_token,exchange_token,tradingsymbol,name,last_price,expiry,strike,
    ///          tick_size,lot_size,instrument_type,segment,exchange
    /// </summary>
    public async Task<IReadOnlyList<InstrumentTokenMapping>> GetInstrumentMasterAsync(CancellationToken ct)
    {
        var results = new List<InstrumentTokenMapping>();
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "https://api.kite.trade/instruments");
            // Zerodha's instruments endpoint works without auth but respect rate limits
            var response = await http.SendAsync(req, ct);
            if (!response.IsSuccessStatusCode) return results;

            var csv = await response.Content.ReadAsStringAsync(ct);
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) return results;

            var headers = lines[0].Split(',');
            var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++)
                idx[headers[i].Trim().Trim('"')] = i;

            string Get(string[] cols, string key) =>
                idx.TryGetValue(key, out var i) && i < cols.Length ? cols[i].Trim().Trim('"') : string.Empty;

            for (int row = 1; row < lines.Length; row++)
            {
                var cols = lines[row].Split(',');
                if (cols.Length < 4) continue;
                var token  = Get(cols, "instrument_token");
                var symbol = Get(cols, "tradingsymbol");
                var exch   = Get(cols, "exchange").ToUpper();
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(symbol)) continue;
                results.Add(new InstrumentTokenMapping($"{exch}:{symbol}", token, exch, "Zerodha"));
            }
        }
        catch { /* fail silently */ }
        return results;
    }
}

// NOTE: GetInstrumentMasterAsync appended below to satisfy IBrokerInstrumentClient
