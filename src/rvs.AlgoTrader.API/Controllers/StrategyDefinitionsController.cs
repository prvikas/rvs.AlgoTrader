using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.Strategy;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
// StrategyDefinitionsController — CRUD for UI-designed strategies.
//
// A "strategy definition" is the full Strategy JSON from StrategyDefinitionPage:
// indicators, longEntry/Exit, shortEntry/Exit, stopLoss, profitTarget, etc.
// Definitions are executed by GenericRulesStrategy during backtesting.
//
// Endpoints:
//   GET    /api/strategy-definitions          → list all definitions
//   GET    /api/strategy-definitions/{id}     → get one definition
//   POST   /api/strategy-definitions          → create
//   PUT    /api/strategy-definitions/{id}     → update
//   DELETE /api/strategy-definitions/{id}     → delete
// ─────────────────────────────────────────────────────────────────────────────

[ApiController]
[Route("api/strategy-definitions")]
[Authorize]
public class StrategyDefinitionsController(IStrategyDefinitionService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StrategyDefinitionDto>>>> GetAll(
        CancellationToken ct)
    {
        var list = await service.GetAllAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<StrategyDefinitionDto>>.Ok(list));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<StrategyDefinitionDto>>> GetById(
        Guid id, CancellationToken ct)
    {
        var item = await service.GetByIdAsync(id, ct);
        if (item == null)
            return NotFound(ApiResponse<object>.Fail("Strategy definition not found"));
        return Ok(ApiResponse<StrategyDefinitionDto>.Ok(item));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<StrategyDefinitionDto>>> Create(
        [FromBody] UpsertStrategyDefinitionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponse<object>.Fail("Name is required"));

        var created = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id },
            ApiResponse<StrategyDefinitionDto>.Ok(created));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<StrategyDefinitionDto>>> Update(
        Guid id, [FromBody] UpsertStrategyDefinitionRequest request, CancellationToken ct)
    {
        var updated = await service.UpdateAsync(id, request, ct);
        if (updated == null)
            return NotFound(ApiResponse<object>.Fail("Strategy definition not found"));
        return Ok(ApiResponse<StrategyDefinitionDto>.Ok(updated));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await service.DeleteAsync(id, ct);
        if (!deleted)
            return NotFound(ApiResponse<object>.Fail("Strategy definition not found"));
        return Ok(ApiResponse<object>.Ok(null!));
    }
}
