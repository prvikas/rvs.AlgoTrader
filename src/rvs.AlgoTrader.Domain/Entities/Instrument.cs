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

    /// <summary>
    /// Currency in which the instrument is quoted.
    /// INR for NSE/BSE equities and derivatives; USD for MCX commodities (e.g. crude, gold).
    /// Defaults to "INR" — must be set explicitly for cross-currency instruments.
    /// Used by BacktestEngine and LiveExecutionEngine to convert P&amp;L to base currency.
    /// </summary>
    public string QuoteCurrency      { get; set; } = "INR";

    /// <summary>Currency in which the instrument settles (usually same as QuoteCurrency).</summary>
    public string SettlementCurrency { get; set; } = "INR";

    /// <summary>
    /// Multiplier applied to price before P&amp;L calculation.
    /// 1 for equities. For MCX crude oil (USD-per-barrel × LotSize × Multiplier = INR P&amp;L).
    /// </summary>
    public decimal PriceMultiplier   { get; set; } = 1m;

    public Instant LastRefreshedAt { get; set; }

    // Display helpers
    public string DisplayName => Name.Length > 0 ? Name : TradingSymbol;
    public int Lot => LotSize;
    public Instant UpdatedAt => LastRefreshedAt;
    public Instant RefreshedAt => LastRefreshedAt;

    /// <summary>Returns the broker token for a specific broker, or null if not available.</summary>
    public string? GetBrokerToken(string brokerName) => brokerName.ToLowerInvariant() switch
    {
        "zerodha" => ZerodhaToken,
        "upstox"  => UpstoxToken,
        "mstock"  => MStockToken,
        _         => null
    };

    // EF Core requires parameterless constructor
    public Instrument() { }
}
