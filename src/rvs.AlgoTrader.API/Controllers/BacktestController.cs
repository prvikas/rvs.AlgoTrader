using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Queries.Backtest;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BacktestController(IMediator mediator) : ControllerBase
{

    [HttpPost("run")]
    public async Task<ActionResult<ApiResponse<BacktestResultDto>>> Run(
        [FromBody] BacktestRequestDto request, CancellationToken ct)
    {
        var result = await mediator.Send(new RunBacktestQuery(request), ct);
        return Ok(ApiResponse<BacktestResultDto>.Ok(result));
    }

    [HttpPost("walk-forward")]
    public async Task<ActionResult<ApiResponse<object>>> RunWalkForward(
        [FromBody] BacktestRequestDto request, CancellationToken ct)
    {
        var result = await mediator.Send(new RunWalkForwardQuery(request), ct);
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpGet("{id:guid}/report")]
    public async Task<IActionResult> GetReport(Guid id, CancellationToken ct)
    {
        var pdfBytes = await mediator.Send(new GetBacktestReportQuery(id), ct);
        if (pdfBytes == null) return NotFound();
        return File(pdfBytes, "application/pdf", $"backtest-{id}.pdf");
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BacktestResultDto>>>> GetAll(
        [FromQuery] string? strategyName, CancellationToken ct)
    {
        var result = await mediator.Send(new GetBacktestResultsQuery(strategyName), ct);
        return Ok(ApiResponse<IReadOnlyList<BacktestResultDto>>.Ok(result));
    }
}
