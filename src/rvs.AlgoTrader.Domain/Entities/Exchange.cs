namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Master exchanges table. Seed data: NSE, BSE, NFO, MCX, CDS (India); NYSE, NASDAQ (US); LSE (UK).
/// Each exchange has a code, market code (IN/US/UK/SG), and timezone.
/// </summary>
public class Exchange
{
    /// <summary>Database PK: SMALLINT GENERATED ALWAYS AS IDENTITY.</summary>
    public short Id { get; set; }

    /// <summary>Exchange code (e.g. "NSE", "NASDAQ", "LSE").</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Market code (2-char): IN | US | UK | SG | JP | EU.</summary>
    public string MarketCode { get; set; } = string.Empty;

    /// <summary>FK to timezones. Operating timezone for this exchange.</summary>
    public short TimezoneId { get; set; }

    // Navigation
    public virtual Timezone? Timezone { get; set; }
}
