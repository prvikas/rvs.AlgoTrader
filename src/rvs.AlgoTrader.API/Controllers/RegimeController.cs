using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.QuantIntelligence;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// P10-B: Market Regime Engine.
/// GET /api/regime/classify?symbol=NIFTY50&amp;timeframe=1d
///   → QuantRegimeDto with regime label, traffic light, confidence, and contributing factors.
/// </summary>
[ApiController]
[Route("api/regime")]
[Authorize]
public class RegimeController(IQuantRegimeService svc) : ControllerBase
{
    [HttpGet("classify")]
    public async Task<ActionResult<ApiResponse<QuantRegimeDto>>> Classify(
        [FromQuery] string symbol    = "NIFTY50",
        [FromQuery] string timeframe = "1d",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return BadRequest(ApiResponse<QuantRegimeDto>.Fail("symbol is required"));

        var result = await svc.ClassifyAsync(symbol.Trim().ToUpperInvariant(), timeframe.Trim(), ct);
        return Ok(ApiResponse<QuantRegimeDto>.Ok(result));
    }
}
