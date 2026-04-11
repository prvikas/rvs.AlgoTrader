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
//   GET    /api/strategy-definitions                          → list all definitions
//   GET    /api/strategy-definitions/{id}                    → get one definition
//   POST   /api/strategy-definitions                         → create
//   PUT    /api/strategy-definitions/{id}                    → update
//   DELETE /api/strategy-definitions/{id}                    → delete
//
//   GET    /api/strategy-definitions/{id}/scenarios          → list scenarios
//   GET    /api/strategy-definitions/{id}/scenarios/{scenId} → get one scenario
//   POST   /api/strategy-definitions/{id}/scenarios          → create scenario
//   PUT    /api/strategy-definitions/{id}/scenarios/{scenId} → update scenario
//   PATCH  /api/strategy-definitions/{id}/scenarios/{scenId} → patch status/metrics
//   DELETE /api/strategy-definitions/{id}/scenarios/{scenId} → delete scenario
// ─────────────────────────────────────────────────────────────────────────────

[ApiController]
[Route("api/strategy-definitions")]
[Authorize]
public class StrategyDefinitionsController(
    IStrategyDefinitionService service,
    IStrategyDefinitionScenarioService scenarios) : ControllerBase
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

    // ── Scenario sub-resource ─────────────────────────────────────────────────

    [HttpGet("{id:guid}/scenarios")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StrategyDefinitionScenarioDto>>>> ListScenarios(
        Guid id, CancellationToken ct)
    {
        var list = await scenarios.ListAsync(id, ct);
        return Ok(ApiResponse<IReadOnlyList<StrategyDefinitionScenarioDto>>.Ok(list));
    }

    [HttpGet("{id:guid}/scenarios/{scenId:guid}")]
    public async Task<ActionResult<ApiResponse<StrategyDefinitionScenarioDto>>> GetScenario(
        Guid id, Guid scenId, CancellationToken ct)
    {
        var item = await scenarios.GetByIdAsync(scenId, ct);
        if (item == null || item.StrategyDefinitionId != id)
            return NotFound(ApiResponse<object>.Fail("Scenario not found"));
        return Ok(ApiResponse<StrategyDefinitionScenarioDto>.Ok(item));
    }

    [HttpPost("{id:guid}/scenarios")]
    public async Task<ActionResult<ApiResponse<StrategyDefinitionScenarioDto>>> CreateScenario(
        Guid id, [FromBody] UpsertDefinitionScenarioRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponse<object>.Fail("Name is required"));
        var created = await scenarios.CreateAsync(id, request, ct);
        return CreatedAtAction(nameof(GetScenario), new { id, scenId = created.Id },
            ApiResponse<StrategyDefinitionScenarioDto>.Ok(created));
    }

    [HttpPut("{id:guid}/scenarios/{scenId:guid}")]
    public async Task<ActionResult<ApiResponse<StrategyDefinitionScenarioDto>>> UpdateScenario(
        Guid id, Guid scenId, [FromBody] UpsertDefinitionScenarioRequest request, CancellationToken ct)
    {
        var updated = await scenarios.UpdateAsync(scenId, request, ct);
        if (updated == null || updated.StrategyDefinitionId != id)
            return NotFound(ApiResponse<object>.Fail("Scenario not found"));
        return Ok(ApiResponse<StrategyDefinitionScenarioDto>.Ok(updated));
    }

    [HttpPatch("{id:guid}/scenarios/{scenId:guid}")]
    public async Task<ActionResult<ApiResponse<StrategyDefinitionScenarioDto>>> PatchScenario(
        Guid id, Guid scenId, [FromBody] PatchDefinitionScenarioRequest request, CancellationToken ct)
    {
        var patched = await scenarios.PatchAsync(scenId, request, ct);
        if (patched == null || patched.StrategyDefinitionId != id)
            return NotFound(ApiResponse<object>.Fail("Scenario not found"));
        return Ok(ApiResponse<StrategyDefinitionScenarioDto>.Ok(patched));
    }

    [HttpDelete("{id:guid}/scenarios/{scenId:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteScenario(
        Guid id, Guid scenId, CancellationToken ct)
    {
        var item = await scenarios.GetByIdAsync(scenId, ct);
        if (item == null || item.StrategyDefinitionId != id)
            return NotFound(ApiResponse<object>.Fail("Scenario not found"));
        await scenarios.DeleteAsync(scenId, ct);
        return Ok(ApiResponse<object>.Ok(null!));
    }
}
