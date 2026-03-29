using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.Text;
using rvs.AlgoTrader.Application.DTOs.HistoricalData;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Coordinates historical candle data: quality checks, gap detection,
/// download orchestration via <see cref="IDownloadJobRepository"/>,
/// and CSV bulk import.
/// </summary>
public class HistoricalDataManager(
    AlgoTraderDbContext db,
    ICandleRepository candleRepo,
    IHistoricalDownloadService downloader,
    IDownloadJobRepository jobRepo,
    IMarketCalendarService calendar,
    IClock clock,
    ILogger<HistoricalDataManager> log) : IHistoricalDataManager
{
    // Reserved for future gap-fill download orchestration
    private readonly IHistoricalDownloadService _downloader = downloader;
    private readonly IMarketCalendarService _calendar = calendar;

    // ── Quality ───────────────────────────────────────────────────────────────

    public async Task<DataQualityReportDto> GetQualityReportAsync(
        string symbol, string timeframe, CancellationToken ct = default)
    {
        var stats = await db.Database
            .SqlQueryRaw<QualityStatsRow>(
                $"SELECT COUNT(*) AS total, MIN(open_time) AS first_candle, MAX(open_time) AS last_candle " +
                $"FROM candles WHERE internal_symbol = '{Esc(symbol)}' AND timeframe = '{Esc(timeframe)}'")
            .FirstOrDefaultAsync(ct);

        if (stats is null || stats.Total == 0)
            return new DataQualityReportDto(symbol, timeframe, null, null, 0, 0, [], 0, false);

        var first = LocalDate.FromDateTime(stats.FirstCandle.DateTime);
        var last  = LocalDate.FromDateTime(stats.LastCandle.DateTime);

        var dups = await db.Database
            .SqlQueryRaw<CountRow>(
                $"SELECT COUNT(*)::int AS value FROM (" +
                $"  SELECT open_time, COUNT(*) c FROM candles " +
                $"  WHERE internal_symbol = '{Esc(symbol)}' AND timeframe = '{Esc(timeframe)}' " +
                $"  GROUP BY open_time HAVING COUNT(*) > 1) t")
            .FirstOrDefaultAsync(ct);

        var gaps = DetectGaps(first, last, stats.Total, timeframe);

        return new DataQualityReportDto(
            symbol, timeframe, first, last, stats.Total,
            gaps.Count, gaps, dups?.Value ?? 0, true);
    }

    public async Task<IReadOnlyList<DataQualityReportDto>> GetAllQualityReportsAsync(
        string timeframe = "1D", CancellationToken ct = default)
    {
#pragma warning disable EF1002 // timeframe sanitised via Esc(); no user-controlled input
        var symbols = await db.Database
            .SqlQueryRaw<SymbolRow>(
                $"SELECT DISTINCT internal_symbol AS symbol FROM candles WHERE timeframe = '{Esc(timeframe)}'")
            .ToListAsync(ct);
#pragma warning restore EF1002

        var reports = new List<DataQualityReportDto>(symbols.Count);
        foreach (var s in symbols)
            reports.Add(await GetQualityReportAsync(s.Symbol, timeframe, ct));
        return reports;
    }

    // ── Download orchestration ────────────────────────────────────────────────

    public async Task<Guid> EnqueueDownloadAsync(HistoricalDownloadRequest request, CancellationToken ct = default)
    {
        var jobId = Guid.NewGuid();
        var job = new DownloadJob(
            jobId,
            request.InternalSymbol,
            request.Timeframe,
            DateOnly.FromDateTime(request.FromDate.ToDateTimeUnspecified()),
            DateOnly.FromDateTime(request.ToDate.ToDateTimeUnspecified()),
            request.BrokerName ?? "default",
            "Queued",
            0,
            EstimateChunks(request.FromDate, request.ToDate, request.Timeframe),
            clock.NowInstant());

        await jobRepo.AddAsync(job, ct);
        log.LogInformation("Enqueued historical download job {JobId} for {Symbol}/{Timeframe} {From}–{To}",
            jobId, request.InternalSymbol, request.Timeframe, request.FromDate, request.ToDate);
        return jobId;
    }

    public async Task<int> FillGapsAsync(
        string symbol, string timeframe, string? brokerName = null, CancellationToken ct = default)
    {
        var report = await GetQualityReportAsync(symbol, timeframe, ct);
        if (!report.HasData || report.GapCount == 0) return 0;

        int queued = 0;
        foreach (var gap in report.Gaps)
        {
            await EnqueueDownloadAsync(new HistoricalDownloadRequest(
                symbol, timeframe, gap.GapStart, gap.GapEnd, brokerName), ct);
            queued++;
        }
        return queued;
    }

    // ── CSV bulk import ───────────────────────────────────────────────────────

    public async Task<CsvImportResultDto> ImportCsvAsync(
        string symbol, string timeframe, Stream csvStream, CancellationToken ct = default)
    {
        var ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
        using var reader = new System.IO.StreamReader(csvStream);

        var errors   = new List<string>();
        var candles  = new List<ClosedCandle>();
        int rowsRead = 0;
        int rowsSkip = 0;

        // Skip header
        var header = await reader.ReadLineAsync(ct);
        if (header is null)
            return new CsvImportResultDto(symbol, timeframe, 0, 0, 0, ["File is empty."]);

        var expectedHeader = "date,open,high,low,close,volume";
        if (!header.ToLowerInvariant().StartsWith("date"))
        {
            errors.Add($"Unexpected header: '{header}'. Expected: {expectedHeader}");
            return new CsvImportResultDto(symbol, timeframe, 0, 0, 0, errors);
        }

        string? line;
        int lineNum = 1;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            lineNum++;
            rowsRead++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cols = line.Split(',');
            if (cols.Length < 6)
            {
                errors.Add($"Line {lineNum}: insufficient columns ({cols.Length})");
                rowsSkip++;
                continue;
            }

            var dateParseResult = LocalDatePattern.Iso.Parse(cols[0].Trim());
            if (!dateParseResult.Success)
            {
                errors.Add($"Line {lineNum}: invalid date '{cols[0]}' (expected yyyy-MM-dd)");
                rowsSkip++;
                continue;
            }
            var date = dateParseResult.Value;

            if (!decimal.TryParse(cols[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var open)  ||
                !decimal.TryParse(cols[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var high)  ||
                !decimal.TryParse(cols[3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var low)   ||
                !decimal.TryParse(cols[4], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var close) ||
                !long.TryParse(cols[5].Trim(), out var volume))
            {
                errors.Add($"Line {lineNum}: could not parse OHLCV values");
                rowsSkip++;
                continue;
            }

            var openTime  = date.AtStartOfDayInZone(ist).ToInstant();
            var closeTime = date.PlusDays(1).AtStartOfDayInZone(ist).ToInstant().Minus(Duration.FromSeconds(1));

            candles.Add(new ClosedCandle(
                symbol, timeframe,
                openTime.InZone(ist),
                closeTime.InZone(ist),
                open, high, low, close, volume));
        }

        if (candles.Count == 0)
            return new CsvImportResultDto(symbol, timeframe, rowsRead, 0, rowsSkip, errors);

        // BulkInsert uses ON CONFLICT DO NOTHING — duplicate timestamps are idempotent
        await candleRepo.BulkInsertAsync(candles, ct);
        int inserted = candles.Count - rowsSkip;

        log.LogInformation("CSV import for {Symbol}/{TF}: {Read} rows read, {Inserted} inserted, {Skipped} skipped",
            symbol, timeframe, rowsRead, inserted, rowsSkip);

        return new CsvImportResultDto(symbol, timeframe, rowsRead, inserted, rowsSkip, errors);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<DataGapDto> DetectGaps(
        LocalDate first, LocalDate last, long actualCount, string timeframe)
    {
        // For daily data only — intra-day gap detection would require market calendar
        if (timeframe != "1D") return [];

        // Rough expected count: calendar days × 5/7 (weekdays) × 0.95 (holidays)
        var calDays     = (last - first).Days + 1;
        var expectedApx = (long)(calDays * 5.0 / 7 * 0.95);

        if (actualCount >= expectedApx * 0.9) return []; // within 10% — no significant gap

        // Build list of gaps longer than 5 consecutive calendar days
        // (simple heuristic: if actual is <90% of expected, flag the whole range as a gap)
        var gaps = new List<DataGapDto>();
        var gapDays = (int)(expectedApx - actualCount);
        if (gapDays > 5)
            gaps.Add(new DataGapDto(first, last, gapDays));
        return gaps;
    }

    private static int EstimateChunks(LocalDate from, LocalDate to, string timeframe)
    {
        var days = (to - from).Days;
        return timeframe switch
        {
            "1D" => Math.Max(1, days / 30),
            "1H" => Math.Max(1, days / 7),
            _    => Math.Max(1, days / 2),
        };
    }

    private static string Esc(string s) => s.Replace("'", "''");

    private sealed class QualityStatsRow
    {
        public long Total { get; set; }
        public DateTimeOffset FirstCandle { get; set; }
        public DateTimeOffset LastCandle { get; set; }
    }

    private sealed class CountRow { public int Value { get; set; } }
    private sealed class SymbolRow { public string Symbol { get; set; } = ""; }
}
