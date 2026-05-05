namespace rvs.AlgoTrader.Domain.Interfaces;

/// <summary>
/// Resolves the correct <see cref="IMarketTimezone"/> for a given broker at runtime.
///
/// The timezone is read from the <c>broker_credentials.market_timezone_id</c> DB column,
/// so it is always up-to-date without restarting the application.
///
/// Usage in services / jobs:
/// <code>
/// // Inject IBrokerTimezoneResolver
/// var tz = await _brokerTimezoneResolver.ResolveAsync(brokerName, ct);
/// var sessionOpen = tz.Zone.AtStrictly(tz.TodayInMarket.At(new LocalTime(9, 15)));
/// </code>
///
/// Because each broker carries its own timezone, you can trade Zerodha (IST)
/// and IBKR (America/New_York) simultaneously — no config change, no restart.
/// </summary>
public interface IBrokerTimezoneResolver
{
    /// <summary>
    /// Resolves timezone for the given broker.
    /// Reads from DB; implementations should cache with a short TTL.
    /// </summary>
    Task<IMarketTimezone> ResolveAsync(string brokerName, CancellationToken ct = default);

    /// <summary>Synchronous overload for non-async call-sites (uses cache only).</summary>
    IMarketTimezone Resolve(string brokerName);

    /// <summary>Clears the timezone cache (call after updating broker_credentials row).</summary>
    void InvalidateCache(string brokerName);
}
