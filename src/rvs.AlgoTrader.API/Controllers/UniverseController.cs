using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.Commands.Instruments;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.Instruments;
using rvs.AlgoTrader.Application.Queries.Instruments;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// CRUD for the instrument_universe table.
/// Controls which symbols are persisted during broker instrument refresh.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UniverseController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// List universe entries with optional filtering.
    /// category: NSE_EQUITY | OPTIONS_UNDERLYING
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<InstrumentUniverseDto>>>> GetAll(
        [FromQuery] string? category,
        [FromQuery] bool?   active,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 200,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetUniverseQuery(category, active, page, pageSize), ct);
        return Ok(ApiResponse<PagedResult<InstrumentUniverseDto>>.Ok(result));
    }

    /// <summary>Add a new symbol to the universe.</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<InstrumentUniverseDto>>> Create(
        [FromBody] CreateInstrumentUniverseRequest req,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new CreateUniverseEntryCommand(req.Symbol, req.Exchange, req.Category), ct);
        return Ok(ApiResponse<InstrumentUniverseDto>.Ok(result));
    }

    /// <summary>Update symbol, exchange, category, or active flag.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<InstrumentUniverseDto>>> Update(
        Guid id,
        [FromBody] UpdateInstrumentUniverseRequest req,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new UpdateUniverseEntryCommand(id, req.Symbol, req.Exchange, req.Category, req.IsActive), ct);
        if (result == null)
            return NotFound(ApiResponse<InstrumentUniverseDto>.Fail("Universe entry not found"));
        return Ok(ApiResponse<InstrumentUniverseDto>.Ok(result));
    }

    /// <summary>Delete a universe entry permanently.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(
        Guid id, CancellationToken ct)
    {
        var ok = await mediator.Send(new DeleteUniverseEntryCommand(id), ct);
        if (!ok) return NotFound(ApiResponse<bool>.Fail("Universe entry not found"));
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>
    /// Seeds the universe with Nifty 50 equities and major option underlyings.
    /// Idempotent — skips entries that already exist.
    /// Returns the number of new rows added.
    /// </summary>
    [HttpPost("seed-defaults")]
    public async Task<ActionResult<ApiResponse<int>>> SeedDefaults(CancellationToken ct)
    {
        var added = await mediator.Send(new SeedDefaultUniverseCommand(), ct);
        return Ok(ApiResponse<int>.Ok(added));
    }
}
