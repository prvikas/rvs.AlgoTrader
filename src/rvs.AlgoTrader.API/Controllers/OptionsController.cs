using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.Options;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// Options Intelligence — live chain data, derived analytics, and rule-engine signals.
/// Data sourced from IOptionChainService (live broker) with fallback to snapshot DB.
/// </summary>
[Authorize]
[ApiController]
[Route("api/options")]
public class OptionsController(IOptionsIntelligenceService intel) : ControllerBase
{
    /// <summary>Full intelligence: chain + rule signals + bias score.</summary>
    [HttpGet("intelligence/{symbol}")]
    public async Task<ActionResult<ApiResponse<OptionsIntelligenceDto>>> GetIntelligence(
        string symbol,
        [FromQuery] string expiry = "nearest",
        CancellationToken ct = default)
    {
        var result = await intel.GetIntelligenceAsync(symbol, expiry, ct);
        if (result == null)
            return NotFound(ApiResponse<OptionsIntelligenceDto>.Fail(
                $"No option chain data available for {symbol}"));
        return Ok(ApiResponse<OptionsIntelligenceDto>.Ok(result));
    }

    /// <summary>Raw chain data only — lighter call for chart-only views.</summary>
    [HttpGet("chain/{symbol}")]
    public async Task<ActionResult<ApiResponse<OptionChainDto>>> GetChain(
        string symbol,
        [FromQuery] string expiry = "nearest",
        CancellationToken ct = default)
    {
        var result = await intel.GetChainAsync(symbol, expiry, ct);
        if (result == null)
            return NotFound(ApiResponse<OptionChainDto>.Fail(
                $"No option chain data available for {symbol}"));
        return Ok(ApiResponse<OptionChainDto>.Ok(result));
    }

    /// <summary>Available expiry dates for a symbol (nearest weekly + nearest monthly).</summary>
    [HttpGet("expiries/{symbol}")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<string>>>> GetExpiries(
        string symbol,
        CancellationToken ct = default)
    {
        var expiries = await intel.GetExpiriesAsync(symbol, ct);
        return Ok(ApiResponse<IReadOnlyList<string>>.Ok(expiries));
    }
}
