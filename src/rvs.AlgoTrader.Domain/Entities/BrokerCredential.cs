using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Broker-specific credentials and order routing configuration for a strategy instance.
/// Separated from StrategyInstance (definition) to follow SRP and isolate security concerns.
/// 1:1 relationship with StrategyInstance.
/// BrokerToken is encrypted at rest via repository layer.
/// </summary>
public class BrokerCredential
{
    public Guid StrategyInstanceId { get; set; }
    public StrategyInstance? StrategyInstance { get; set; }

    /// <summary>Broker instrument token (e.g., mStock nfo code) — encrypted at rest.</summary>
    public string? BrokerToken { get; set; }

    /// <summary>Trading exchange (NSE, BSE, NCDEX, etc.).</summary>
    public Exchange Exchange { get; set; } = Enums.Exchange.NSE;

    /// <summary>Order product type (MIS=intraday, CNC=delivery, etc.).</summary>
    public ProductType ProductType { get; set; } = Enums.ProductType.MIS;

    /// <summary>Order size in lots; defaults to 1.</summary>
    public int LotSize { get; set; } = 1;

    // EF Core requires parameterless constructor
    public BrokerCredential() { }

    public static BrokerCredential Create(Guid strategyInstanceId)
    {
        return new BrokerCredential
        {
            StrategyInstanceId = strategyInstanceId,
            BrokerToken = null,
            Exchange = Enums.Exchange.NSE,
            ProductType = Enums.ProductType.MIS,
            LotSize = 1,
        };
    }

    /// <summary>
    /// Updates order routing configuration.
    /// Called from command handlers when the user edits the instance.
    /// </summary>
    public void UpdateOrderRouting(Exchange exchange, ProductType productType, int lotSize)
    {
        Exchange = exchange;
        ProductType = productType;
        LotSize = lotSize > 0 ? lotSize : 1;
    }
}
