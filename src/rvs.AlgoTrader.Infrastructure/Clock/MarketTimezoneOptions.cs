namespace rvs.AlgoTrader.Infrastructure.Clock;

/// <summary>
/// Bound to <c>MarketTimezone</c> section in appsettings.json.
/// Example configuration:
/// <code>
/// "MarketTimezone": {
///   "TimeZoneId": "Asia/Kolkata"
/// }
/// </code>
/// For US brokers use "America/New_York", for UK brokers use "Europe/London".
/// </summary>
public sealed class MarketTimezoneOptions
{
    public const string Section = "MarketTimezone";

    /// <summary>IANA timezone identifier. Defaults to "Asia/Kolkata" for backward compatibility.</summary>
    public string TimeZoneId { get; set; } = "Asia/Kolkata";
}
