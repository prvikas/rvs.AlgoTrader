using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.Commands.Orders;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.Orders;
using rvs.AlgoTrader.Application.Queries.Orders;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController(IMediator mediator) : ControllerBase
{

    [HttpPost]
    public async Task<ActionResult<ApiResponse<PlaceOrderResult>>> PlaceOrder(
        [FromBody] PlaceOrderCommand command,
        CancellationToken ct)
    {
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();
        var result = await mediator.Send(command with { CorrelationId = correlationId }, ct);
        return Ok(ApiResponse<PlaceOrderResult>.Ok(result));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<OrderDto>>>> GetOrders(
        [FromQuery] string? broker, [FromQuery] string? status, CancellationToken ct)
    {
        var query = new GetOrdersQuery(broker, status, null, null);
        var result = await mediator.Send(query, ct);
        return Ok(ApiResponse<PagedResult<OrderDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> GetOrder(
        Guid id, CancellationToken ct)
    {
        var query = new GetOrderByIdQuery(id);
        var result = await mediator.Send(query, ct);
        if (result == null) return NotFound(ApiResponse<object>.Fail("Order not found"));
        return Ok(ApiResponse<OrderDto>.Ok(result));
    }

    [HttpDelete("{brokerId}")]
    public async Task<ActionResult<ApiResponse<bool>>> CancelOrder(
        string brokerId, [FromQuery] string broker, CancellationToken ct)
    {
        var command = new CancelOrderCommand(brokerId, broker);
        var result = await mediator.Send(command, ct);
        return Ok(ApiResponse<bool>.Ok(result));
    }
}
