using MediatR;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.TradeJournal;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Queries.TradeJournal;

public record GetTradeJournalQuery(
    Guid?  StrategyInstanceId = null,
    string? Symbol             = null,
    string? ExitReason         = null,
    string? Source             = null,
    int     Page               = 1,
    int     PageSize           = 50) : IRequest<PagedResult<TradeJournalEntryDto>>;

public class GetTradeJournalQueryHandler(ITradeJournalRepository repo)
    : IRequestHandler<GetTradeJournalQuery, PagedResult<TradeJournalEntryDto>>
{
    public async Task<PagedResult<TradeJournalEntryDto>> Handle(
        GetTradeJournalQuery request, CancellationToken ct)
    {
        var (items, total) = await repo.GetPagedAsync(
            request.StrategyInstanceId, request.Symbol,
            request.ExitReason, request.Source,
            request.Page, Math.Clamp(request.PageSize, 1, 200), ct);

        return new PagedResult<TradeJournalEntryDto>(items, total, request.Page, request.PageSize);
    }
}

public record GetTradeJournalEntryQuery(Guid Id) : IRequest<TradeJournalEntryDto?>;

public class GetTradeJournalEntryQueryHandler(ITradeJournalRepository repo)
    : IRequestHandler<GetTradeJournalEntryQuery, TradeJournalEntryDto?>
{
    public Task<TradeJournalEntryDto?> Handle(GetTradeJournalEntryQuery request, CancellationToken ct)
        => repo.GetByIdAsync(request.Id, ct);
}

public record GetPnlAttributionQuery(
    Guid?   StrategyInstanceId = null,
    string? Symbol             = null,
    string? FromDate           = null,
    string? ToDate             = null) : IRequest<PnlAttributionDto>;

public class GetPnlAttributionQueryHandler(ITradeJournalRepository repo)
    : IRequestHandler<GetPnlAttributionQuery, PnlAttributionDto>
{
    public async Task<PnlAttributionDto> Handle(GetPnlAttributionQuery request, CancellationToken ct)
    {
        DateOnly? from = request.FromDate is not null ? DateOnly.Parse(request.FromDate) : null;
        DateOnly? to   = request.ToDate   is not null ? DateOnly.Parse(request.ToDate)   : null;
        return await repo.GetAttributionAsync(request.StrategyInstanceId, request.Symbol, from, to, ct);
    }
}
