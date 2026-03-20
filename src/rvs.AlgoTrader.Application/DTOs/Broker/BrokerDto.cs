namespace rvs.AlgoTrader.Application.DTOs.Broker;

public record BrokerLatencyDto(
    string BrokerName, double P50Ms, double P95Ms, double P99Ms,
    int SampleCount, DateTimeOffset MeasuredAt);

public record BrokerConnectionStatusDto(
    string BrokerName, bool IsConnected, bool IsAuthenticated,
    DateTimeOffset? LastHeartbeatAt, int ReconnectAttempts,
    string? LastDisconnectReason, DateTimeOffset? SessionExpiresAt);

public record BrokerFundsDto(
    string BrokerName, decimal AvailableMargin, decimal UsedMargin,
    decimal TotalMargin, string Currency, DateTimeOffset FetchedAt);

public record BrokerPositionDto(
    string TradingSymbol, string InstrumentType, int Quantity,
    decimal AverageBuyPrice, decimal LastTradedPrice,
    decimal UnrealisedPnl, decimal RealisedPnl, string Product);
