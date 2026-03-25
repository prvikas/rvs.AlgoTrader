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
}
