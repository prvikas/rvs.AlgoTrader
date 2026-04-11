using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.DTOs.Common;
// DownloadHistoryRequest is defined in Application/DTOs/Backtest/DownloadHistoryRequest.cs
using rvs.AlgoTrader.Application.Queries.Backtest;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Infrastructure.Constants;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BacktestController(
    IMediator mediator,
    IHistoricalDownloadService downloadService,
    IBacktestJobManager jobManager,
    IMonteCarloSimulator monteCarlo) : ControllerBase
{
    [HttpPost("download-history")]
    public async Task<ActionResult<ApiResponse<object>>> DownloadHistory(
        [FromBody] DownloadHistoryRequest request, CancellationToken ct)
    {
        var result = await downloadService.DownloadAsync(
            request.InternalSymbol,
            request.BrokerName ?? BrokerNames.Default,
            request.Timeframe,
            new DateOnly(request.FromDate.Year, request.FromDate.Month, request.FromDate.Day),
            new DateOnly(request.ToDate.Year, request.ToDate.Month, request.ToDate.Day),
            ct);

        return result.Success
            ? Ok(ApiResponse<object>.Ok(new { barCount = result.BarCount, dataHash = result.DataHash }))
            : BadRequest(ApiResponse.Fail<object>(result.Error ?? "Download failed"));
    }

    /// <summary>
    /// [DEPRECATED] Synchronous backtest run — blocks until complete (10 s timeout).
    /// Use POST /backtest/start instead for async execution.
    /// </summary>
    [HttpPost("run")]
    [Obsolete("Use POST /backtest/start for async execution.")]
    public async Task<ActionResult<ApiResponse<BacktestResultDto>>> Run(
        [FromBody] BacktestRequestDto request, CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            var result = await mediator.Send(new RunBacktestQuery(request), linked.Token);
            return result.Success
                ? Ok(ApiResponse<BacktestResultDto>.Ok(result))
                : BadRequest(ApiResponse<BacktestResultDto>.Fail(result.Error ?? "Backtest failed"));
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return StatusCode(408, ApiResponse.Fail<BacktestResultDto>("Backtest timed out. Use POST /backtest/start for async execution."));
        }
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

    /// <summary>
    /// Returns the full per-trade breakdown for a saved backtest run.
    /// Includes entry/exit prices, quantity, brokerage per leg, slippage, R-multiple,
    /// holding bars, MAE/MFE, and per-leg JSON for spread trades.
    /// </summary>
    [HttpGet("{id:guid}/trades")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BacktestTradeDto>>>> GetTrades(
        Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetBacktestResultQuery(id), ct);
        if (result == null)
            return NotFound(ApiResponse.Fail<IReadOnlyList<BacktestTradeDto>>($"Backtest run '{id}' not found"));

        var trades = result.Trades ?? [];
        return Ok(ApiResponse<IReadOnlyList<BacktestTradeDto>>.Ok(trades));
    }

    /// <summary>
    /// Runs a Monte Carlo simulation on a completed backtest's trade sequence.
    /// Shuffles trade order N times to compute confidence intervals for drawdown and final equity.
    /// </summary>
    [HttpPost("{id:guid}/montecarlo")]
    public async Task<ActionResult<ApiResponse<MonteCarloSimulationDto>>> RunMonteCarlo(
        Guid id, [FromBody] MonteCarloRequestDto? request, CancellationToken ct)
    {
        var result = await mediator.Send(new GetBacktestResultQuery(id), ct);
        if (result == null)
            return NotFound(ApiResponse.Fail<MonteCarloSimulationDto>($"Backtest run '{id}' not found"));
        if (result.Trades == null || result.Trades.Count == 0)
            return BadRequest(ApiResponse.Fail<MonteCarloSimulationDto>("No trades available for simulation"));

        var netPnls = result.Trades.Select(t => t.NetPnl).ToList();
        var simResult = monteCarlo.Run(netPnls, result.InitialCapital,
            request?.Simulations ?? 1000, request?.Seed);
        return Ok(ApiResponse<MonteCarloSimulationDto>.Ok(new MonteCarloSimulationDto(
            simResult.DrawdownP5, simResult.DrawdownP50, simResult.DrawdownP95,
            simResult.EquityP5, simResult.EquityP50, simResult.EquityP95,
            simResult.ProbabilityOfRuin)));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<BacktestResultDto>>>> GetAll(
        [FromQuery] string? strategyName,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetBacktestRunsQuery(null, page, Math.Clamp(pageSize, 1, 200), strategyName), ct);
        return Ok(ApiResponse<PagedResult<BacktestResultDto>>.Ok(result));
    }
}

// DownloadHistoryRequest moved to Application/DTOs/Backtest/DownloadHistoryRequest.cs
