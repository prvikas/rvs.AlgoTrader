namespace rvs.AlgoTrader.Brokers.Abstractions;

public interface IBrokerOrderClient
{
    Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct);
    Task<OrderResult> ModifyOrderAsync(string brokerId, OrderModification mod, CancellationToken ct);
    Task<bool> CancelOrderAsync(string brokerId, CancellationToken ct);
    Task<IReadOnlyList<BrokerOrder>> GetOrderBookAsync(CancellationToken ct);
}

public interface IBrokerMarketDataClient
{
    Task<BrokerQuote> GetQuoteAsync(string brokerToken, CancellationToken ct);

    /// <summary>
    /// Batch quote fetch for multiple broker tokens (up to 500 per call — broker-specific limit).
    /// Returns a dictionary keyed by broker token. Tokens not found in the response are absent.
    /// Used by OptionChainService to fetch all option legs in one call.
    /// </summary>
    Task<IReadOnlyDictionary<string, OptionQuote>> GetOptionQuotesAsync(
        IEnumerable<string> brokerTokens, CancellationToken ct);

    Task<IReadOnlyList<OhlcvBar>> GetHistoricalDataAsync(HistoricalDataQuery query, CancellationToken ct);
    Task<MarketDepth> GetDepthAsync(string brokerToken, CancellationToken ct);
}

public interface IBrokerAccountClient
{
    Task<AccountFunds> GetFundsAsync(CancellationToken ct);
    Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct);
    Task<IReadOnlyList<BrokerHolding>> GetHoldingsAsync(CancellationToken ct);
}

public interface IBrokerStreamClient
{
    IAsyncEnumerable<BrokerTick> StreamAsync(IEnumerable<string> brokerTokens, CancellationToken ct);
    Task SubscribeAsync(IEnumerable<string> brokerTokens, CancellationToken ct);
    Task UnsubscribeAsync(IEnumerable<string> brokerTokens, CancellationToken ct);
}

/// <summary>
/// Downloads the broker's complete instrument/scrip master — all tradeable symbols, tokens, and metadata.
/// Called once after login and once per trading day (typically before market open).
/// </summary>
public interface IBrokerInstrumentClient
{
    /// <summary>
    /// Download all instrument mappings from the broker.
    /// Returns the complete scrip master as InstrumentTokenMapping records.
    /// Exchanges: NSE, BSE, NFO, BFO, MCX, CDS, etc.
    /// </summary>
    Task<IReadOnlyList<InstrumentTokenMapping>> GetInstrumentMasterAsync(CancellationToken ct);
}

public interface IFullBrokerClient
    : IBrokerOrderClient, IBrokerMarketDataClient, IBrokerAccountClient, IBrokerStreamClient, IBrokerInstrumentClient
{
    string BrokerName { get; }
    Task<LoginResult> AuthenticateAsync(BrokerCredentials creds, CancellationToken ct);
    Task<LatencyReport> MeasureLatencyAsync(CancellationToken ct);

    /// <summary>
    /// Restores a previously-obtained access token into this client's in-memory state
    /// without going through a full auth round-trip. Called by IStartupOrchestrator Step 7
    /// to re-inject Redis-persisted tokens after a process restart.
    /// feedToken is optional (used by brokers that have a separate WebSocket feed token).
    /// </summary>
    void RestoreToken(string accessToken, string? feedToken = null);
}

public interface IBrokerClientFactory
{
    IFullBrokerClient GetClient(string brokerName);
    IBrokerOrderClient GetOrderClient(string brokerName);
    IBrokerMarketDataClient GetMarketDataClient(string brokerName);
    IBrokerStreamClient GetStreamClient(string brokerName);
}

public interface IBrokerSessionManager
{
    Task<string> GetAccessTokenAsync(string brokerName, CancellationToken ct);
    Task RefreshSessionAsync(string brokerName, CancellationToken ct);
    Task InvalidateSessionAsync(string brokerName, CancellationToken ct);
    bool IsSessionValid(string brokerName);
}

public interface IInstrumentTokenResolver
{
    Task<string?> ResolveAsync(string internalSymbol, string brokerName, CancellationToken ct);
    Task<IReadOnlyList<InstrumentTokenMapping>> GetAllMappingsAsync(string brokerName, CancellationToken ct);
    Task RefreshAsync(string brokerName, CancellationToken ct);
}
