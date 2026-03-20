using MediatR;
using rvs.AlgoTrader.Application.DTOs.Instruments;
using rvs.AlgoTrader.Application.DTOs.Common;

namespace rvs.AlgoTrader.Application.Queries.Instruments;

public record GetInstrumentSearchQuery(string Query, int Limit = 20) : IRequest<IReadOnlyList<InstrumentDto>>;
public record GetSymbolDataPreferencesQuery(string InternalSymbol) : IRequest<SymbolDataPreferencesDto?>;
public record SearchInstrumentsQuery(string Query, int Page = 1, int PageSize = 20) : IRequest<PagedResult<InstrumentDto>>;
public record GetInstrumentBySymbolQuery(string Symbol) : IRequest<InstrumentDto?>;
public record GetInstrumentsQuery(string? Exchange = null, bool? Active = null) : IRequest<IReadOnlyList<InstrumentDto>>;
public record GetCandlesQuery(string Symbol, string Timeframe, int Limit = 100) : IRequest<IReadOnlyList<object>>;
