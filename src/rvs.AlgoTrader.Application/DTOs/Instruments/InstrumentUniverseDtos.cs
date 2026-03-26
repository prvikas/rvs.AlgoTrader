namespace rvs.AlgoTrader.Application.DTOs.Instruments;

/// <summary>One row from the instrument_universe table.</summary>
public record InstrumentUniverseDto(
    Guid   Id,
    string Symbol,
    string Exchange,
    string Category,
    bool   IsActive,
    DateTimeOffset CreatedAt);

/// <summary>NSE_EQUITY | OPTIONS_UNDERLYING</summary>
public record CreateInstrumentUniverseRequest(
    string Symbol,
    string Exchange,
    string Category);

public record UpdateInstrumentUniverseRequest(
    string? Symbol,
    string? Exchange,
    string? Category,
    bool?   IsActive);
