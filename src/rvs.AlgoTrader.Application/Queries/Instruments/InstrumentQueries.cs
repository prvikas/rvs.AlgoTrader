using MediatR;
using rvs.AlgoTrader.Application.DTOs.Instruments;
using rvs.AlgoTrader.Application.DTOs.Common;

namespace rvs.AlgoTrader.Application.Queries.Instruments;

public record GetInstrumentSearchQuery(string Query, int Limit = 20) : IRequest<IReadOnlyList<InstrumentDto>>;
public record GetSymbolDataPreferencesQuery(string InternalSymbol) : IRequest<SymbolDataPreferencesDto?>;
public record SearchInstrumentsQuery(string Query, int Page = 1, int PageSize = 20) : IRequest<PagedResult<InstrumentDto>>;
public record GetInstrumentBySymbolQuery(string Symbol) : IRequest<InstrumentDto?>;
/// <summary>
/// Server-side paged + sorted + filtered instrument list query.
/// sortBy: "symbol" | "name" | "exchange" | "type" | "trading" — default "symbol"
/// </summary>
public record GetInstrumentsQuery(
    string? Search        = null,
    string? Exchange      = null,
    string? InstrumentType = null,
    bool?   Active        = null,
    string  SortBy        = "symbol",
    bool    SortDesc      = false,
    int     Page          = 1,
    int     PageSize      = 50
) : IRequest<PagedResult<InstrumentDto>>;
public record GetCandlesQuery(string Symbol, string Timeframe, int Limit = 100) : IRequest<IReadOnlyList<object>>;
