using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.Commands.Capital;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.Strategy;
using rvs.AlgoTrader.Application.Queries.Capital;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CapitalController(IMediator mediator) : ControllerBase
{

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CapitalAllocationDto>>>> GetAllocations(
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetCapitalAllocationsQuery(), ct);
        return Ok(ApiResponse<IReadOnlyList<CapitalAllocationDto>>.Ok(result));
    }

    [HttpPost("allocate")]
    public async Task<ActionResult<ApiResponse<bool>>> Allocate(
        [FromBody] AllocateCapitalCommand command, CancellationToken ct)
    {
        await mediator.Send(command, ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpPost("deallocate")]
    public async Task<ActionResult<ApiResponse<bool>>> Deallocate(
        [FromBody] DeallocateCapitalCommand command, CancellationToken ct)
    {
        await mediator.Send(command, ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
