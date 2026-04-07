using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.MarketData;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// Market-breadth data endpoints.
/// Used by the dashboard regime widget and by VCP strategy pre-flight checks.
/// </summary>
[ApiController]
[Route("api/market-breadth")]
[Authorize]
public class MarketBreadthController(IMarketBreadthService breadth, IBreadthJobDispatcher jobDispatcher) : ControllerBase
{
    /// <summary>Latest available breadth snapshot (today or most recent trading day).</summary>
    [HttpGet("latest")]
    public async Task<ActionResult<ApiResponse<MarketBreadthDto>>> GetLatest(CancellationToken ct)
    {
        var dto = await breadth.GetLatestAsync(ct);
        return dto is null
            ? NotFound(ApiResponse<MarketBreadthDto>.Fail("No breadth data available yet."))
            : Ok(ApiResponse<MarketBreadthDto>.Ok(dto));
    }

    /// <summary>Breadth for a specific date (ISO yyyy-MM-dd).</summary>
    [HttpGet("{date}")]
    public async Task<ActionResult<ApiResponse<MarketBreadthDto>>> GetByDate(
        [FromRoute] string date, CancellationToken ct)
    {
        var parsed = LocalDatePattern.Iso.Parse(date);
        if (!parsed.Success)
            return BadRequest(ApiResponse<MarketBreadthDto>.Fail("date must be ISO format: yyyy-MM-dd"));
        var localDate = parsed.Value;

        var dto = await breadth.GetByDateAsync(localDate, ct);
        return dto is null
            ? NotFound(ApiResponse<MarketBreadthDto>.Fail($"No breadth data for {date}."))
            : Ok(ApiResponse<MarketBreadthDto>.Ok(dto));
    }

    /// <summary>Last N trading-day snapshots (default 30).</summary>
    [HttpGet("history")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MarketBreadthDto>>>> GetHistory(
        [FromQuery] int days = 30, CancellationToken ct = default)
    {
        var list = await breadth.GetHistoryAsync(Math.Clamp(days, 1, 365), ct);
        return Ok(ApiResponse<IReadOnlyList<MarketBreadthDto>>.Ok(list));
    }

    /// <summary>
    /// [Admin] Trigger EOD breadth computation for a specific date.
    /// If no date is supplied, uses today.
    /// </summary>
    [HttpPost("compute")]
    public ActionResult<ApiResponse<string>> TriggerCompute([FromQuery] string? date)
    {
        if (date is not null && !LocalDatePattern.Iso.Parse(date).Success)
            return BadRequest(ApiResponse<string>.Fail("date must be ISO format: yyyy-MM-dd"));

        var jobId = jobDispatcher.Enqueue();
        return Accepted(ApiResponse<string>.Ok(jobId));
    }
}
