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
public class BacktestController(
    IMediator mediator,
    IHistoricalDownloadService downloadService,
    IBacktestJobManager jobManager) : ControllerBase
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

    /// <summary>
    /// Synchronous backtest run (backward compat). Blocks until complete.
    /// For long-running backtests, prefer POST /backtest/start.
    /// </summary>
    [HttpPost("run")]
    public async Task<ActionResult<ApiResponse<BacktestResultDto>>> Run(
        [FromBody] BacktestRequestDto request, CancellationToken ct)
    {
        var result = await mediator.Send(new RunBacktestQuery(request), ct);
        return result.Success
            ? Ok(ApiResponse<BacktestResultDto>.Ok(result))
            : BadRequest(ApiResponse<BacktestResultDto>.Fail(result.Error ?? "Backtest failed"));
    }

    /// <summary>
    /// Async backtest — returns a jobId immediately (202 Accepted).
    /// Subscribe to SignalR /hubs/backtest and call SubscribeToJob(jobId) for real-time updates.
    /// Poll GET /backtest/{jobId}/status for progress without SignalR.
    /// Supports running multiple strategies simultaneously.
    /// </summary>
    [HttpPost("start")]
    public async Task<ActionResult<ApiResponse<object>>> Start(
        [FromBody] BacktestRequestDto request, CancellationToken ct)
    {
        var jobId = await jobManager.EnqueueAsync(request, ct);
        return Accepted(ApiResponse<object>.Ok(new { jobId }));
    }

    /// <summary>Get status/progress of an async backtest job.</summary>
    [HttpGet("{jobId}/status")]
    public ActionResult<ApiResponse<BacktestJobStatusDto>> GetStatus(string jobId)
    {
        var status = jobManager.GetStatus(jobId);
        return status == null
            ? NotFound(ApiResponse.Fail<BacktestJobStatusDto>($"Job '{jobId}' not found"))
            : Ok(ApiResponse<BacktestJobStatusDto>.Ok(status));
    }

    /// <summary>Cancel a running backtest job.</summary>
    [HttpPost("{jobId}/cancel")]
    public ActionResult<ApiResponse<object>> Cancel(string jobId)
    {
        jobManager.CancelJob(jobId);
        return Ok(ApiResponse<object>.Ok(new { jobId, status = "Cancelling" }));
    }

    /// <summary>List all active backtest job IDs.</summary>
    [HttpGet("active")]
    public ActionResult<ApiResponse<IReadOnlyList<string>>> GetActive()
        => Ok(ApiResponse<IReadOnlyList<string>>.Ok(jobManager.GetActiveJobIds()));

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

    /// <summary>Get a single backtest result by ID (includes ChartSample if stored).</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<BacktestResultDto>>> GetById(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetBacktestResultQuery(id), ct);
        return result == null
            ? NotFound(ApiResponse.Fail<BacktestResultDto>($"Backtest run '{id}' not found"))
            : Ok(ApiResponse<BacktestResultDto>.Ok(result));
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
