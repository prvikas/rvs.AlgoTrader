using NodaTime;

namespace rvs.AlgoTrader.Application.DTOs.Backtest;

/// <summary>Request body for downloading historical OHLCV data for a symbol/timeframe range.</summary>
public record DownloadHistoryRequest(
    string InternalSymbol,
    string Timeframe,
    LocalDate FromDate,
    LocalDate ToDate,
    string? BrokerName = null);
