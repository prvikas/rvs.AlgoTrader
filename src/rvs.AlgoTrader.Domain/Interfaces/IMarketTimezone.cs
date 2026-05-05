using NodaTime;

namespace rvs.AlgoTrader.Domain.Interfaces;

/// <summary>
/// Provides timezone-aware time helpers scoped to a specific broker's market.
///
/// Do NOT inject this as a singleton that reads from appsettings.json.
/// Instead, resolve it per-broker at runtime via <see cref="IBrokerTimezoneResolver"/>:
///
///   var tz = _brokerTimezoneResolver.Resolve(brokerName);
///   var marketNow = tz.Now;  // correct time for that broker's exchange
///
/// This supports simultaneous multi-market trading (e.g. Zerodha at IST + IBKR at ET)
/// without any config change or app restart.
/// </summary>
public interface IMarketTimezone
{
    /// <summary>The broker's IANA timezone id (e.g. "Asia/Kolkata").</summary>
    string TimezoneId { get; }

    /// <summary>The NodaTime DateTimeZone resolved from <see cref="TimezoneId"/>.</summary>
    DateTimeZone Zone { get; }

    /// <summary>Current wall-clock instant in the broker's market timezone.</summary>
    ZonedDateTime Now { get; }

    /// <summary>Converts a UTC Instant to the broker's local ZonedDateTime.</summary>
    ZonedDateTime ToMarketTime(Instant utcInstant);

    /// <summary>
    /// Convenience overload: converts a UTC DateTime (from EF Core) to market local time.
    /// </summary>
    ZonedDateTime ToMarketTime(DateTime utcDateTime);

    /// <summary>Today's date in the broker's market timezone.</summary>
    LocalDate TodayInMarket { get; }
}
