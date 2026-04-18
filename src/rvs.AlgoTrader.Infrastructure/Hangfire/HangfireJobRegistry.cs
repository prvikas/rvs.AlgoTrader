using Hangfire;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Infrastructure.Constants;
using rvs.AlgoTrader.Infrastructure.Jobs;

namespace rvs.AlgoTrader.Infrastructure.Hangfire;

/// <summary>
/// Registers all recurring Hangfire jobs at startup.
/// All times in IST (Hangfire cron is UTC — convert: IST = UTC+5:30)
/// </summary>
public static class HangfireJobRegistry
{
    public static void RegisterJobs(ILogger logger)
    {
        // Instrument refresh: daily at 8:00 AM IST = 2:30 UTC
        RecurringJob.AddOrUpdate<InstrumentRefreshJob>(
            "instrument-refresh", j => j.ExecuteAsync(CancellationToken.None),
            "30 2 * * 1-5"); // weekdays at 2:30 UTC

        // Historical download: daily at 6:00 AM IST = 0:30 UTC
        RecurringJob.AddOrUpdate<HistoricalDownloadJob>(
            "historical-download", j => j.ExecuteAsync(CancellationToken.None),
            "30 0 * * 1-5");

        // Strategy scheduler: every minute during market hours (9:00-15:35 IST = 3:30-10:05 UTC)
        RecurringJob.AddOrUpdate<StrategySchedulerJob>(
            "strategy-scheduler", j => j.ExecuteAsync(CancellationToken.None),
            "* 3-10 * * 1-5");

        // Reconciliation: every 15 minutes during market hours
        RecurringJob.AddOrUpdate<ReconciliationJob>(
            "reconciliation-zerodha", j => j.ExecuteAsync(BrokerNames.Zerodha, CancellationToken.None),
            "*/15 3-10 * * 1-5");

        // Monitoring alerts: every 5 minutes
        RecurringJob.AddOrUpdate<MonitoringAlertJob>(
            "monitoring-alerts", j => j.ExecuteAsync(CancellationToken.None),
            "*/5 * * * *");

        // EOD report: 3:45 PM IST = 10:15 UTC
        RecurringJob.AddOrUpdate<EodReportJob>(
            "eod-report", j => j.ExecuteAsync(CancellationToken.None),
            "15 10 * * 1-5");

        // Market breadth: after market close 4:30 PM IST = 11:00 UTC (idempotent — safe to re-run)
        RecurringJob.AddOrUpdate<BreadthCalculatorJob>(
            "breadth-calculator", j => j.RunAsync(CancellationToken.None),
            "0 11 * * 1-5");

        // Option chain EOD snapshot: 3:45 PM IST = 10:15 UTC (same slot as EOD report)
        // Records live OC snapshot per running strategy symbol for historical backtest use (FIB-5).
        RecurringJob.AddOrUpdate<OptionChainSnapshotJob>(
            "option-chain-snapshot", j => j.RunAsync(CancellationToken.None),
            "15 10 * * 1-5");

        // P9: Equity screener — after market close 5:00 PM IST = 11:30 UTC
        RecurringJob.AddOrUpdate<ScreenerJob>(
            "equity-screener", j => j.RunAsync(CancellationToken.None),
            "30 11 * * 1-5");

        // P9: F&O expiry auto-seed — 1st of every month at 1:00 AM IST = 19:30 UTC (prev day)
        RecurringJob.AddOrUpdate<FnoExpirySeedJob>(
            "fno-expiry-seed", j => j.RunAsync(CancellationToken.None),
            "30 19 1 * *");

        logger.LogInformation("[HangfireJobRegistry] All recurring jobs registered");
    }
}
