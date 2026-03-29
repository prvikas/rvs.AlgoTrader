using NodaTime;
using rvs.AlgoTrader.Application.DTOs.HistoricalData;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// Manages the historical candle data lifecycle: download orchestration, data-quality
/// checks, gap detection, and CSV bulk import.
/// <para>
/// This is a higher-level coordinator on top of <see cref="IHistoricalDownloadService"/>
/// (raw broker download) and <see cref="ICandleRepository"/> (storage).
/// </para>
/// </summary>
public interface IHistoricalDataManager
{
    // ── Quality ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Produce a data-quality report for <paramref name="symbol"/> at <paramref name="timeframe"/>.
    /// Identifies total candle count, first/last date, gaps, and duplicate timestamps.
    /// </summary>
    Task<DataQualityReportDto> GetQualityReportAsync(
        string symbol,
        string timeframe,
        CancellationToken ct = default);

    /// <summary>
    /// Returns quality reports for all symbols that have at least one candle stored at
    /// <paramref name="timeframe"/>.
    /// </summary>
    Task<IReadOnlyList<DataQualityReportDto>> GetAllQualityReportsAsync(
        string timeframe = "1D",
        CancellationToken ct = default);

    // ── Download orchestration ────────────────────────────────────────────────

    /// <summary>
    /// Enqueue a background download for the requested symbol/timeframe range.
    /// Returns the <see cref="IDownloadJobRepository"/> job ID.
    /// </summary>
    Task<Guid> EnqueueDownloadAsync(HistoricalDownloadRequest request, CancellationToken ct = default);

    /// <summary>
    /// Fill data gaps for <paramref name="symbol"/> at <paramref name="timeframe"/> by
    /// re-downloading missing date ranges from the broker.
    /// Returns the number of gaps that were queued for download.
    /// </summary>
    Task<int> FillGapsAsync(
        string symbol,
        string timeframe,
        string? brokerName = null,
        CancellationToken ct = default);

    // ── CSV bulk import ───────────────────────────────────────────────────────

    /// <summary>
    /// Import OHLCV data from a CSV stream.
    /// Expected columns: <c>date,open,high,low,close,volume</c> (header row required).
    /// Timestamps are parsed as IST calendar dates and converted to UTC instants.
    /// Duplicate timestamps are skipped (idempotent).
    /// </summary>
    Task<CsvImportResultDto> ImportCsvAsync(
        string symbol,
        string timeframe,
        Stream csvStream,
        CancellationToken ct = default);
}
