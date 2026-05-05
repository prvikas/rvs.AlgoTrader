namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Child table: instrument → broker-specific tokens.
/// Replaces hardcoded zerodha_token, upstox_token, mstock_token columns on instruments.
/// Enables adding new brokers without ALTER TABLE.
/// </summary>
public class InstrumentBrokerToken
{
    /// <summary>FK to instruments. Part of composite PK.</summary>
    public Guid InstrumentId { get; set; }

    /// <summary>FK to brokers. Part of composite PK.</summary>
    public short BrokerId { get; set; }

    /// <summary>Broker-specific token for this instrument (e.g. Zerodha NSE token, Upstox token).</summary>
    public string Token { get; set; } = string.Empty;

    // Navigation
    public virtual Instrument? Instrument { get; set; }
    public virtual Broker? Broker { get; set; }
}
