using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.QuantIntelligence;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// P10-A: Quant Intelligence — indicator knowledge cards.
/// GET /api/indicator-intelligence          → all cards (ordered by display name)
/// GET /api/indicator-intelligence/{key}    → single card
/// PUT /api/indicator-intelligence/{key}    → update editable fields
/// </summary>
[ApiController]
[Route("api/indicator-intelligence")]
[Authorize]
public class IndicatorIntelligenceController(IIndicatorIntelligenceService svc) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<IndicatorIntelligenceDto>>>> GetAll(
        CancellationToken ct)
    {
        var result = await svc.GetAllAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<IndicatorIntelligenceDto>>.Ok(result));
    }

    [HttpGet("{key}")]
    public async Task<ActionResult<ApiResponse<IndicatorIntelligenceDto>>> GetByKey(
        string key, CancellationToken ct)
    {
        var card = await svc.GetByKeyAsync(key, ct);
        if (card is null)
            return NotFound(ApiResponse<IndicatorIntelligenceDto>.Fail($"Indicator '{key}' not found"));
        return Ok(ApiResponse<IndicatorIntelligenceDto>.Ok(card));
    }

    [HttpPut("{key}")]
    public async Task<ActionResult<ApiResponse<IndicatorIntelligenceDto>>> Update(
        string key, [FromBody] UpdateIndicatorIntelligenceRequest req, CancellationToken ct)
    {
        try
        {
            var updated = await svc.UpdateAsync(key, req, ct);
            return Ok(ApiResponse<IndicatorIntelligenceDto>.Ok(updated));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<IndicatorIntelligenceDto>.Fail(ex.Message));
        }
    }
}
