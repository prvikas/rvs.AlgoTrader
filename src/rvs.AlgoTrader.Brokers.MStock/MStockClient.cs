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
    /// <summary>WebSocket feed endpoint — configurable via Broker:MStock:WebSocketUrl in appsettings.</summary>
    public string WebSocketUrl { get; set; } = "wss://api.mstock.trade/openapi/typeb/feed";
    // Note: TOTP is NOT stored here — it must be submitted interactively at login time
}

public class MStockClient(
    HttpClient http,
    MStockAuth auth,
    IOptions<MStockOptions> options,
    IClock clock,
    ILogger<MStockClient> logger) : IFullBrokerClient
{
    private readonly DateTimeZone _ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
    private string? _jwtToken;
    private string? _feedToken;
    private const string BaseUrl = "https://api.mstock.trade/openapi/typeb";

    public string BrokerName   => Domain.Constants.BrokerNames.MStock;
    public string Market       => Domain.Constants.MarketCodes.India;
    public Domain.Enums.BrokerAuthFlowType AuthFlowType => Domain.Enums.BrokerAuthFlowType.DirectCredentials;

    private void SetAuthHeaders(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_jwtToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken);
        request.Headers.Add("X-PrivateKey", options.Value.ApiKey);
        request.Headers.Add("X-Mirae-Version", "1");
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

    /// <inheritdoc/>
    public void RestoreToken(string accessToken, string? feedToken = null)
    {
        _jwtToken = accessToken;
        _feedToken = feedToken;   // may be null if not stored; WebSocket falls back to _jwtToken
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
                clock.GetCurrentInstant().ToDateTimeOffset()));
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
        var now = clock.GetCurrentInstant().InZone(_ist);
        return new BrokerQuote(brokerToken, d.GetProperty("ltp").GetDecimal(),
            d.GetProperty("open").GetDecimal(), d.GetProperty("high").GetDecimal(),
            d.GetProperty("low").GetDecimal(), d.GetProperty("close").GetDecimal(),
            d.GetProperty("volume").GetInt64(), 0, 0, now);
    }

    public async Task<IReadOnlyDictionary<string, OptionQuote>> GetOptionQuotesAsync(
        IEnumerable<string> brokerTokens, CancellationToken ct)
    {
        // mStock option chain endpoint accepts a comma-separated symbol list.
        // OI is available; IV and Greeks are not returned by the basic quote endpoint.
        var tokens = brokerTokens.ToList();
        var symbols = string.Join(",", tokens);
        var req = CreateRequest(HttpMethod.Get, $"/v1/optionchain?symbols={Uri.EscapeDataString(symbols)}");
        var response = await http.SendAsync(req, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, OptionQuote>();
        foreach (var token in tokens)
        {
            if (!doc.RootElement.GetProperty("data").TryGetProperty(token, out var d)) continue;
            result[token] = new OptionQuote(
                InternalSymbol: token,
                LastTradedPrice: d.GetProperty("ltp").GetDecimal(),
                OpenInterest: d.TryGetProperty("oi", out var oi) ? oi.GetInt64() : 0L,
                OiChange: d.TryGetProperty("oiChange", out var oiChg) ? oiChg.GetInt64() : 0L,
                Volume: d.TryGetProperty("volume", out var vol) ? vol.GetInt64() : 0L,
                ImpliedVolatility: 0m,
                BidPrice: d.TryGetProperty("bestBid", out var bid) ? bid.GetDecimal() : 0m,
                AskPrice: d.TryGetProperty("bestAsk", out var ask) ? ask.GetDecimal() : 0m,
                Delta: 0m);
        }
        return result;
    }

    public async Task<IReadOnlyList<OhlcvBar>> GetHistoricalDataAsync(HistoricalDataQuery query, CancellationToken ct)
    {
        // MStock Type B uses POST /v1/historical/candle/data with JSON body.
        // Interval must be one of the named constants; date format is "yyyy-MM-dd HH:mm".
        var interval = TimeframeToMStockInterval(query.Timeframe);

        // Extract exchange from InternalSymbol ("NSE:AXISBANK" → "NSE", "BSE:TCS" → "BSE")
        var parts = query.InternalSymbol.Split(':', 2);
        var exchange = parts.Length == 2 ? parts[0].ToUpperInvariant() : "NSE";

        // MStock limits the date window per request depending on interval.
        // Use IST market open/close as from/to time anchors.
        var fromStr = query.FromDate.ToString("yyyy-MM-dd") + " 09:00";
        var toStr = query.ToDate.ToString("yyyy-MM-dd") + " 15:30";

        logger.LogDebug(
            "[MStock] GetHistoricalData POST /v1/historical — {Exchange} token={Token} interval={Interval} {From}→{To}",
            exchange, query.BrokerToken, interval, fromStr, toStr);

        var body = JsonSerializer.Serialize(new
        {
            exchange = exchange,
            symboltoken = query.BrokerToken,
            interval = interval,
            fromdate = fromStr,
            todate = toStr,
        });

        var req = CreateRequest(HttpMethod.Get, "/instruments/historical");
        req.Content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json");

        var response = await http.SendAsync(req, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "[MStock] GetHistoricalData HTTP {Status} for {Symbol}/{Tf}. Body: {Body}",
                (int)response.StatusCode, query.InternalSymbol, query.Timeframe,
                json.Length > 500 ? json[..500] : json);
            throw new HttpRequestException(
                $"MStock historical data request failed with HTTP {(int)response.StatusCode} ({response.StatusCode}) " +
                $"for {query.InternalSymbol}/{query.Timeframe}. Body: {(json.Length > 300 ? json[..300] : json)}");
        }

        var doc = JsonDocument.Parse(json);
        var bars = new List<OhlcvBar>();

        // MStock response: { "status": true, "message": "SUCCESS", "data": [["2024-01-01T09:15:00+05:30", o, h, l, c, v], ...] }
        if (!doc.RootElement.TryGetProperty("data", out var dataElement) ||
            dataElement.ValueKind == JsonValueKind.Null)
        {
            // API may return status:false with a message — log it
            var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "no data field";
            logger.LogWarning(
                "[MStock] GetHistoricalData returned no data for {Symbol}/{Tf}. message={Msg}. Body: {Body}",
                query.InternalSymbol, query.Timeframe, msg,
                json.Length > 300 ? json[..300] : json);
            return [];
        }


        if (!dataElement.TryGetProperty("candles", out var dataCandles) ||
           dataCandles.ValueKind == JsonValueKind.Null)
        {
            // API may return status:false with a message — log it
            var msg = dataElement.TryGetProperty("message", out var m) ? m.GetString() : "no candle data field";
            logger.LogWarning(
                "[MStock] GetHistoricalData returned no Candle data for {Symbol}/{Tf}. message={Msg}. Body: {Body}",
                query.InternalSymbol, query.Timeframe, msg,
                json.Length > 300 ? json[..300] : json);
            return [];
        }

        foreach (var c in dataCandles.EnumerateArray())
        {
            // Each element is an array: [timestamp_str, open, high, low, close, volume]
            if (c.GetArrayLength() < 6) continue;
            var rawTs = c[0].GetString()!;
            var ts = Instant.FromDateTimeUtc(DateTime.Parse(rawTs,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime())
                .InZone(_ist);

            bars.Add(new OhlcvBar(query.InternalSymbol, query.Timeframe, ts,
                c[1].GetDecimal(), c[2].GetDecimal(),
                c[3].GetDecimal(), c[4].GetDecimal(),
                c[5].GetInt64()));
        }

        logger.LogInformation("[MStock] GetHistoricalData returned {Count} bars for {Symbol}/{Tf}",
            bars.Count, query.InternalSymbol, query.Timeframe);
        return bars;
    }

    /// <summary>
    /// MStock Type B hard limit: 1000 candles per request.
    /// NSE intraday session = 375 minutes (09:15–15:30).
    /// Calendar days = trading days × 1.46 (365÷250), rounded down for safety.
    /// </summary>
    public HistoricalQueryLimits GetHistoricalQueryLimits(string timeframe) => timeframe switch
    {
        "1m" => new(2),    // 375 candles/day → 2 trading days → 3 calendar days
        "3m" => new(8),   // 125 candles/day → 8 trading days → 11 calendar days
        "5m" => new(13),   // 75 candles/day  → 13 trading days → 18 calendar days
        "15m" => new(40),   // 25 candles/day  → 40 trading days → 55 calendar days
        "30m" => new(76),  // 13 candles/day  → 76 trading days → 105 calendar days
        "60m" => new(142),  // 7 candles/day   → 142 trading days → 195 calendar days
        "1d" => new(1000), // 1 candle/day    → 1000 trading days → 1380 calendar days
        _ => new(3)
    };

    /// <summary>
    /// Maps our internal timeframe strings to MStock Type B interval constants.
    /// Reference: mStock Type B API — POST /v1/historical/candle/data — interval field.
    /// </summary>
    private static string TimeframeToMStockInterval(string timeframe) => timeframe switch
    {
        "1m" => "ONE_MINUTE",
        "3m" => "THREE_MINUTE",
        "5m" => "FIVE_MINUTE",
        "10m" => "TEN_MINUTE",
        "15m" => "FIFTEEN_MINUTE",
        "30m" => "THIRTY_MINUTE",
        "60m" => "ONE_HOUR",
        "1h" => "ONE_HOUR",
        "1d" => "ONE_DAY",
        _ => throw new ArgumentException($"Unsupported timeframe for MStock: '{timeframe}'. " +
                     "Supported: 1m, 3m, 5m, 10m, 15m, 30m, 60m, 1d")
    };

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
            clock.GetCurrentInstant().InZone(_ist));
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
        var wsUrl = options.Value.WebSocketUrl;
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
                    var now = clock.GetCurrentInstant().InZone(_ist);
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
            samples.Count, clock.GetCurrentInstant().InZone(_ist));
    }

    // ── Instrument Master (Scrip Master) ─────────────────────────────────────

    /// <summary>
    /// Downloads the complete MStock scrip master using the OpenAPIScripMaster endpoint.
    ///
    /// Endpoint: GET /instruments/OpenAPIScripMaster
    /// Returns all exchanges (NSE, BSE, NFO, BFO, MCX, CDS) in a single CSV download.
    /// Each row contains the exch_seg column identifying the exchange, so no per-exchange
    /// looping is required.
    ///
    /// Reference: https://api.mstock.trade/openapi/typeb/instruments/OpenAPIScripMaster
    /// (OpenAlgo mstock broker implementation uses this same endpoint)
    ///
    /// The per-exchange /v1/master?exch_seg={X} endpoint was NOT used here because it
    /// silently returns empty data for NSE and some other segments under certain auth states.
    /// </summary>
    public async Task<IReadOnlyList<InstrumentTokenMapping>> GetInstrumentMasterAsync(CancellationToken ct)
    {
        try
        {
            // Primary: bulk OpenAPIScripMaster — all exchanges in one JSON download.
            // Endpoint returns either a plain JSON array ([...]) or a wrapped object
            // ({"data":[...]}, {"scrips":[...]}, etc.) depending on API version.
            var req = CreateRequest(HttpMethod.Get, "/instruments/OpenAPIScripMaster");
            var response = await http.SendAsync(req, ct);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                var trimmed = body.TrimStart();

                List<InstrumentTokenMapping> mappings;
                if (trimmed.StartsWith('['))
                {
                    // Plain array format: [{"token":"...","symbol":"...",...},...]
                    mappings = ParseScripMasterJson(body);
                }
                else if (trimmed.StartsWith('{'))
                {
                    // Wrapped object format: {"data":[...]} or {"scrips":[...]} etc.
                    mappings = ParseScripMasterJsonWrapped(body);
                }
                else
                {
                    // Fallback: treat as CSV (unlikely for this endpoint but safe)
                    mappings = ParseScripMasterCsv(body, string.Empty);
                }

                if (mappings.Count > 0)
                    return mappings;
            }
        }
        catch (Exception ex)
        {
            // Log and fall through to per-exchange fallback
            _ = ex; // caller can inspect via Serilog enrichment if needed
        }

        // Fallback: per-exchange segments (used if bulk endpoint is unavailable)
        var fallback = new List<InstrumentTokenMapping>();
        foreach (var exchSeg in new[] { "NSE", "BSE", "NFO", "BFO", "MCX", "CDS" })
        {
            try
            {
                var req = CreateRequest(HttpMethod.Get, $"/v1/master?exch_seg={exchSeg}");
                var response = await http.SendAsync(req, ct);
                if (!response.IsSuccessStatusCode) continue;
                var csv = await response.Content.ReadAsStringAsync(ct);
                fallback.AddRange(ParseScripMasterCsv(csv, exchSeg));
            }
            catch (Exception)
            {
                // Skip segment; continue with others
            }
        }
        return fallback;
    }

    /// <summary>
    /// Handles wrapped object responses from OpenAPIScripMaster, e.g.:
    ///   { "data": [...] }  or  { "scrips": [...] }  or  { "result": [...] }
    /// Searches all top-level properties for the first JSON array value and
    /// delegates to ParseScripMasterJson.
    /// </summary>
    private static List<InstrumentTokenMapping> ParseScripMasterJsonWrapped(string json)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(json).RootElement; }
        catch { return []; }

        if (root.ValueKind != JsonValueKind.Object) return [];

        // Common wrapper property names used by various broker APIs
        var candidateProps = new[] { "data", "scrips", "result", "instruments", "records", "ScripMaster" };

        // Try known property names first
        foreach (var prop in candidateProps)
        {
            if (root.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.Array)
            {
                var results = ParseScripMasterJson(val.GetRawText());
                if (results.Count > 0) return results;
            }
        }

        // Fall back: scan all properties for the first non-empty array
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                var results = ParseScripMasterJson(prop.Value.GetRawText());
                if (results.Count > 0) return results;
            }
        }

        return [];
    }

    /// <summary>
    /// Parses the OpenAPIScripMaster JSON array returned by /instruments/OpenAPIScripMaster.
    /// JSON element shape (same field names as the per-exchange CSV):
    /// { "token":"3045", "symbol":"SBIN-EQ", "name":"STATE BANK OF INDIA",
    ///   "expiry":"", "strike":"-1", "lotsize":"1", "instrumenttype":"",
    ///   "exch_seg":"NSE", "tick_size":"5" }
    /// For derivatives, expiry comes as "dd-MMM-yyyy" (e.g. "25-APR-2026") from MStock.
    /// It is normalised to ISO "yyyy-MM-dd" here so downstream code can use LocalDatePattern.Iso.
    /// </summary>
    private static List<InstrumentTokenMapping> ParseScripMasterJson(string json)
    {
        var results = new List<InstrumentTokenMapping>();
        if (string.IsNullOrWhiteSpace(json)) return results;

        JsonElement root;
        try { root = JsonDocument.Parse(json).RootElement; }
        catch { return results; }

        if (root.ValueKind != JsonValueKind.Array) return results;

        string Str(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? string.Empty
                : string.Empty;

        foreach (var item in root.EnumerateArray())
        {
            var token = Str(item, "token");
            var symbol = Str(item, "symbol");
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(symbol)) continue;

            var name = Str(item, "name");
            var instrType = Str(item, "instrumenttype").ToUpper();
            var expiryRaw = Str(item, "expiry");                  // raw MStock form, e.g. "25-APR-2026" — used only for internalSymbol construction
            var expiryIso = NormalizeExpiryToIso(expiryRaw);      // "yyyy-MM-dd" — stored in Expiry; used by LocalDatePattern.Iso downstream
            var strike = Str(item, "strike");
            var optType = Str(item, "optiontype");   // CE / PE (may be absent in some responses)
            var exchSeg = Str(item, "exch_seg").ToUpper();

            // strike "-1" means no strike (equities/futures)
            var strikeClean = strike is "-1" or "" ? string.Empty : strike;
            decimal.TryParse(Str(item, "lotsize"), out var lotSize);
            decimal.TryParse(Str(item, "tick_size"), out var tickDecimal);
            decimal.TryParse(strikeClean, out var strikeDecimal);

            // Infer CE/PE from symbol suffix when optiontype field is absent
            if (string.IsNullOrEmpty(optType))
            {
                if (symbol.EndsWith("CE", StringComparison.OrdinalIgnoreCase)) optType = "CE";
                else if (symbol.EndsWith("PE", StringComparison.OrdinalIgnoreCase)) optType = "PE";
            }

            // Normalise instrType when the field is empty but we can infer it from context.
            // BFO (and some NFO rows) omit instrumenttype entirely, so we fill it in here
            // so that InstrumentRefreshService.ParseInstrumentType gets a meaningful value
            // instead of falling through to the Equity default.
            if (string.IsNullOrEmpty(instrType))
            {
                if (!string.IsNullOrEmpty(optType))
                    instrType = "OPT";   // option leg — CE or PE
                else if (!string.IsNullOrEmpty(expiryIso) && string.IsNullOrEmpty(strikeClean))
                    instrType = "FUT";   // futures leg — has expiry, no strike
            }

            // Normalise equity symbol: MStock appends "-EQ" (e.g. "AXISBANK-EQ") for NSE/BSE equities.
            // Strip it so internal symbols are canonical ("NSE:AXISBANK"), matching what users enter
            // in strategy forms and what InstrumentRefreshService.NormaliseSymbol produces.
            var baseSymbol = symbol.EndsWith("-EQ", StringComparison.OrdinalIgnoreCase)
                ? symbol[..^3]
                : symbol;

            // Build internalSymbol using MStock's compact date form (strip dashes from raw expiry)
            // so the key matches MStock's own naming convention, e.g. "NFO:NIFTY25APR2026FUT".
            string internalSymbol;
            if (!string.IsNullOrEmpty(expiryIso) && !string.IsNullOrEmpty(strikeClean) && !string.IsNullOrEmpty(optType))
            {
                var expiryCompact = expiryRaw.Replace("-", "");     // "25APR2026" from "25-APR-2026"
                internalSymbol = $"{exchSeg}:{symbol}{expiryCompact}{optType}{strikeClean}";
            }
            else if (!string.IsNullOrEmpty(expiryIso) && instrType is "FUT" or "FUTIDX" or "FUTSTK")
            {
                var expiryCompact = expiryRaw.Replace("-", "");
                internalSymbol = $"{exchSeg}:{symbol}{expiryCompact[..Math.Min(5, expiryCompact.Length)]}FUT";
            }
            else
            {
                internalSymbol = $"{exchSeg}:{baseSymbol}";
            }

            results.Add(new InstrumentTokenMapping(
                InternalSymbol: internalSymbol,
                BrokerToken: token,
                Exchange: exchSeg,
                BrokerName: "MStock",
                TradingSymbol: symbol,
                Name: string.IsNullOrEmpty(name) ? symbol : name,
                InstrumentType: string.IsNullOrEmpty(instrType) ? "EQ" : instrType,
                Expiry: string.IsNullOrEmpty(expiryIso) ? null : expiryIso,
                StrikePrice: string.IsNullOrEmpty(strikeClean) ? null : strikeDecimal,
                OptionType: string.IsNullOrEmpty(optType) ? null : optType,
                LotSize: (int)(lotSize > 0 ? lotSize : 1),
                TickSize: tickDecimal > 0 ? tickDecimal : 0.05m
            ));
        }

        return results;
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

            var token = Get(cols, "token");
            var symbol = Get(cols, "symbol");
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(symbol)) continue;

            var name = Get(cols, "name");
            var instrType = Get(cols, "instrumenttype").ToUpper();
            var expiryRaw = Get(cols, "expiry");                   // raw MStock form — used for internalSymbol construction
            var expiryIso = NormalizeExpiryToIso(expiryRaw);       // "yyyy-MM-dd" — stored in Expiry
            var strike = Get(cols, "strike");
            var optType = Get(cols, "optiontype");   // CE / PE
            var exchSeg = Get(cols, "exch_seg").ToUpper();
            if (string.IsNullOrEmpty(exchSeg)) exchSeg = exchange.ToUpper();
            int.TryParse(Get(cols, "lotsize"), out var lotSize);
            decimal.TryParse(Get(cols, "tick_size"), out var tickSize);
            decimal.TryParse(strike, out var strikeDecimal);

            // Normalise instrType when the field is empty but we can infer it from context.
            if (string.IsNullOrEmpty(instrType))
            {
                if (!string.IsNullOrEmpty(optType))
                    instrType = "OPT";
                else if (!string.IsNullOrEmpty(expiryIso) && string.IsNullOrEmpty(strike))
                    instrType = "FUT";
            }

            // Build InternalSymbol using raw expiry for MStock-compatible compact dates
            // e.g. "25-APR-2026".Replace("-","") → "25APR2026" → "NFO:NIFTY25APR2026FUT"
            string internalSymbol;
            if (!string.IsNullOrEmpty(expiryIso) && !string.IsNullOrEmpty(strike) && !string.IsNullOrEmpty(optType))
            {
                // Options: NFO:NIFTY25APR202619500CE
                var expiryCompact = expiryRaw.Replace("-", "");
                internalSymbol = $"{exchSeg}:{symbol}{expiryCompact}{optType}{strike}";
            }
            else if (!string.IsNullOrEmpty(expiryIso) && instrType is "FUT" or "FUTIDX" or "FUTSTK")
            {
                // Futures: NFO:NIFTY25APRFUT
                var expiryCompact = expiryRaw.Replace("-", "");
                internalSymbol = $"{exchSeg}:{symbol}{expiryCompact.Substring(0, Math.Min(5, expiryCompact.Length))}FUT";
            }
            else
            {
                // Equities / Indices: NSE:RELIANCE
                internalSymbol = $"{exchSeg}:{symbol}";
            }

            results.Add(new InstrumentTokenMapping(
                InternalSymbol: internalSymbol,
                BrokerToken: token,
                Exchange: exchSeg,
                BrokerName: "MStock",
                TradingSymbol: symbol,
                Name: string.IsNullOrEmpty(name) ? symbol : name,
                InstrumentType: string.IsNullOrEmpty(instrType) ? "EQ" : instrType,
                Expiry: string.IsNullOrEmpty(expiryIso) ? null : expiryIso,
                StrikePrice: string.IsNullOrEmpty(strike) ? null : strikeDecimal,
                OptionType: string.IsNullOrEmpty(optType) ? null : optType,
                LotSize: lotSize > 0 ? lotSize : 1,
                TickSize: tickSize > 0 ? tickSize : 0.05m
            ));
        }

        return results;
    }

    /// <summary>
    /// Normalises MStock expiry dates to ISO "yyyy-MM-dd" format so that
    /// NodaTime's LocalDatePattern.Iso can parse them downstream.
    ///
    /// MStock returns expiry as "dd-MMM-yyyy" (e.g. "25-APR-2026").
    /// Some API versions may already return ISO "2026-04-25".
    /// Both forms are handled; anything else is returned as-is and will be
    /// treated as unparseable (instruments are accepted without an expiry filter
    /// instead of being silently dropped).
    /// </summary>
    private static string NormalizeExpiryToIso(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw == "1") return string.Empty;

        // Already ISO: "2026-04-25"
        if (raw.Length == 10 && raw[4] == '-' && raw[7] == '-') return raw;

        // MStock dd-MMM-yyyy: "25-APR-2026"
        if (DateTime.TryParseExact(raw, "dd-MMM-yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return dt.ToString("yyyy-MM-dd");

        // MStock dd-MMM-yy: "25-APR-26" (two-digit year, seen in older responses)
        if (DateTime.TryParseExact(raw, "dd-MMM-yy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt2))
            return dt2.ToString("yyyy-MM-dd");

        // General fallback — try any common format
        if (DateTime.TryParse(raw,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dtGen))
            return dtGen.ToString("yyyy-MM-dd");

        return raw;   // leave as-is; will fail LocalDatePattern.Iso → treated as no-expiry
    }
}
