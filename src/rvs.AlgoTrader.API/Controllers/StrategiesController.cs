using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.Commands.Strategy;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.Strategy;
using rvs.AlgoTrader.Application.Queries.Strategy;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StrategiesController(IMediator mediator) : ControllerBase
{

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StrategyInstanceDto>>>> GetAll(CancellationToken ct)
    {
        var result = await mediator.Send(new GetAllStrategyInstancesQuery(), ct);
        return Ok(ApiResponse<IReadOnlyList<StrategyInstanceDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<StrategyInstanceDto>>> GetById(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetStrategyInstanceByIdQuery(id), ct);
        if (result == null) return NotFound(ApiResponse<object>.Fail("Strategy instance not found"));
        return Ok(ApiResponse<StrategyInstanceDto>.Ok(result));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<Guid>>> Create(
        [FromBody] CreateStrategyInstanceCommand command, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = result },
            ApiResponse<Guid>.Ok(result));
    }

    [HttpPost("{id:guid}/start")]
    public async Task<ActionResult<ApiResponse<Guid>>> Start(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new StartStrategyCommand(id), ct);
        return Ok(ApiResponse<Guid>.Ok(result));
    }

    [HttpPost("{id:guid}/pause")]
    public async Task<ActionResult<ApiResponse<bool>>> Pause(
        Guid id, [FromBody] PauseStrategyCommand command, CancellationToken ct)
    {
        await mediator.Send(command with { InstanceId = id }, ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpPost("{id:guid}/stop")]
    public async Task<ActionResult<ApiResponse<bool>>> Stop(
        Guid id, [FromBody] StopStrategyCommand command, CancellationToken ct)
    {
        await mediator.Send(command with { InstanceId = id }, ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("{id:guid}/signals")]
    public async Task<ActionResult<ApiResponse<PagedResult<SignalJournalEntryDto>>>> GetSignals(
        Guid id, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetSignalJournalQuery(id, null, null, null, 1, limit), ct);
        return Ok(ApiResponse<PagedResult<SignalJournalEntryDto>>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Update(
        Guid id, [FromBody] UpdateStrategyInstanceCommand command, CancellationToken ct)
    {
        await mediator.Send(command with { Id = id }, ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id, CancellationToken ct)
    {
        await mediator.Send(new DeleteStrategyInstanceCommand(id), ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
