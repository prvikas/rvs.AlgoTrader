using MediatR;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.Instruments;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Application.Queries.Instruments;

// ── Queries ───────────────────────────────────────────────────────────────────

/// <summary>Returns all instrument_universe rows, optionally filtered by category.</summary>
public record GetUniverseQuery(
    string? Category = null,
    bool?   Active   = null,
    int     Page     = 1,
    int     PageSize = 200
) : IRequest<PagedResult<InstrumentUniverseDto>>;

// ── Helpers ───────────────────────────────────────────────────────────────────

internal static class UniverseMapper
{
    public static InstrumentUniverseDto ToDto(InstrumentUniverse u) =>
        new(u.Id, u.Symbol, u.Exchange, u.Category, u.IsActive, u.CreatedAt.ToDateTimeOffset());
}

// ── Handlers ──────────────────────────────────────────────────────────────────

public class GetUniverseHandler(IInstrumentUniverseRepository repo)
    : IRequestHandler<GetUniverseQuery, PagedResult<InstrumentUniverseDto>>
{
    public async Task<PagedResult<InstrumentUniverseDto>> Handle(
        GetUniverseQuery request, CancellationToken ct)
    {
        var (items, total) = await repo.GetPagedAsync(
            request.Category, request.Active, request.Page, request.PageSize, ct);

        return new PagedResult<InstrumentUniverseDto>(
            items.Select(UniverseMapper.ToDto).ToList(),
            total, request.Page, request.PageSize);
    }
}
