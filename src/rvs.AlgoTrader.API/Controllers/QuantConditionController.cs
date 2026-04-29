using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.QuantIntelligence;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// P10-C Quant Lab: CRUD + lifecycle for user-defined research conditions.
///
/// GET    /api/quant-conditions               → all user conditions (not templates)
/// GET    /api/quant-conditions/templates     → prebuilt template library
/// GET    /api/quant-conditions/{id}          → single condition
/// POST   /api/quant-conditions               → create new condition
/// PUT    /api/quant-conditions/{id}          → update fields
/// DELETE /api/quant-conditions/{id}          → delete (non-templates only)
/// POST   /api/quant-conditions/{id}/notes    → append a dated note
/// PUT    /api/quant-conditions/{id}/status   → transition lifecycle status
/// POST   /api/quant-conditions/{id}/clone    → clone into new Hypothesis condition
/// </summary>
[ApiController]
[Route("api/quant-conditions")]
[Authorize]
public class QuantConditionController(IQuantConditionService svc) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<QuantConditionDto>>>> GetAll(CancellationToken ct)
    {
        var result = await svc.GetAllAsync(templatesOnly: false, ct);
        return Ok(ApiResponse<IReadOnlyList<QuantConditionDto>>.Ok(result));
    }

    [HttpGet("templates")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<QuantConditionDto>>>> GetTemplates(CancellationToken ct)
    {
        var result = await svc.GetAllAsync(templatesOnly: true, ct);
        return Ok(ApiResponse<IReadOnlyList<QuantConditionDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<QuantConditionDto>>> GetById(Guid id, CancellationToken ct)
    {
        var result = await svc.GetByIdAsync(id, ct);
        if (result is null) return NotFound(ApiResponse<QuantConditionDto>.Fail($"Condition '{id}' not found"));
        return Ok(ApiResponse<QuantConditionDto>.Ok(result));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<QuantConditionDto>>> Create(
        [FromBody] CreateQuantConditionRequest req, CancellationToken ct)
    {
        var result = await svc.CreateAsync(req, ct);
        return Ok(ApiResponse<QuantConditionDto>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<QuantConditionDto>>> Update(
        Guid id, [FromBody] UpdateQuantConditionRequest req, CancellationToken ct)
    {
        try
        {
            var result = await svc.UpdateAsync(id, req, ct);
            return Ok(ApiResponse<QuantConditionDto>.Ok(result));
        }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<QuantConditionDto>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<QuantConditionDto>.Fail(ex.Message)); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await svc.DeleteAsync(id, ct);
            return Ok(ApiResponse<bool>.Ok(true));
        }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<bool>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<bool>.Fail(ex.Message)); }
    }

    [HttpPost("{id:guid}/notes")]
    public async Task<ActionResult<ApiResponse<QuantConditionDto>>> AddNote(
        Guid id, [FromBody] AddQuantConditionNoteRequest req, CancellationToken ct)
    {
        try
        {
            var result = await svc.AddNoteAsync(id, req, ct);
            return Ok(ApiResponse<QuantConditionDto>.Ok(result));
        }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<QuantConditionDto>.Fail(ex.Message)); }
    }

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<ApiResponse<QuantConditionDto>>> ChangeStatus(
        Guid id, [FromBody] ChangeQuantConditionStatusRequest req, CancellationToken ct)
    {
        try
        {
            var result = await svc.ChangeStatusAsync(id, req, ct);
            return Ok(ApiResponse<QuantConditionDto>.Ok(result));
        }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<QuantConditionDto>.Fail(ex.Message)); }
        catch (Exception ex) { return BadRequest(ApiResponse<QuantConditionDto>.Fail(ex.Message)); }
    }

    [HttpPost("{id:guid}/clone")]
    public async Task<ActionResult<ApiResponse<QuantConditionDto>>> Clone(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await svc.CloneAsync(id, ct);
            return Ok(ApiResponse<QuantConditionDto>.Ok(result));
        }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<QuantConditionDto>.Fail(ex.Message)); }
    }
}
