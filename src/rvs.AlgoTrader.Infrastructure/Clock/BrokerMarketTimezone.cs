using NodaTime;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Clock;

/// <summary>
/// Concrete <see cref="IMarketTimezone"/> scoped to a single broker's timezone.
/// Constructed by <see cref="BrokerTimezoneResolver"/> from the DB row value.
/// </summary>
public sealed class BrokerMarketTimezone : IMarketTimezone
{
    private readonly NodaTime.IClock _clock;

    public BrokerMarketTimezone(string ianaTimezoneId, NodaTime.IClock clock)
    {
        _clock     = clock;
        TimezoneId = ianaTimezoneId;
        Zone       = DateTimeZoneProviders.Tzdb[ianaTimezoneId];
    }

    public string         TimezoneId    { get; }
    public DateTimeZone   Zone          { get; }
    public ZonedDateTime  Now           => _clock.GetCurrentInstant().InZone(Zone);
    public LocalDate      TodayInMarket => Now.Date;

    public ZonedDateTime ToMarketTime(Instant utcInstant) =>
        utcInstant.InZone(Zone);

    public ZonedDateTime ToMarketTime(DateTime utcDateTime)
    {
        var instant = Instant.FromDateTimeUtc(
            utcDateTime.Kind == DateTimeKind.Utc
                ? utcDateTime
                : DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc));
        return instant.InZone(Zone);
    }
}
