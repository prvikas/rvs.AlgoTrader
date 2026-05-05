using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Stores execution-level broker credentials keyed by <see cref="BrokerName"/>.
///
/// DESIGN:
///   - PK = BrokerName (natural key).  One row per broker, shared across strategy instances.
///   - StrategyInstanceId is nullable with NO FK constraint (diagnostic only).
///   - MarketTimezoneId is the IANA timezone of the exchange this broker operates on.
///     This makes multi-market trading possible: Zerodha (IST) and IBKR (ET) can run
///     simultaneously without any config change or app restart.
///
/// Adding a new broker:
///   INSERT INTO broker_credentials (broker_name, market_timezone_id, ...)
///   VALUES ('IBKR', 'America/New_York', ...);
///   -- No code change, no appsettings.json change, no restart needed.
/// </summary>
public class BrokerCredential
{
    /// <summary>Natural PK. Examples: "Zerodha", "Upstox", "IBKR", "IGGroup".</summary>
    public string BrokerName { get; set; } = string.Empty;

    /// <summary>
    /// IANA timezone id of the exchange this broker operates on.
    /// Examples:
    ///   "Asia/Kolkata"      → NSE / BSE (India)
    ///   "America/New_York"  → NYSE / NASDAQ (US)
    ///   "Europe/London"     → LSE (UK)
    ///   "Asia/Singapore"    → SGX (Singapore)
    ///
    /// Stored in DB so multiple brokers across overlapping markets can co-exist
    /// without restarting the application or changing appsettings.json.
    /// </summary>
    public string MarketTimezoneId { get; set; } = "Asia/Kolkata";

    /// <summary>
    /// Optional back-reference to the strategy instance that last updated this row.
    /// No FK constraint — credentials outlive individual strategy instances.
    /// </summary>
    public Guid? StrategyInstanceId { get; set; }

    public string?     BrokerToken  { get; set; }
    public Exchange    Exchange     { get; set; } = Exchange.NSE;
    public ProductType ProductType  { get; set; } = ProductType.MIS;
    public int         LotSize      { get; set; } = 1;
}
