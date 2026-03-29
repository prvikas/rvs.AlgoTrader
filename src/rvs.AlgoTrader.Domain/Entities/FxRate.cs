using NodaTime;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Daily FX rate snapshot used to convert cross-currency P&amp;L to INR.
/// Source: RBI reference rates (imported via IFxRateProvider).
/// Example: BaseCurrency="USD", QuoteCurrency="INR", Rate=83.50 on 2025-03-29.
/// </summary>
public class FxRate
{
    public Guid      Id             { get; set; }
    public string    BaseCurrency   { get; set; } = string.Empty;   // e.g. "USD"
    public string    QuoteCurrency  { get; set; } = string.Empty;   // e.g. "INR"
    public LocalDate Date           { get; set; }
    public decimal   Rate           { get; set; }   // BaseCurrency per 1 QuoteCurrency
    public string    Source         { get; set; } = "RBI";
    public Instant   RecordedAt     { get; set; }
}
