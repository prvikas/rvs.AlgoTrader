using Hangfire;
using rvs.AlgoTrader.Domain.Constants;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Hangfire;

public class HistoricalDownloadJob(
    IHistoricalDownloadService downloadService,
    IStrategyInstanceRepository instanceRepo,
    IClock clock,
    ILogger<HistoricalDownloadJob> logger)
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    [AutomaticRetry(Attempts = 3)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("[HistoricalDownload] Starting daily download job");
        var today = clock.TodayIst();
        var instances = await instanceRepo.GetAllAsync(ct);
        var pairs = instances
            .Select(i => (
                Symbol: i.InternalSymbol,
                BrokerName: i.BrokerAccount?.Broker?.Name ?? BrokerNames.Default,
                Timeframe: i.Timeframe))
            .Distinct()
            .ToList();

        foreach (var pair in pairs)
        {
            try
            {
                // Download last 5 days to fill any gaps
                var from = today.Minus(Period.FromDays(5));
                var fromDate = new DateOnly(from.Year, from.Month, from.Day);
                var toDate = new DateOnly(today.Year, today.Month, today.Day);
                var result = await downloadService.DownloadAsync(pair.Symbol, pair.BrokerName, pair.Timeframe, fromDate, toDate, ct);
                logger.LogInformation("[HistoricalDownload] {Symbol}/{Tf}: {Count} bars, hash={Hash}",
                    pair.Symbol, pair.Timeframe, result.BarCount, result.DataHash?[..12]);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[HistoricalDownload] Failed for {Symbol}/{Tf}", pair.Symbol, pair.Timeframe);
            }
        }
    }
}
