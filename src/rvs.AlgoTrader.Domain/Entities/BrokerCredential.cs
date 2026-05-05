using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Stores execution-level broker credentials (exchange, product type, lot size, token)
/// keyed by <see cref="BrokerName"/>.
///
/// DESIGN NOTE: This table is intentionally INDEPENDENT of <see cref="StrategyInstance"/>.
/// A single broker credential (e.g. "Zerodha" – NSE/NRML/LotSize=1) is reusable across
/// any number of strategy instances.  The previous design mistakenly used StrategyInstanceId
/// as the primary key, coupling credentials to a single strategy run.
///
/// <see cref="StrategyInstanceId"/> is kept as a nullable denorm column for diagnostic
/// queries only; it carries NO foreign key constraint.
/// </summary>
public class BrokerCredential
{
    /// <summary>Broker name acts as the natural key (e.g. "Zerodha", "Upstox", "MStock").</summary>
    public string BrokerName { get; set; } = string.Empty;

    /// <summary>
    /// Optional back-reference to the strategy instance that last upserted this row.
    /// No FK constraint — credentials outlive individual strategy instances.
    /// </summary>
    public Guid? StrategyInstanceId { get; set; }

    public string? BrokerToken    { get; set; }
    public Exchange   Exchange     { get; set; } = Exchange.NSE;
    public ProductType ProductType { get; set; } = ProductType.MIS;
    public int        LotSize      { get; set; } = 1;
}
