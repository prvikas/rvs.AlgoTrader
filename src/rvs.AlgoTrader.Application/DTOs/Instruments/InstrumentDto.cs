namespace rvs.AlgoTrader.Application.DTOs.Instruments;

/// <summary>
/// Canonical instrument DTO — one row per real-world instrument, shared across all brokers.
/// BrokerTokens contains each broker's native token for this instrument (only populated brokers appear).
/// Example: { "Zerodha": "738561", "Upstox": "NSE_EQ|INE002A01018", "MStock": "3045" }
/// </summary>
public record InstrumentDto(
    Guid     Id,
    string   InternalSymbol,
    string   TradingSymbol,
    string   Name,
    string   Exchange,
    string   InstrumentType,
    string?  Underlying,
    decimal? StrikePrice,
    string?  OptionType,
    DateOnly? Expiry,
    int      LotSize,
    decimal  TickSize,
    bool     IsActive,
    IReadOnlyDictionary<string, string> BrokerTokens);

public record SymbolDataPreferencesDto(
    Guid Id, string InternalSymbol, string[] Timeframes, DateOnly FromDate,
    int Priority, bool IsActive, DateTimeOffset UpdatedAt);

public record UpdateSymbolDataPreferencesDto(
    string[]? Timeframes, DateOnly? FromDate, int? Priority, bool? IsActive);
