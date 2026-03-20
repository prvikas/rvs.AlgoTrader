using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.Commands.Instruments;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.Instruments;
using rvs.AlgoTrader.Application.Queries.Instruments;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class InstrumentsController(IMediator mediator) : ControllerBase
{

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<InstrumentDto>>>> GetAll(
        [FromQuery] string? exchange, [FromQuery] bool? active, CancellationToken ct)
    {
        var result = await mediator.Send(new GetInstrumentsQuery(exchange, active), ct);
        return Ok(ApiResponse<IReadOnlyList<InstrumentDto>>.Ok(result));
    }

    [HttpGet("{symbol}")]
    public async Task<ActionResult<ApiResponse<InstrumentDto>>> GetBySymbol(
        string symbol, CancellationToken ct)
    {
        var result = await mediator.Send(new GetInstrumentBySymbolQuery(symbol), ct);
        if (result == null) return NotFound(ApiResponse<object>.Fail("Instrument not found"));
        return Ok(ApiResponse<InstrumentDto>.Ok(result));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<ApiResponse<bool>>> Refresh(
        [FromQuery] string? brokerName, CancellationToken ct)
    {
        await mediator.Send(new RefreshInstrumentsCommand(brokerName ?? "all"), ct);
        return Ok(ApiResponse<bool>.Ok(true));
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
