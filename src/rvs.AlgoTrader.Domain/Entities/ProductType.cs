namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Master product types table. Seed data: MIS, NRML, CNC, BO, CO, DAY, GTC.
/// Replaces hardcoded enum strings with normalized reference data.
/// </summary>
public class ProductType
{
    /// <summary>Database PK: SMALLINT GENERATED ALWAYS AS IDENTITY.</summary>
    public short Id { get; set; }

    /// <summary>Product type code (e.g. "MIS", "NRML", "CNC", "DAY").</summary>
    public string Code { get; set; } = string.Empty;
}
