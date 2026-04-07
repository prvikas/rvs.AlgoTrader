using Hangfire;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Infrastructure.Services;

namespace rvs.AlgoTrader.Infrastructure.Jobs;

/// <summary>
/// Hangfire EOD job — computes and persists the market-breadth snapshot for the previous
/// trading day.  Scheduled to run after market close (typically 16:30 IST).
/// </summary>
public class BreadthCalculatorJob(
    IMarketBreadthService breadth,
    IMarketCalendarService calendar,
    IClock clock,
    INseBhavcopyCandleSource bhavcopy,
    ILogger<BreadthCalculatorJob> log)
{
    /// <summary>
    /// Compute breadth for today (if market was open) or for the last trading day.
    /// Safe to re-run — <see cref="IMarketBreadthService.ComputeAndSaveAsync"/> is idempotent.
    /// </summary>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 300, 600])]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var today = clock.NowInstant()
            .InZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"])
            .Date;

        // Walk back to the last trading day (skip weekends and holidays)
        var targetDate = today;
        int tries = 0;
        while (!calendar.IsTradingDay(targetDate) && tries++ < 10)
            targetDate = targetDate.PlusDays(-1);

        if (!calendar.IsTradingDay(targetDate))
        {
            log.LogWarning("BreadthCalculatorJob: could not find a trading day near {Today}. Skipping.", today);
            return;
        }

        log.LogInformation("BreadthCalculatorJob: downloading Bhavcopy for {Date}", targetDate);
        var downloaded = await bhavcopy.DownloadAndSaveAsync(targetDate, ct);
        log.LogInformation("BreadthCalculatorJob: Bhavcopy downloaded {Count} symbols for {Date}", downloaded, targetDate);

        log.LogInformation("BreadthCalculatorJob: computing breadth for {Date}", targetDate);
        var result = await breadth.ComputeAndSaveAsync(targetDate, ct);
        log.LogInformation("BreadthCalculatorJob: done — regime={Regime}, symbols={Total}", result.Regime, result.TotalSymbols);
    }
}
