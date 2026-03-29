using NodaTime;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Daily ATM IV snapshot for an underlying (NIFTY, BANKNIFTY, etc.).
/// Populated by a nightly job that reads the option chain and stores AtmIv.
/// Used by IOptionIvRankService to compute IV Rank and IV Percentile.
/// TimescaleDB hypertable — partitioned by date.
/// </summary>
public class OptionIvHistory
{
    public Guid      Id               { get; set; }
    public string    UnderlyingSymbol { get; set; } = string.Empty;
    public LocalDate Date             { get; set; }
    public decimal   AtmIv            { get; set; }   // annualised, e.g. 18.5 = 18.5%
    public Instant   RecordedAt       { get; set; }
}
