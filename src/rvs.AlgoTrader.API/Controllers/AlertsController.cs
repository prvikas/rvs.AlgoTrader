using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.Commands.Alerts;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Queries.Alerts;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AlertsController(IMediator mediator) : ControllerBase
{

    [HttpGet("rules")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AlertRuleDto>>>> GetRules(CancellationToken ct)
    {
        var result = await mediator.Send(new GetAlertRulesQuery(), ct);
        return Ok(ApiResponse<IReadOnlyList<AlertRuleDto>>.Ok(result));
    }

    [HttpPost("rules")]
    public async Task<ActionResult<ApiResponse<Guid>>> CreateRule(
        [FromBody] CreateAlertRuleCommand command, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return Ok(ApiResponse<Guid>.Ok(result));
    }

    [HttpDelete("rules/{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteRule(Guid id, CancellationToken ct)
    {
        await mediator.Send(new DeleteAlertRuleCommand(id), ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("history")]
    public async Task<ActionResult<ApiResponse<object>>> GetHistory(
        [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetAlertHistoryQuery(limit), ct);
        return Ok(ApiResponse<object>.Ok(result));
    }
}
