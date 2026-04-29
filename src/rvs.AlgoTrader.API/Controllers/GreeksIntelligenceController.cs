using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.QuantIntelligence;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// P10-A: Quant Intelligence — options metrics knowledge cards (Greeks, IV, VIX).
/// GET /api/greeks-intelligence          → all cards (ordered by display name)
/// GET /api/greeks-intelligence/{key}    → single card
/// PUT /api/greeks-intelligence/{key}    → update editable fields
/// </summary>
[ApiController]
[Route("api/greeks-intelligence")]
[Authorize]
public class GreeksIntelligenceController(IGreeksIntelligenceService svc) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<GreeksIntelligenceDto>>>> GetAll(
        CancellationToken ct)
    {
        var result = await svc.GetAllAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<GreeksIntelligenceDto>>.Ok(result));
    }

    [HttpGet("{key}")]
    public async Task<ActionResult<ApiResponse<GreeksIntelligenceDto>>> GetByKey(
        string key, CancellationToken ct)
    {
        var card = await svc.GetByKeyAsync(key, ct);
        if (card is null)
            return NotFound(ApiResponse<GreeksIntelligenceDto>.Fail($"Metric '{key}' not found"));
        return Ok(ApiResponse<GreeksIntelligenceDto>.Ok(card));
    }

    [HttpPut("{key}")]
    public async Task<ActionResult<ApiResponse<GreeksIntelligenceDto>>> Update(
        string key, [FromBody] UpdateGreeksIntelligenceRequest req, CancellationToken ct)
    {
        try
        {
            var updated = await svc.UpdateAsync(key, req, ct);
            return Ok(ApiResponse<GreeksIntelligenceDto>.Ok(updated));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<GreeksIntelligenceDto>.Fail(ex.Message));
        }
    }
}
