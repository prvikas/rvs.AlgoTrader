namespace rvs.AlgoTrader.Infrastructure.Clock;

/// <summary>
/// REMOVED — timezone is no longer stored in appsettings.json.
///
/// The market timezone is now stored per-broker in the <c>broker_credentials</c> table
/// (column: <c>market_timezone_id</c>), resolved at runtime via <see cref="IBrokerTimezoneResolver"/>.
///
/// This means:
///   - India (Zerodha) and US (IBKR) brokers can run simultaneously
///   - No app restart required when adding or changing a broker's timezone
///   - No appsettings.json change needed
///
/// To add a new broker, simply INSERT a row:
///   INSERT INTO broker_credentials (broker_name, market_timezone_id)
///   VALUES ('MyNewBroker', 'America/Chicago');
///
/// This file is kept as a tombstone to prevent re-introduction of the appsettings approach.
/// </summary>
[Obsolete("Do not use. Timezone is now per-broker in broker_credentials.market_timezone_id. Use IBrokerTimezoneResolver.", error: true)]
public sealed class MarketTimezoneOptions
{
    public const string Section = "MarketTimezone";
    public string TimeZoneId { get; set; } = string.Empty;
}
