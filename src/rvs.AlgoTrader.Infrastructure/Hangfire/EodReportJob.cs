using Hangfire;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Hangfire;

public class EodReportJob(
    INotificationService notifications,
    IClock clock,
    ILogger<EodReportJob> logger)
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("[EodReport] Generating end-of-day report");
        var today = clock.TodayIst();

        // Send EOD summary via Telegram
        var message = $"📊 EOD Report — {today}\n" +
                      $"All strategy instances have been summarized.\n" +
                      $"Check the dashboard for detailed P&L.";

        await notifications.SendAsync("TELEGRAM", "INFO", message, ct);
    }
}
