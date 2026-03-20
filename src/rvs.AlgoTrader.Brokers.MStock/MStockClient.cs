using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Brokers.MStock.Auth;

namespace rvs.AlgoTrader.Brokers.MStock;

public class MStockOptions
{
    public string ApiKey { get; set; } = "";
    public string PrivateKey { get; set; } = "";
    public string ClientCode { get; set; } = "";
    public string Password { get; set; } = "";
    // Note: TOTP is NOT stored here — it must be submitted interactively at login time
}

public class MStockClient(
    HttpClient http,
    MStockAuth auth,
    IOptions<MStockOptions> options) : IFullBrokerClient
{
    private readonly DateTimeZone _ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
    private string? _jwtToken;
    private string? _feedToken;
    private const string BaseUrl = "https://api.mstock.trade/openapi/typeb";

    public string BrokerName => "MStock";

    private void SetAuthHeaders(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_jwtToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken);
        request.Headers.Add("X-PrivateKey", options.Value.ApiKey);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, $"{BaseUrl}{path}");
        SetAuthHeaders(req);
        return req;
    }

    public async Task<LoginResult> AuthenticateAsync(BrokerCredentials creds, CancellationToken ct)
    {
        var result = await auth.ExchangeSessionAsync(creds, ct);
        if (result.Success && result.AccessToken != null)
        {
            _jwtToken = result.AccessToken;
            _feedToken = result.FeedToken;
        }
        return result;
    }

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct)
    {
        var req = CreateRequest(HttpMethod.Post, "/v1/orders");
        req.Content = new StringContent(JsonSerializer.Serialize(new
        {
            tradingsymbol = request.BrokerToken,
            exchange = request.Exchange,
            transaction_type = request.Direction,
            order_type = request.OrderType,
            quantity = request.Quantity,
            product = request.ProductType,
            price = request.Price ?? 0,
            trigger_price = request.TriggerPrice ?? 0,
            validity = "DAY"
        }), System.Text.Encoding.UTF8, "application/json");

        var response = await http.SendAsync(req, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return new OrderResult(false, null, $"HTTP {response.StatusCode}: {json}", request.CorrelationId);

        var doc = JsonDocument.Parse(json);
        var orderId = doc.RootElement.GetProperty("data").GetProperty("order_id").GetString()!;
        return new OrderResult(true, orderId, null, request.CorrelationId);
    }

    public async Task<OrderResult> ModifyOrderAsync(string brokerId, OrderModification mod, CancellationToken ct)
    {
        var req = CreateRequest(HttpMethod.Put, $"/v1/orders/{brokerId}");
        req.Content = new StringContent(JsonSerializer.Serialize(new
        {
            quantity = mod.Quantity,
            price = mod.Price,
            trigger_price = mod.TriggerPrice
        }), System.Text.Encoding.UTF8, "application/json");
        var response = await http.SendAsync(req, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return new OrderResult(false, brokerId, $"HTTP {response.StatusCode}: {json}", brokerId);
        return new OrderResult(true, brokerId, null, brokerId);
    }

    public async Task<bool> CancelOrderAsync(string brokerId, CancellationToken ct)
    {
        var req = CreateRequest(HttpMethod.Delete, $"/v1/orders/{brokerId}");
        var response = await http.SendAsync(req, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<BrokerOrder>> GetOrderBookAsync(CancellationToken ct)
    {
        var req = CreateRequest(HttpMethod.Get, "/v1/orders");
        var response = await http.SendAsync(req, ct);
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
                item.TryGetProperty("filled_quantity", out var fq) ? fq.GetInt32() : 0,
                item.TryGetProperty("price", out var p) && p.GetDecimal() > 0 ? (decimal?)p.GetDecimal() : null,
                item.TryGetProperty("trigger_price", out var tp) && tp.GetDecimal() > 0 ? (decimal?)tp.GetDecimal() : null,
                item.GetProperty("status").GetString()!,
                DateTimeOffset.UtcNow));
        }
        return orders;
    }

    public async Task<BrokerQuote> GetQuoteAsync(string brokerToken, CancellationToken ct)
    {
        var req = CreateRequest(HttpMethod.Get, $"/v1/quote?symbol={brokerToken}");
        var response = await http.SendAsync(req, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        var d = doc.RootElement.GetProperty("data");
        var now = SystemClock.Instance.GetCurrentInstant().InZone(_ist);
        return new BrokerQuote(brokerToken, d.GetProperty("ltp").GetDecimal(),
            d.GetProperty("open").GetDecimal(), d.GetProperty("high").GetDecimal(),
            d.GetProperty("low").GetDecimal(), d.GetProperty("close").GetDecimal(),
            d.GetProperty("volume").GetInt64(), 0, 0, now);
    }

    public async Task<IReadOnlyList<OhlcvBar>> GetHistoricalDataAsync(HistoricalDataQuery query, CancellationToken ct)
    {
        var from = query.FromDate.ToString("yyyy-MM-dd");
        var to = query.ToDate.ToString("yyyy-MM-dd");
        var req = CreateRequest(HttpMethod.Get,
            $"/v1/historical-candle?symbol={query.BrokerToken}&resolution={query.Timeframe}&from={from}&to={to}");
        var response = await http.SendAsync(req, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return [];
        var doc = JsonDocument.Parse(json);
        var bars = new List<OhlcvBar>();
        foreach (var c in doc.RootElement.GetProperty("data").EnumerateArray())
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
        var req = CreateRequest(HttpMethod.Get, "/v1/user/funds");
        var response = await http.SendAsync(req, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        var d = doc.RootElement.GetProperty("data");
        return new AccountFunds("MStock",
            d.GetProperty("available_cash").GetDecimal(),
            d.GetProperty("used_margin").GetDecimal(),
            d.GetProperty("net_balance").GetDecimal(),
            SystemClock.Instance.GetCurrentInstant().InZone(_ist));
    }

    public async Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct)
    {
        var req = CreateRequest(HttpMethod.Get, "/v1/portfolio/positions");
        var response = await http.SendAsync(req, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return [];
        var doc = JsonDocument.Parse(json);
        var positions = new List<BrokerPosition>();
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            positions.Add(new BrokerPosition("MStock",
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
        var req = CreateRequest(HttpMethod.Get, "/v1/portfolio/holdings");
        var response = await http.SendAsync(req, ct);
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
        var wsUrl = "wss://api.mstock.trade/openapi/typeb/feed";
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", $"Bearer {_feedToken ?? _jwtToken}");
        ws.Options.SetRequestHeader("X-PrivateKey", options.Value.ApiKey);
        await ws.ConnectAsync(new Uri(wsUrl), ct);

        var tokens = brokerTokens.ToList();
        var subscribeMsg = JsonSerializer.Serialize(new
        {
            action = "subscribe",
            symbols = tokens
        });
        await ws.SendAsync(System.Text.Encoding.UTF8.GetBytes(subscribeMsg), WebSocketMessageType.Text, true, ct);

        var buffer = new byte[65536];
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Text)
            {
                var json = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("symbol", out var sym) &&
                    doc.RootElement.TryGetProperty("ltp", out var ltp))
                {
                    var now = SystemClock.Instance.GetCurrentInstant().InZone(_ist);
                    yield return new BrokerTick(sym.GetString()!, ltp.GetDecimal(), 0, now);
                }
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
        return new LatencyReport("MStock",
            samples[samples.Count / 2], samples[(int)(samples.Count * 0.95)], samples[^1],
            samples.Count, SystemClock.Instance.GetCurrentInstant().InZone(_ist));
    }

    // ── Instrument Master (Scrip Master) ─────────────────────────────────────

    /// <summary>
    /// Downloads the complete MStock scrip master for all exchanges.
    /// MStock Type B provides per-exchange CSVs at /v1/master?exch_seg={exchange}.
    /// CSV columns: token, symbol, name, expiry, strike, lotsize, instrumenttype, exch_seg, tick_size
    /// Called once after login and once per day via Hangfire.
    /// </summary>
    public async Task<IReadOnlyList<InstrumentTokenMapping>> GetInstrumentMasterAsync(CancellationToken ct)
    {
        var mappings = new List<InstrumentTokenMapping>();

        // Download scrip master for each exchange segment
        foreach (var exchSeg in new[] { "NSE", "BSE", "NFO", "BFO", "MCX", "CDS" })
        {
            try
            {
                var req = CreateRequest(HttpMethod.Get, $"/v1/master?exch_seg={exchSeg}");
                var response = await http.SendAsync(req, ct);
                if (!response.IsSuccessStatusCode) continue;

                var csv = await response.Content.ReadAsStringAsync(ct);
                var parsed = ParseScripMasterCsv(csv, exchSeg);
                mappings.AddRange(parsed);
            }
            catch (Exception)
            {
                // Skip exchanges that fail — proceed with others
            }
        }

        return mappings;
    }

    /// <summary>
    /// Parses MStock scrip master CSV.
    /// Expected columns (header row): token,symbol,name,expiry,strike,lotsize,instrumenttype,exch_seg,tick_size
    /// Builds InternalSymbol as "{EXCHANGE}:{SYMBOL}" for equities,
    /// and "{EXCHANGE}:{SYMBOL}{EXPIRY}{OPTIONTYPE}{STRIKE}" for derivatives.
    /// </summary>
    private static List<InstrumentTokenMapping> ParseScripMasterCsv(string csv, string exchange)
    {
        var results = new List<InstrumentTokenMapping>();
        if (string.IsNullOrWhiteSpace(csv)) return results;

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return results; // need header + at least one data row

        // Parse header to build column index map (case-insensitive)
        var headers = lines[0].Split(',');
        var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++)
            idx[headers[i].Trim().ToLower()] = i;

        // Required columns
        if (!idx.ContainsKey("token") || !idx.ContainsKey("symbol")) return results;

        string Get(string[] cols, string key) =>
            idx.TryGetValue(key, out var i) && i < cols.Length
                ? cols[i].Trim().Trim('"')
                : string.Empty;

        for (int row = 1; row < lines.Length; row++)
        {
            var cols = lines[row].Split(',');
            if (cols.Length < 2) continue;

            var token     = Get(cols, "token");
            var symbol    = Get(cols, "symbol");
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(symbol)) continue;

            var name      = Get(cols, "name");
            var instrType = Get(cols, "instrumenttype").ToUpper();
            var expiry    = Get(cols, "expiry");       // e.g. 2024-12-26
            var strike    = Get(cols, "strike");
            var optType   = Get(cols, "optiontype");   // CE / PE
            var exchSeg   = Get(cols, "exch_seg").ToUpper();
            if (string.IsNullOrEmpty(exchSeg)) exchSeg = exchange.ToUpper();
            int.TryParse(Get(cols, "lotsize"), out var lotSize);
            decimal.TryParse(Get(cols, "tick_size"), out var tickSize);
            decimal.TryParse(strike, out var strikeDecimal);

            // Build InternalSymbol — mirror MStock's own naming convention
            string internalSymbol;
            if (!string.IsNullOrEmpty(expiry) && !string.IsNullOrEmpty(strike) && !string.IsNullOrEmpty(optType))
            {
                // Options: NFO:NIFTY2412019500CE
                var expiryCompact = expiry.Replace("-", "");
                internalSymbol = $"{exchSeg}:{symbol}{expiryCompact}{optType}{strike}";
            }
            else if (!string.IsNullOrEmpty(expiry) && instrType is "FUT" or "FUTIDX" or "FUTSTK")
            {
                // Futures: NFO:NIFTY24DECFUT (use short expiry: YYMMMDD → YYMM part)
                var expiryCompact = expiry.Replace("-", "");
                internalSymbol = $"{exchSeg}:{symbol}{expiryCompact.Substring(0, Math.Min(5, expiryCompact.Length))}FUT";
            }
            else
            {
                // Equities / Indices: NSE:RELIANCE
                internalSymbol = $"{exchSeg}:{symbol}";
            }

            results.Add(new InstrumentTokenMapping(
                InternalSymbol:  internalSymbol,
                BrokerToken:     token,
                Exchange:        exchSeg,
                BrokerName:      "MStock",
                TradingSymbol:   symbol,
                Name:            string.IsNullOrEmpty(name) ? symbol : name,
                InstrumentType:  string.IsNullOrEmpty(instrType) ? "EQ" : instrType,
                Expiry:          string.IsNullOrEmpty(expiry) ? null : expiry,
                StrikePrice:     string.IsNullOrEmpty(strike) ? null : strikeDecimal,
                OptionType:      string.IsNullOrEmpty(optType) ? null : optType,
                LotSize:         lotSize > 0 ? lotSize : 1,
                TickSize:        tickSize > 0 ? tickSize : 0.05m
            ));
        }

        return results;
    }
}
