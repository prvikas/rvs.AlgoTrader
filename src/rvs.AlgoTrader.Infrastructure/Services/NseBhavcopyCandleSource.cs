using System.Globalization;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Downloads and parses NSE Bhavcopy (equity EOD data) and bulk-upserts into the candles table.
///
/// Source URL: https://nsearchives.nseindia.com/products/content/sec_bhavdata_full_{DDMMYYYY}.csv
/// Headers required: Referer, User-Agent, Accept (NSE blocks requests without these).
/// On 404 (weekend/holiday): logs a warning and returns 0 — does not throw.
///
/// AP-010: IHttpClientFactory with named client "NseBhavcopy" — Polly retry + circuit breaker
///         are configured on this client in ServiceCollectionExtensions.
/// </summary>
public sealed class NseBhavcopyCandleSource(
    IHttpClientFactory httpClientFactory,
    ICandleRepository candleRepo,
    ILogger<NseBhavcopyCandleSource> logger) : INseBhavcopyCandleSource
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
    private const string ClientName = "NseBhavcopy";

    /// <summary>
    /// Downloads Bhavcopy for <paramref name="date"/>, parses EQ series rows,
    /// and bulk-upserts into candles table (timeframe = "1D").
    /// Returns the number of symbols written.
    /// </summary>
    public async Task<int> DownloadAndSaveAsync(LocalDate date, CancellationToken ct)
    {
        var ddmmyyyy = $"{date.Day:D2}{date.Month:D2}{date.Year}";
        var url = $"https://nsearchives.nseindia.com/products/content/sec_bhavdata_full_{ddmmyyyy}.csv";

        string csv;
        try
        {
            var client = httpClientFactory.CreateClient(ClientName);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Referer", "https://www.nseindia.com");
            req.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; AlgoTrader/1.0)");
            req.Headers.Add("Accept", "text/csv,*/*");

            var resp = await client.SendAsync(req, ct);

            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                logger.LogWarning("[NseBhavcopy] No file for {Date} (holiday/weekend) — skipping", date);
                return 0;
            }

            resp.EnsureSuccessStatusCode();
            csv = await resp.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "[NseBhavcopy] HTTP error downloading Bhavcopy for {Date}", date);
            return 0;
        }

        var candles = ParseCsv(csv, date);
        if (candles.Count == 0)
        {
            logger.LogWarning("[NseBhavcopy] No EQ rows parsed for {Date}", date);
            return 0;
        }

        await candleRepo.BulkInsertAsync(candles, ct);
        logger.LogInformation("[NseBhavcopy] Saved {Count} daily candles for {Date}", candles.Count, date);
        return candles.Count;
    }

    private List<ClosedCandle> ParseCsv(string csv, LocalDate date)
    {
        var candles = new List<ClosedCandle>();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return candles;

        // Build header index
        var headers = lines[0].Split(',');
        var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++)
            idx[headers[i].Trim().Trim('"')] = i;

        string Get(string[] cols, string key) =>
            idx.TryGetValue(key, out var i) && i < cols.Length ? cols[i].Trim().Trim('"') : "";

        // Candle open/close times: IST midnight for a daily bar
        var openTime  = date.AtStartOfDayInZone(Ist);
        var closeTime = date.PlusDays(1).AtStartOfDayInZone(Ist).PlusNanoseconds(-1);

        for (int row = 1; row < lines.Length; row++)
        {
            var cols = lines[row].Split(',');
            if (cols.Length < 4) continue;

            var series = Get(cols, "SERIES");
            if (!string.Equals(series, "EQ", StringComparison.OrdinalIgnoreCase)) continue;

            var symbol = Get(cols, "SYMBOL");
            if (string.IsNullOrEmpty(symbol)) continue;

            if (!decimal.TryParse(Get(cols, "OPEN"),  NumberStyles.Number, CultureInfo.InvariantCulture, out var open))  continue;
            if (!decimal.TryParse(Get(cols, "HIGH"),  NumberStyles.Number, CultureInfo.InvariantCulture, out var high))  continue;
            if (!decimal.TryParse(Get(cols, "LOW"),   NumberStyles.Number, CultureInfo.InvariantCulture, out var low))   continue;
            if (!decimal.TryParse(Get(cols, "CLOSE"), NumberStyles.Number, CultureInfo.InvariantCulture, out var close)) continue;

            decimal.TryParse(Get(cols, "TOTTRDQTY"), NumberStyles.Number, CultureInfo.InvariantCulture, out var volume);

            candles.Add(new ClosedCandle(
                InternalSymbol: $"NSE:{symbol}",
                Timeframe:      "1D",
                OpenTime:       openTime,
                CloseTime:      closeTime,
                Open:           open,
                High:           high,
                Low:            low,
                Close:          close,
                Volume:         (long)volume));
        }

        return candles;
    }
}

/// <summary>Downloads and saves NSE Bhavcopy EOD data into the candles table.</summary>
public interface INseBhavcopyCandleSource
{
    Task<int> DownloadAndSaveAsync(LocalDate date, CancellationToken ct);
}
