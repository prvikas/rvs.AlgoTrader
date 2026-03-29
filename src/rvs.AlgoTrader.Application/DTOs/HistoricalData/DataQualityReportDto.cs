using NodaTime;

namespace rvs.AlgoTrader.Application.DTOs.HistoricalData;

/// <summary>
/// Summary of data quality for a symbol/timeframe combination.
/// Used by the admin data manager page to surface gaps, stale candles, or duplicate rows.
/// </summary>
public record DataQualityReportDto(
    string   InternalSymbol,
    string   Timeframe,
    LocalDate? FirstCandle,
    LocalDate? LastCandle,
    long     TotalCandles,
    int      GapCount,
    IReadOnlyList<DataGapDto> Gaps,
    int      DuplicateCount,
    bool     HasData);

/// <summary>A contiguous gap in candle data between two trading dates.</summary>
public record DataGapDto(LocalDate GapStart, LocalDate GapEnd, int MissingDays);

/// <summary>Request to download historical data for a symbol/timeframe range.</summary>
public record HistoricalDownloadRequest(
    string    InternalSymbol,
    string    Timeframe,
    LocalDate FromDate,
    LocalDate ToDate,
    string?   BrokerName = null);

/// <summary>Result of a CSV bulk import operation.</summary>
public record CsvImportResultDto(
    string   InternalSymbol,
    string   Timeframe,
    int      RowsRead,
    int      RowsInserted,
    int      RowsSkipped,
    IReadOnlyList<string> Errors);
