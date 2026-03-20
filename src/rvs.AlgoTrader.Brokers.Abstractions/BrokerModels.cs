using NodaTime;

namespace rvs.AlgoTrader.Brokers.Abstractions;

/// <summary>Raw tick data from broker WebSocket stream. Immutable struct — high-frequency allocation path.</summary>
public readonly record struct BrokerTick(
    string Symbol,
    decimal Ltp,
    long Volume,
    ZonedDateTime Timestamp
);

public record OrderRequest(
    string InternalSymbol,
    string BrokerToken,
    string OrderType,          // MARKET, LIMIT, SL, SL-M
    string Direction,          // BUY, SELL
    int Quantity,
    decimal? Price,
    decimal? TriggerPrice,
    string Exchange,           // NSE, BSE
    string ProductType,        // MIS, CNC, NRML
    string IdempotencyKey,
    Guid? StrategyRunId,
    string CorrelationId
);

public record OrderResult(
    bool Success,
    string? BrokerOrderId,
    string? RejectionReason,
    string CorrelationId
);

public record OrderModification(
    int? Quantity,
    decimal? Price,
    decimal? TriggerPrice
);

public record BrokerOrder(
    string BrokerOrderId,
    string InternalSymbol,
    string OrderType,
    string Direction,
    int Quantity,
    int FilledQuantity,
    decimal? Price,
    decimal? TriggerPrice,
    string Status,
    DateTimeOffset PlacedAt
);

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
    string ProductType
);

public record BrokerHolding(
    string InternalSymbol,
    int Quantity,
    decimal AveragePrice,
    decimal LastPrice,
    decimal PnL
);

/// <summary>
/// Broker credentials — field usage by broker:
/// - mStock Type B : ApiKey, ClientCode, Password, Totp (ephemeral — submitted at login time)
/// - Zerodha       : ApiKey, ApiSecret, RequestToken (from Kite OAuth redirect)
/// - Upstox        : ApiKey, ApiSecret, RequestToken (auth code), RefreshToken (extended_token)
/// </summary>
public record BrokerCredentials(
    string BrokerName,
    string ApiKey,
    string? ApiSecret,
    string? RequestToken,
    string? RefreshToken,   // Upstox only
    string? ClientCode,     // mStock: client login code (e.g. "AB1234")
    string? Password,       // mStock: login password
    string? Totp            // mStock: 6-digit TOTP (ephemeral, never persisted)
);

public record LoginResult(
    bool Success,
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    string? LoginUrl,
    string? ErrorMessage,
    string? FeedToken       // mStock only — used for WebSocket market data stream
);

public record LatencyReport(
    string BrokerName,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    int SampleCount,
    ZonedDateTime MeasuredAt
);

public record InstrumentTokenMapping(
    string InternalSymbol,
    string BrokerToken,
    string Exchange,
    string BrokerName,
    // Extended metadata — populated when available from broker scrip master
    string? TradingSymbol    = null,
    string? Name             = null,
    string? InstrumentType   = null,  // EQ, FUT, OPT, IDX, etc.
    string? Expiry           = null,  // YYYY-MM-DD
    decimal? StrikePrice     = null,
    string? OptionType       = null,  // CE, PE
    int     LotSize          = 1,
    decimal TickSize         = 0.05m
);
