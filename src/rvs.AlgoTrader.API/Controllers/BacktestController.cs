using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Queries.Backtest;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BacktestController(IMediator mediator, IHistoricalDownloadService downloadService) : ControllerBase
{
    [HttpPost("download-history")]
    public async Task<ActionResult<ApiResponse<object>>> DownloadHistory(
        [FromBody] DownloadHistoryRequest request, CancellationToken ct)
    {
        var result = await downloadService.DownloadAsync(
            request.InternalSymbol,
            request.BrokerName ?? "MStock",
            request.Timeframe,
            new DateOnly(request.FromDate.Year, request.FromDate.Month, request.FromDate.Day),
            new DateOnly(request.ToDate.Year, request.ToDate.Month, request.ToDate.Day),
            ct);

        return result.Success
            ? Ok(ApiResponse<object>.Ok(new { barCount = result.BarCount, dataHash = result.DataHash }))
            : BadRequest(ApiResponse.Fail<object>(result.Error ?? "Download failed"));
    }

    [HttpPost("run")]
    public async Task<ActionResult<ApiResponse<BacktestResultDto>>> Run(
        [FromBody] BacktestRequestDto request, CancellationToken ct)
    {
        var result = await mediator.Send(new RunBacktestQuery(request), ct);
        return result.Success
            ? Ok(ApiResponse<BacktestResultDto>.Ok(result))
            : BadRequest(ApiResponse<BacktestResultDto>.Fail(result.Error ?? "Backtest failed"));
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

public record DownloadHistoryRequest(
    string InternalSymbol,
    string Timeframe,
    LocalDate FromDate,
    LocalDate ToDate,
    string? BrokerName = null);
