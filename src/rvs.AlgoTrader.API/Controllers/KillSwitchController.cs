using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.Commands.Strategy;
using rvs.AlgoTrader.Application.DTOs.Common;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/kill-switch")]
[Authorize]
public class KillSwitchController(IMediator mediator) : ControllerBase
{

    [HttpPost("activate")]
    public async Task<ActionResult<ApiResponse<bool>>> Activate(
        [FromBody] ActivateKillSwitchCommand command, CancellationToken ct)
    {
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();
        await mediator.Send(command with { CorrelationId = correlationId }, ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpPost("deactivate")]
    public async Task<ActionResult<ApiResponse<bool>>> Deactivate(
        [FromBody] DeactivateKillSwitchCommand command, CancellationToken ct)
    {
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();
        await mediator.Send(command with { CorrelationId = correlationId }, ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("status")]
    public async Task<ActionResult<ApiResponse<bool>>> GetStatus(CancellationToken ct)
    {
        var result = await mediator.Send(new GetKillSwitchStatusQuery(), ct);
        return Ok(ApiResponse<bool>.Ok(result));
    }
}
