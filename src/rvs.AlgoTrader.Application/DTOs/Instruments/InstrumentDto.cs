namespace rvs.AlgoTrader.Application.DTOs.Instruments;

public record InstrumentDto(
    Guid Id, string InternalSymbol, string BrokerToken, string BrokerName,
    string TradingSymbol, string Exchange, string InstrumentType,
    string? Underlying, decimal? StrikePrice, string? OptionType,
    DateOnly? Expiry, int LotSize, decimal TickSize, bool IsActive);

public record SymbolDataPreferencesDto(
    Guid Id, string InternalSymbol, string[] Timeframes, DateOnly FromDate,
    int Priority, bool IsActive, DateTimeOffset UpdatedAt);

public record UpdateSymbolDataPreferencesDto(
    string[]? Timeframes, DateOnly? FromDate, int? Priority, bool? IsActive);
