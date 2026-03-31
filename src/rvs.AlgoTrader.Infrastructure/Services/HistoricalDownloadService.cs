using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Infrastructure.Constants;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Downloads historical OHLCV data from broker and persists to candles table.
/// Called by HistoricalDownloadJob (Hangfire) and on-demand via API.
/// SHA-256 data hash computed per download for reproducibility.
/// </summary>
public class HistoricalDownloadService(
    IBrokerClientFactory brokerFactory,
    ICandleRepository candleRepo,
    IInstrumentRepository instrumentRepo,
    ILogger<HistoricalDownloadService> logger) : IHistoricalDownloadService
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    public async Task<DownloadResult> DownloadAsync(
        string internalSymbol, string brokerName, string timeframe,
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        logger.LogInformation("[HistoricalDownload] {Symbol} {Tf} from {From} to {To} via {Broker}",
            internalSymbol, timeframe, from, to, brokerName);

        var instrument = await instrumentRepo.GetByInternalSymbolAsync(internalSymbol, ct);

        // MStock scrip master appends "-EQ" to NSE/BSE equity symbols (e.g. AXISBANK-EQ).
        // Try the suffixed form as a fallback so strategies using the canonical symbol
        // ("NSE:AXISBANK") still resolve after an instrument refresh that stored "NSE:AXISBANK-EQ".
        if (instrument == null && !internalSymbol.EndsWith("-EQ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = internalSymbol.Split(':', 2);
            if (parts.Length == 2)
            {
                var fallbackSymbol = $"{parts[0]}:{parts[1]}-EQ";
                instrument = await instrumentRepo.GetByInternalSymbolAsync(fallbackSymbol, ct);
                if (instrument != null)
                    logger.LogDebug(
                        "[HistoricalDownload] Resolved '{Original}' → '{Fallback}' via -EQ suffix lookup",
                        internalSymbol, fallbackSymbol);
            }
        }

        if (instrument == null)
            return new DownloadResult(false, 0, null, $"Instrument '{internalSymbol}' not found. " +
                "Run instrument refresh (POST /api/instruments/refresh or the Instruments wizard) to populate the instruments table.");

        var brokerToken = brokerName switch
        {
            BrokerNames.Zerodha => instrument.ZerodhaToken,
            BrokerNames.Upstox  => instrument.UpstoxToken,
            BrokerNames.MStock  => instrument.MStockToken,
            _                   => instrument.ZerodhaToken
        };

        if (string.IsNullOrEmpty(brokerToken))
            return new DownloadResult(false, 0, null, $"No broker token for {brokerName}:{internalSymbol}");

        var mdClient = brokerFactory.GetMarketDataClient(brokerName);

        // Ask the broker for its per-request limit and chunk the date range accordingly.
        var chunkDays = mdClient.GetHistoricalQueryLimits(timeframe).MaxCalendarDaysPerRequest;
        var chunks    = BuildDateChunks(from, to, chunkDays);

        logger.LogInformation(
            "[HistoricalDownload] Fetching {Chunks} chunk(s) of up to {Days} days each ({Tf})",
            chunks.Count, chunkDays, timeframe);

        var allBars = new List<OhlcvBar>();
        foreach (var (chunkFrom, chunkTo) in chunks)
        {
            ct.ThrowIfCancellationRequested();
            var query = new HistoricalDataQuery(brokerToken, internalSymbol, timeframe, chunkFrom, chunkTo);
            try
            {
                var chunk = await mdClient.GetHistoricalDataAsync(query, ct);
                allBars.AddRange(chunk);
                logger.LogDebug(
                    "[HistoricalDownload] Chunk {From}–{To}: {Count} bars", chunkFrom, chunkTo, chunk.Count);
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(ex, "[HistoricalDownload] Broker HTTP error for {Symbol}/{Tf} chunk {From}–{To}: {Message}",
                    internalSymbol, timeframe, chunkFrom, chunkTo, ex.Message);
                return new DownloadResult(false, 0, null,
                    $"Broker request failed: {ex.Message}. " +
                    "Check that the broker session is authenticated (POST /api/broker/authenticate) " +
                    "and the instrument has a valid MStock token.");
            }
        }

        if (allBars.Count == 0)
            return new DownloadResult(false, 0, null,
                $"Broker returned 0 bars for {internalSymbol}/{timeframe} ({from}–{to}). " +
                "The date range may have no trading data, or the broker token may be incorrect.");

        // Convert to ClosedCandle value objects
        var candles = allBars.Select(b => new ClosedCandle(
            b.InternalSymbol,
            b.Timeframe,
            b.OpenTime,
            b.OpenTime.Plus(TimeframeToInterval(b.Timeframe)),
            b.Open, b.High, b.Low, b.Close, b.Volume
        )).ToList();

        await candleRepo.BulkInsertAsync(candles, ct);

        // Compute SHA-256 hash for reproducibility
        var hash = ComputeDataHash(allBars);

        logger.LogInformation("[HistoricalDownload] Downloaded {Count} bars for {Symbol}/{Tf}. Hash: {Hash}",
            candles.Count, internalSymbol, timeframe, hash[..12]);

        return new DownloadResult(true, candles.Count, hash, null);
    }

    private static string ComputeDataHash(IReadOnlyList<OhlcvBar> bars)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var b in bars)
            sb.Append($"{b.OpenTime.ToInstant().ToUnixTimeMilliseconds()},{b.Open},{b.High},{b.Low},{b.Close},{b.Volume};");

        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static NodaTime.Duration TimeframeToInterval(string tf) => tf switch
    {
        "1m"  => NodaTime.Duration.FromMinutes(1),
        "3m"  => NodaTime.Duration.FromMinutes(3),
        "5m"  => NodaTime.Duration.FromMinutes(5),
        "15m" => NodaTime.Duration.FromMinutes(15),
        "30m" => NodaTime.Duration.FromMinutes(30),
        "60m" => NodaTime.Duration.FromMinutes(60),
        "1d"  => NodaTime.Duration.FromHours(6.25), // 9:15 to 15:30
        _     => NodaTime.Duration.FromMinutes(1)
    };

    /// <summary>Splits [from, to] into non-overlapping windows of at most <paramref name="chunkDays"/> days.</summary>
    private static List<(DateOnly From, DateOnly To)> BuildDateChunks(DateOnly from, DateOnly to, int chunkDays)
    {
        var chunks = new List<(DateOnly, DateOnly)>();
        var cursor = from;
        while (cursor <= to)
        {
            var end = cursor.AddDays(chunkDays - 1);
            if (end > to) end = to;
            chunks.Add((cursor, end));
            cursor = end.AddDays(1);
        }
        return chunks;
    }
}

