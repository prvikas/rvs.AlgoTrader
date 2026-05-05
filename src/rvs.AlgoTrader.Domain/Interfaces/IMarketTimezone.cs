using NodaTime;

namespace rvs.AlgoTrader.Domain.Interfaces;

/// <summary>
/// Provides the market/exchange timezone for time-sensitive calculations
/// (session windows, EOD resets, candle aggregation, etc.).
///
/// Register a concrete implementation via DI and configure the IANA timezone id
/// in appsettings.json under <c>MarketTimezone:TimeZoneId</c>.
///
/// Examples:
///   India (NSE/BSE):   "Asia/Kolkata"
///   US (NYSE/NASDAQ):  "America/New_York"
///   UK (LSE):          "Europe/London"
///   Singapore (SGX):   "Asia/Singapore"
/// </summary>
public interface IMarketTimezone
{
    /// <summary>The configured IANA timezone (e.g. "Asia/Kolkata").</summary>
    DateTimeZone Zone { get; }

    /// <summary>Returns the current wall-clock time in the market timezone.</summary>
    ZonedDateTime Now { get; }

    /// <summary>Converts a UTC <see cref="Instant"/> to the market's local <see cref="ZonedDateTime"/>.</summary>
    ZonedDateTime ToMarketTime(Instant utcInstant);

    /// <summary>
    /// Converts a UTC <see cref="DateTime"/> (Kind=Utc) to the market's local <see cref="ZonedDateTime"/>.
    /// Convenience overload used by repositories and services that receive DateTime from EF Core.
    /// </summary>
    ZonedDateTime ToMarketTime(DateTime utcDateTime);

    /// <summary>Returns today's date in the market timezone.</summary>
    LocalDate TodayInMarket { get; }
}
