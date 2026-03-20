using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

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
        if (instrument == null)
            return new DownloadResult(false, 0, null, $"Instrument '{internalSymbol}' not found");

        var brokerToken = brokerName switch
        {
            "Zerodha" => instrument.ZerodhaToken,
            "Upstox" => instrument.UpstoxToken,
            "MStock" => instrument.MStockToken,
            _ => instrument.ZerodhaToken
        };

        if (string.IsNullOrEmpty(brokerToken))
            return new DownloadResult(false, 0, null, $"No broker token for {brokerName}:{internalSymbol}");

        var mdClient = brokerFactory.GetMarketDataClient(brokerName);
        var query = new HistoricalDataQuery(brokerToken, internalSymbol, timeframe, from, to);
        var bars = await mdClient.GetHistoricalDataAsync(query, ct);

        if (bars.Count == 0)
            return new DownloadResult(true, 0, null, "No data returned");

        // Convert to ClosedCandle value objects
        var candles = bars.Select(b => new ClosedCandle(
            b.InternalSymbol,
            b.Timeframe,
            b.OpenTime,
            b.OpenTime.Plus(TimeframeToInterval(b.Timeframe)),
            b.Open, b.High, b.Low, b.Close, b.Volume
        )).ToList();

        await candleRepo.BulkInsertAsync(candles, ct);

        // Compute SHA-256 hash for reproducibility
        var hash = ComputeDataHash(bars);

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
        "1m" => NodaTime.Duration.FromMinutes(1),
        "3m" => NodaTime.Duration.FromMinutes(3),
        "5m" => NodaTime.Duration.FromMinutes(5),
        "15m" => NodaTime.Duration.FromMinutes(15),
        "30m" => NodaTime.Duration.FromMinutes(30),
        "60m" => NodaTime.Duration.FromMinutes(60),
        "1d" => NodaTime.Duration.FromHours(6.25), // 9:15 to 15:30
        _ => NodaTime.Duration.FromMinutes(1)
    };
}

