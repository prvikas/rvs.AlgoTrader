using NodaTime;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Domain.Entities;

public class Instrument
{
    public Guid Id { get; set; }
    public string InternalSymbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TradingSymbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public InstrumentType InstrumentType { get; set; }
    public string? Underlying { get; set; }
    public decimal? StrikePrice { get; set; }
    public OptionType? OptionType { get; set; }
    public LocalDate? Expiry { get; set; }
    public int LotSize { get; set; }
    public decimal TickSize { get; set; }
    public bool IsActive { get; set; }

    // Per-broker tokens — stored separately so any broker can resolve a token
    public string? ZerodhaToken { get; set; }
    public string? UpstoxToken { get; set; }
    public string? MStockToken { get; set; }

    public Instant LastRefreshedAt { get; set; }

    // Compatibility aliases
    public string DisplayName => Name.Length > 0 ? Name : TradingSymbol;
    public int Lot => LotSize;
    public Instant UpdatedAt => LastRefreshedAt;
    public Instant RefreshedAt => LastRefreshedAt;
    /// <summary>The token for a single-broker row (backwards compat with old schema).</summary>
    public string BrokerToken => ZerodhaToken ?? UpstoxToken ?? MStockToken ?? string.Empty;
    public string BrokerName =>
        ZerodhaToken != null ? "Zerodha" :
        UpstoxToken  != null ? "Upstox"  :
        MStockToken  != null ? "MStock"  : string.Empty;

    // EF Core requires parameterless constructor
    public Instrument() { }
}
