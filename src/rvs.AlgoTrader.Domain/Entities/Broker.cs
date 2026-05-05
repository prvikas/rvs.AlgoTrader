namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Master broker table. Seed data: Zerodha, Upstox, MStock, Dhan, IBKR, Alpaca, IGGroup.
/// Each broker has a default timezone (e.g. Zerodha → Asia/Kolkata, IBKR → America/New_York).
/// </summary>
public class Broker
{
    /// <summary>Database PK: SMALLINT GENERATED ALWAYS AS IDENTITY.</summary>
    public short Id { get; set; }

    /// <summary>Canonical broker name (e.g. "Zerodha", "IBKR"). Used as unique key.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Display name for UI (e.g. "Interactive Brokers"). Optional.</summary>
    public string? DisplayName { get; set; }

    /// <summary>FK to timezones. Default timezone for this broker's exchanges.</summary>
    public short TimezoneId { get; set; }

    /// <summary>Whether this broker is currently active for strategy deployment.</summary>
    public bool IsActive { get; set; }

    // Navigation
    public virtual Timezone? Timezone { get; set; }
}
