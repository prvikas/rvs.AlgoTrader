using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.Commands.Instruments;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.Instruments;
using rvs.AlgoTrader.Application.Queries.Instruments;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class InstrumentsController(
    IMediator mediator,
    IInstrumentRefreshService refreshService) : ControllerBase
{

    /// <summary>
    /// Server-side paged + sorted + filtered instrument list.
    /// sortBy: symbol | name | exchange | type | trading (default: symbol)
    /// sortDir: asc | desc (default: asc)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<InstrumentDto>>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] string? exchange,
        [FromQuery] string? instrumentType,
        [FromQuery] bool?   active,
        [FromQuery] string  sortBy      = "symbol",
        [FromQuery] string  sortDir     = "asc",
        [FromQuery] int     page        = 1,
        [FromQuery] int     pageSize    = 50,
        [FromQuery] bool    universeOnly = false,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetInstrumentsQuery(
            search, exchange, instrumentType, active,
            sortBy, sortDir.Equals("desc", StringComparison.OrdinalIgnoreCase),
            page, pageSize, universeOnly), ct);
        return Ok(ApiResponse<PagedResult<InstrumentDto>>.Ok(result));
    }

    [HttpGet("{symbol}")]
    public async Task<ActionResult<ApiResponse<InstrumentDto>>> GetBySymbol(
        string symbol, CancellationToken ct)
    {
        var result = await mediator.Send(new GetInstrumentBySymbolQuery(symbol), ct);
        if (result == null) return NotFound(ApiResponse<object>.Fail("Instrument not found"));
        return Ok(ApiResponse<InstrumentDto>.Ok(result));
    }

    /// <summary>
    /// Legacy / automated-job endpoint: download + immediately save using saved filter config.
    /// For the interactive wizard use /preview then /commit.
    /// </summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<ApiResponse<bool>>> Refresh(
        [FromQuery] string? brokerName, CancellationToken ct)
    {
        await mediator.Send(new RefreshInstrumentsCommand(brokerName ?? "all"), ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>
    /// Wizard Step 1 — download instruments from the broker and return a preview
    /// with counts grouped by exchange, instrument type, and equity category.
    /// The preview token expires in 30 minutes; pass it to /commit to save.
    /// </summary>
    [HttpPost("preview")]
    public async Task<ActionResult<ApiResponse<RefreshPreviewDto>>> Preview(
        [FromQuery] string brokerName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(brokerName))
            return BadRequest(ApiResponse<RefreshPreviewDto>.Fail("brokerName is required"));

        var result = await refreshService.PreviewAsync(brokerName.Trim(), ct);
        return Ok(ApiResponse<RefreshPreviewDto>.Ok(result));
    }

    /// <summary>
    /// Wizard Step 2 — apply the user's filter selection to the staged data and write to the DB.
    /// </summary>
    [HttpPost("commit")]
    public async Task<ActionResult<ApiResponse<RefreshCommitResult>>> Commit(
        [FromBody] RefreshCommitRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.StagingToken))
            return BadRequest(ApiResponse<RefreshCommitResult>.Fail("StagingToken is required"));

        try
        {
            var result = await refreshService.CommitAsync(request, ct);
            return Ok(ApiResponse<RefreshCommitResult>.Ok(result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<RefreshCommitResult>.Fail(ex.Message));
        }
    }

    [HttpGet("{symbol}/candles")]
    public async Task<ActionResult<ApiResponse<object>>> GetCandles(
        string symbol, [FromQuery] string timeframe = "5m",
        [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetCandlesQuery(symbol, timeframe, limit), ct);
        return Ok(ApiResponse<object>.Ok(result));
    }
}
