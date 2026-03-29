using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.Portfolio;
using rvs.AlgoTrader.Application.Queries.Portfolio;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PortfolioController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Aggregate portfolio metrics across all strategy instances:
    /// total P&amp;L, capital deployed, per-strategy breakdown.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<PortfolioSummaryDto>>> GetSummary(CancellationToken ct)
    {
        var result = await mediator.Send(new GetPortfolioSummaryQuery(), ct);
        return Ok(ApiResponse<PortfolioSummaryDto>.Ok(result));
    }

    /// <summary>Open positions, optionally filtered by broker or strategy run.</summary>
    [HttpGet("positions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PositionDto>>>> GetPositions(
        [FromQuery] string? brokerName,
        [FromQuery] Guid? strategyRunId,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetOpenPositionsQuery(brokerName, strategyRunId), ct);
        return Ok(ApiResponse<IReadOnlyList<PositionDto>>.Ok(result));
    }

    /// <summary>Recent orders, optionally filtered by strategy run, paginated.</summary>
    [HttpGet("orders")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OrderDto>>>> GetOrders(
        [FromQuery] Guid? strategyRunId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new GetRecentOrdersQuery(strategyRunId, page, Math.Clamp(pageSize, 1, 200)), ct);
        return Ok(ApiResponse<IReadOnlyList<OrderDto>>.Ok(result));
    }
}
