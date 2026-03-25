using NodaTime;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Defines the universe of instruments the system will download and store.
/// Only instruments matching entries in this table are persisted during broker refresh.
///
/// Categories:
///   NSE_EQUITY          — NSE-listed equity symbol (e.g. "RELIANCE"). Equities NOT in this
///                         table are discarded during InstrumentRefreshService.
///   OPTIONS_UNDERLYING  — Underlying symbol for NFO/BFO options and futures tracking
///                         (e.g. "NIFTY", "BANKNIFTY"). Options whose underlying does not
///                         appear here are discarded; expiry filter also applies.
///
/// Indexes (instrument_type = INDEX) are ALWAYS included regardless of this table —
/// there are only ~100 NSE indexes and they are small in number.
///
/// The expiry lookahead for options is stored in app_config:
///   key = "InstrumentFilter:NfoExpiryWeeks", value = "4" (weeks from today).
/// </summary>
public class InstrumentUniverse
{
    public Guid   Id        { get; set; }
    /// <summary>Trading symbol exactly as used by NSE (e.g. "RELIANCE", "NIFTY", "BANKNIFTY").</summary>
    public string Symbol    { get; set; } = string.Empty;
    /// <summary>Exchange this symbol belongs to. For equities and indexes: "NSE". For option underlyings: "NFO".</summary>
    public string Exchange  { get; set; } = string.Empty;
    /// <summary>NSE_EQUITY | OPTIONS_UNDERLYING</summary>
    public string Category  { get; set; } = string.Empty;
    public bool   IsActive  { get; set; } = true;
    public Instant CreatedAt { get; set; }

    public InstrumentUniverse() { }
}
