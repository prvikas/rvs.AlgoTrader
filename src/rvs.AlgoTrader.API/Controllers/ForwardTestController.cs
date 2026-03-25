using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.Commands.ForwardTest;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.ForwardTest;
using rvs.AlgoTrader.Application.Queries.ForwardTest;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ForwardTestController(IMediator mediator) : ControllerBase
{
    /// <summary>List all forward test sessions, enriched with strategy info and equity curves.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ForwardTestSessionDetailDto>>>> GetAll(CancellationToken ct)
    {
        var result = await mediator.Send(new GetForwardTestSessionsQuery(), ct);
        return Ok(ApiResponse<IReadOnlyList<ForwardTestSessionDetailDto>>.Ok(result));
    }

    /// <summary>Get a single forward test session by its ID.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ForwardTestSessionDetailDto>>> GetById(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetForwardTestSessionByIdQuery(id), ct);
        if (result is null) return NotFound(ApiResponse<ForwardTestSessionDetailDto>.Fail("Session not found."));
        return Ok(ApiResponse<ForwardTestSessionDetailDto>.Ok(result));
    }

    /// <summary>
    /// Promote a backtest result to a new Forward Test mode strategy instance.
    /// Carries strategy type, symbol, timeframe from the source backtest.
    /// Returns the new strategy instance ID.
    /// </summary>
    [HttpPost("from-backtest")]
    public async Task<ActionResult<ApiResponse<string>>> PromoteFromBacktest(
        [FromBody] PromoteBacktestToForwardTestRequest request, CancellationToken ct)
    {
        var cmd = new PromoteBacktestToForwardTestCommand(
            request.BacktestId,
            request.InstanceName,
            request.BrokerName,
            request.InitialCapital,
            request.ScheduleJson);

        var newId = await mediator.Send(cmd, ct);
        return Ok(ApiResponse<string>.Ok(newId));
    }

    /// <summary>
    /// Promote a stopped/completed forward test to a Live strategy instance.
    /// Runs 7 pre-flight checks; returns check results and the new instance ID on success.
    /// </summary>
    [HttpPost("{instanceId}/promote-to-live")]
    public async Task<ActionResult<ApiResponse<PromoteToLiveResultDto>>> PromoteToLive(
        string instanceId,
        [FromBody] PromoteForwardTestToLiveRequest request,
        CancellationToken ct)
    {
        var cmd = new PromoteForwardTestToLiveCommand(
            instanceId,
            request.BrokerName,
            request.AllocatedCapital,
            request.ScheduleJson);

        var result = await mediator.Send(cmd, ct);

        if (!result.Success)
            return UnprocessableEntity(ApiResponse<PromoteToLiveResultDto>.Ok(result));

        return Ok(ApiResponse<PromoteToLiveResultDto>.Ok(result));
    }
}
