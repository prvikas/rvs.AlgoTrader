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
    IOptions<ZerodhaOptions> options,
    IClock clock) : IFullBrokerClient
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

    /// <inheritdoc/>
    public void RestoreToken(string accessToken, string? feedToken = null)
    {
        _accessToken = accessToken;
        SetAuthHeader();
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
                clock.GetCurrentInstant().ToDateTimeOffset()
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
        var now = clock.GetCurrentInstant().InZone(_ist);
        return new BrokerQuote(brokerToken, d.GetProperty("last_price").GetDecimal(),
            d.GetProperty("ohlc").GetProperty("open").GetDecimal(),
            d.GetProperty("ohlc").GetProperty("high").GetDecimal(),
            d.GetProperty("ohlc").GetProperty("low").GetDecimal(),
            d.GetProperty("ohlc").GetProperty("close").GetDecimal(),
            d.GetProperty("volume").GetInt64(), 0, 0, now);
    }

    public async Task<IReadOnlyDictionary<string, OptionQuote>> GetOptionQuotesAsync(
        IEnumerable<string> brokerTokens, CancellationToken ct)
    {
        // Zerodha supports batch quote via comma-separated "i" params — fetch all in one call.
        // OI and IV are returned in the quote response; Greeks are not provided by Zerodha REST.
        SetAuthHeader();
        var tokens = brokerTokens.ToList();
        var query = string.Join("&", tokens.Select(t => $"i=NFO:{t}"));
        var response = await http.GetAsync($"{BaseUrl}/quote?{query}", ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, OptionQuote>();
        foreach (var token in tokens)
        {
            var key = $"NFO:{token}";
            if (!doc.RootElement.GetProperty("data").TryGetProperty(key, out var d)) continue;
            result[token] = new OptionQuote(
                InternalSymbol:    token,
                LastTradedPrice:   d.GetProperty("last_price").GetDecimal(),
                OpenInterest:      d.TryGetProperty("oi", out var oi) ? oi.GetInt64() : 0L,
                OiChange:          d.TryGetProperty("oi_day_change", out var oiChg) ? oiChg.GetInt64() : 0L,
                Volume:            d.GetProperty("volume").GetInt64(),
                ImpliedVolatility: 0m,  // not returned by Zerodha quote endpoint
                BidPrice:          d.TryGetProperty("depth", out var depthEl)
                                       ? depthEl.GetProperty("buy")[0].GetProperty("price").GetDecimal()
                                       : 0m,
                AskPrice:          d.TryGetProperty("depth", out var depthEl2)
                                       ? depthEl2.GetProperty("sell")[0].GetProperty("price").GetDecimal()
                                       : 0m,
                Delta:             0m);
        }
        return result;
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

    /// <summary>
    /// Zerodha Kite Connect per-request historical data limits (calendar days).
    /// Source: https://kite.trade/docs/connect/v3/historical/
    /// </summary>
    public HistoricalQueryLimits GetHistoricalQueryLimits(string timeframe) => timeframe switch
    {
        "1m"  => new(60),
        "3m"  => new(100),
        "5m"  => new(100),
        "15m" => new(200),
        "30m" => new(200),
        "60m" => new(400),
        "1d"  => new(2000),
        _     => new(60)
    };

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
            clock.GetCurrentInstant().InZone(_ist));
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
        // Zerodha Kite Connect WebSocket binary protocol
        // Reference: https://kite.trade/docs/connect/v3/websocket/#message-structure
        //
        // Frame layout:
        //   Bytes 0-1   : number of packets (big-endian uint16)
        //   Per packet  : 2-byte length prefix, then packet data
        //
        // Packet modes (all fields big-endian uint32, prices in paise → divide by 100):
        //   LTP    (8 bytes)  : [token][ltp]
        //   Quote  (44 bytes) : [token][ltp][ltq][atp][volume][buyQty][sellQty][open][high][low][close]
        //   Full  (184 bytes) : Quote prefix + timestamps + OI + market depth

        var wsUrl = $"wss://ws.kite.trade?api_key={options.Value.ApiKey}&access_token={_accessToken}";
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(wsUrl), ct);

        // Subscribe to numeric instrument tokens in "full" mode
        var tokens = brokerTokens
            .Select(t => int.TryParse(t, out var n) ? n : 0)
            .Where(n => n > 0)
            .ToArray();
        var subscribeMsg = JsonSerializer.Serialize(new { a = "subscribe", v = tokens });
        var modeMsg      = JsonSerializer.Serialize(new { a = "mode", v = new object[] { "full", tokens } });
        var enc = System.Text.Encoding.UTF8;
        await ws.SendAsync(enc.GetBytes(subscribeMsg), WebSocketMessageType.Text, true, ct);
        await ws.SendAsync(enc.GetBytes(modeMsg),      WebSocketMessageType.Text, true, ct);

        // Accumulate across partial ReceiveAsync calls before decoding
        var recvBuf  = new byte[65536];
        using var ms = new System.IO.MemoryStream(65536);

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            // Collect all segments of the current message
            ValueWebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new Memory<byte>(recvBuf), ct);
                if (result.MessageType == WebSocketMessageType.Close) yield break;
                ms.Write(recvBuf, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Binary)
            {
                ms.SetLength(0);
                continue; // text frames are heartbeats / connect confirmations — skip
            }

            var frame = ms.ToArray();
            ms.SetLength(0);

            if (frame.Length < 2) continue;

            var packetCount = (frame[0] << 8) | frame[1];
            var offset      = 2;
            var now         = clock.GetCurrentInstant().InZone(_ist);

            for (int i = 0; i < packetCount; i++)
            {
                if (offset + 2 > frame.Length) break;
                var packetLen = (frame[offset] << 8) | frame[offset + 1];
                offset += 2;
                if (packetLen < 8 || offset + packetLen > frame.Length) { offset += packetLen; continue; }

                // All fields are big-endian unsigned 32-bit integers
                var tokenId = ReadU32(frame, offset);
                var ltpRaw  = ReadU32(frame, offset + 4);

                // Volume lives at byte 16 in Quote/Full mode (packetLen >= 44)
                long volume = packetLen >= 44 ? ReadU32(frame, offset + 16) : 0L;

                // OI lives at byte 48 in Full mode (packetLen == 184)
                long oi = packetLen >= 184 ? ReadU32(frame, offset + 48) : 0L;

                yield return new BrokerTick(tokenId.ToString(), ltpRaw / 100m,
                    volume > 0 ? volume : oi, now);

                offset += packetLen;
            }
        }
    }

    /// <summary>Reads a big-endian unsigned 32-bit integer from <paramref name="buf"/>.</summary>
    private static long ReadU32(byte[] buf, int offset) =>
        (uint)((buf[offset] << 24) | (buf[offset + 1] << 16) | (buf[offset + 2] << 8) | buf[offset + 3]);

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
            samples.Count, clock.GetCurrentInstant().InZone(_ist));
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
                var token     = Get(cols, "instrument_token");
                var symbol    = Get(cols, "tradingsymbol");
                var exch      = Get(cols, "exchange").ToUpper();
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(symbol)) continue;

                var name      = Get(cols, "name");
                var instrType = Get(cols, "instrument_type");  // EQ, FUT, OPT, INDEX, etc.
                var expiry    = Get(cols, "expiry");           // YYYY-MM-DD or empty
                var strike    = Get(cols, "strike");
                var tickStr   = Get(cols, "tick_size");
                var lotStr    = Get(cols, "lot_size");
                // Zerodha does not have a separate option_type column; CE/PE are
                // encoded in the trading symbol for options rows.
                string? optType = null;
                if (instrType is "CE" or "PE") optType = instrType;
                else if (symbol.EndsWith("CE", StringComparison.Ordinal)) optType = "CE";
                else if (symbol.EndsWith("PE", StringComparison.Ordinal)) optType = "PE";

                decimal.TryParse(strike,  out var strikeDecimal);
                decimal.TryParse(tickStr, out var tickSize);
                int.TryParse(lotStr, out var lotSize);

                results.Add(new InstrumentTokenMapping(
                    InternalSymbol: $"{exch}:{symbol}",
                    BrokerToken:    token,
                    Exchange:       exch,
                    BrokerName:     "Zerodha",
                    TradingSymbol:  symbol,
                    Name:           string.IsNullOrEmpty(name) ? symbol : name,
                    InstrumentType: string.IsNullOrEmpty(instrType) ? "EQ" : instrType,
                    Expiry:         string.IsNullOrEmpty(expiry) ? null : expiry,
                    StrikePrice:    strikeDecimal > 0 ? strikeDecimal : null,
                    OptionType:     optType,
                    LotSize:        lotSize > 0 ? lotSize : 1,
                    TickSize:       tickSize > 0 ? tickSize : 0.05m
                ));
            }
        }
        catch { /* fail silently */ }
        return results;
    }
}

// NOTE: GetInstrumentMasterAsync appended below to satisfy IBrokerInstrumentClient
