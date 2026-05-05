namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Broker-Exchange-ProductType configuration: which product types each broker supports on each exchange,
/// and what the default lot size is for that combination.
/// Example: Zerodha/NSE/MIS has default lot size 1.
/// </summary>
public class BrokerExchangeConfig
{
    /// <summary>Database PK: UUID DEFAULT gen_random_uuid().</summary>
    public Guid Id { get; set; }

    /// <summary>FK to brokers.</summary>
    public short BrokerId { get; set; }

    /// <summary>FK to exchanges.</summary>
    public short ExchangeId { get; set; }

    /// <summary>FK to product_types.</summary>
    public short ProductTypeId { get; set; }

    /// <summary>Default lot size for this broker/exchange/product combination.</summary>
    public int DefaultLotSize { get; set; } = 1;

    /// <summary>Whether this configuration is currently active.</summary>
    public bool IsActive { get; set; } = true;

    // Navigation
    public virtual Broker? Broker { get; set; }
    public virtual Exchange? Exchange { get; set; }
    public virtual ProductType? ProductType { get; set; }
}
