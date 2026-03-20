using MediatR;
using rvs.AlgoTrader.Application.DTOs.Instruments;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Queries.Instruments;

internal static class InstrumentMapper
{
    public static InstrumentDto ToDto(Domain.Entities.Instrument i) => new(
        i.Id, i.InternalSymbol, i.BrokerToken, i.BrokerName,
        i.TradingSymbol, i.Exchange, i.InstrumentType.ToString(),
        i.Underlying, i.StrikePrice, i.OptionType?.ToString(),
        i.Expiry.HasValue ? new DateOnly(i.Expiry.Value.Year, i.Expiry.Value.Month, i.Expiry.Value.Day) : null,
        i.LotSize, i.TickSize, i.IsActive);
}

public class SearchInstrumentsHandler(IInstrumentRepository repo) : IRequestHandler<SearchInstrumentsQuery, PagedResult<InstrumentDto>>
{
    public async Task<PagedResult<InstrumentDto>> Handle(SearchInstrumentsQuery request, CancellationToken ct)
    {
        var instruments = await repo.SearchAsync(request.Query, request.PageSize * request.Page, ct);
        var total = instruments.Count;
        var items = instruments.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(InstrumentMapper.ToDto)
            .ToList();
        return new PagedResult<InstrumentDto>(items, total, request.Page, request.PageSize);
    }
}

public class GetInstrumentBySymbolHandler(IInstrumentRepository repo) : IRequestHandler<GetInstrumentBySymbolQuery, InstrumentDto?>
{
    public async Task<InstrumentDto?> Handle(GetInstrumentBySymbolQuery request, CancellationToken ct)
    {
        var i = await repo.GetBySymbolAsync(request.Symbol, ct);
        return i is null ? null : InstrumentMapper.ToDto(i);
    }
}

public class GetInstrumentsHandler(IInstrumentRepository repo) : IRequestHandler<GetInstrumentsQuery, IReadOnlyList<InstrumentDto>>
{
    public async Task<IReadOnlyList<InstrumentDto>> Handle(GetInstrumentsQuery request, CancellationToken ct)
    {
        var instruments = await repo.SearchAsync(string.Empty, 10_000, ct);
        return instruments
            .Where(i => request.Active == null || i.IsActive == request.Active)
            .Select(InstrumentMapper.ToDto)
            .ToList();
    }
}

public class GetCandlesHandler(ICandleCache cache) : IRequestHandler<GetCandlesQuery, IReadOnlyList<object>>
{
    public async Task<IReadOnlyList<object>> Handle(GetCandlesQuery request, CancellationToken ct)
    {
        var candles = await cache.GetAsync(request.Symbol, request.Timeframe, request.Limit, ct);
        return candles.Cast<object>().ToList();
    }
}
