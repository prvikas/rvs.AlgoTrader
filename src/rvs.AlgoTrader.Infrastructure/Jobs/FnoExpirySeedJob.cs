using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Jobs;

/// <summary>
/// Hangfire monthly job — auto-seeds NSE F&amp;O expiry events for the current
/// and next calendar year so strategies never run without expiry data.
/// Calls <see cref="IEventCalendarService.SeedFnoExpiriesAsync"/> which is idempotent.
/// </summary>
public class FnoExpirySeedJob(
    IEventCalendarService eventCalendar,
    IClock clock,
    ILogger<FnoExpirySeedJob> log)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var istNow     = clock.NowInstant().InZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"]);
        var thisYear   = istNow.Year;
        var nextYear   = thisYear + 1;

        log.LogInformation("[FnoExpirySeedJob] Seeding F&O expiries for {Year} and {NextYear}", thisYear, nextYear);

        await eventCalendar.SeedFnoExpiriesAsync(thisYear, ct);
        await eventCalendar.SeedFnoExpiriesAsync(nextYear, ct);

        log.LogInformation("[FnoExpirySeedJob] F&O expiry seeding complete");
    }
}
