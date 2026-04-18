using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.Screener;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// P9 Screener — on-demand equity scan using trend-template criteria.
/// Results are computed live from the candles table (no separate cache).
/// Typical response time: 200–800 ms depending on universe size.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ScreenerController(IScreenerService screener) : ControllerBase
{
    /// <summary>
    /// Scan for trend-template breakout candidates.
    /// </summary>
    /// <param name="signal">VCP_BREAKOUT | NEAR_BREAKOUT | UPTREND (null = all)</param>
    /// <param name="minRsScore">Minimum RS score 0–100 (default 50)</param>
    /// <param name="maxResults">Max rows returned, capped at 200 (default 50)</param>
    /// <param name="ct">Cancellation token</param>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ScreenerResultDto>>>> Scan(
        [FromQuery] string? signal      = null,
        [FromQuery] decimal minRsScore  = 50m,
        [FromQuery] int     maxResults  = 50,
        CancellationToken ct            = default)
    {
        var filters = new ScreenerFilters(
            MinRsScore: minRsScore,
            Signal:     signal,
            MaxResults: maxResults);

        var results = await screener.ScanAsync(filters, ct);
        return Ok(ApiResponse<IReadOnlyList<ScreenerResultDto>>.Ok(results));
    }
}
