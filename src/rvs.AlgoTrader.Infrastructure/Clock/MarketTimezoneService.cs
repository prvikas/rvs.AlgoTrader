using Microsoft.Extensions.Options;
using NodaTime;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Clock;

/// <summary>
/// Default implementation of <see cref="IMarketTimezone"/>.
/// Reads the IANA timezone id from <see cref="MarketTimezoneOptions"/> (appsettings.json)
/// so the application works for any exchange timezone without code changes.
/// </summary>
public sealed class MarketTimezoneService : IMarketTimezone
{
    private readonly IClock _nodaClock;

    public MarketTimezoneService(IOptions<MarketTimezoneOptions> options, IClock nodaClock)
    {
        _nodaClock = nodaClock;
        var tzId = options.Value?.TimeZoneId;
        if (string.IsNullOrWhiteSpace(tzId))
            throw new InvalidOperationException(
                "MarketTimezone:TimeZoneId is not configured in appsettings.json. " +
                "Example: \"Asia/Kolkata\" for India, \"America/New_York\" for US.");

        Zone = DateTimeZoneProviders.Tzdb[tzId];
    }

    /// <inheritdoc />
    public DateTimeZone Zone { get; }

    /// <inheritdoc />
    public ZonedDateTime Now => _nodaClock.GetCurrentInstant().InZone(Zone);

    /// <inheritdoc />
    public ZonedDateTime ToMarketTime(Instant utcInstant) => utcInstant.InZone(Zone);

    /// <inheritdoc />
    public ZonedDateTime ToMarketTime(DateTime utcDateTime)
    {
        var instant = Instant.FromDateTimeUtc(
            utcDateTime.Kind == DateTimeKind.Utc
                ? utcDateTime
                : DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc));
        return instant.InZone(Zone);
    }

    /// <inheritdoc />
    public LocalDate TodayInMarket => Now.Date;
}
