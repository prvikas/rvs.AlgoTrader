namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// IANA timezone master table. Seed data: Asia/Kolkata, America/New_York, Europe/London, etc.
/// Referenced by brokers and exchanges for time handling across markets.
/// </summary>
public class Timezone
{
    /// <summary>Database PK: SMALLINT GENERATED ALWAYS AS IDENTITY.</summary>
    public short Id { get; set; }

    /// <summary>IANA timezone ID (e.g. "Asia/Kolkata", "America/New_York").</summary>
    public string IanaId { get; set; } = string.Empty;
}
