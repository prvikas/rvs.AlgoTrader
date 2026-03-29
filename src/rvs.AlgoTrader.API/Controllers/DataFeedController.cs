using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// Real-time data feed health and staleness status.
/// Used by the dashboard to display broker connection state.
/// </summary>
[ApiController]
[Route("api/data-feed")]
[Authorize]
public class DataFeedController(IDataFeedHealthMonitor healthMonitor) : ControllerBase
{
    /// <summary>Health status for all monitored broker feeds.</summary>
    [HttpGet("health")]
    public ActionResult<ApiResponse<IReadOnlyList<FeedHealthStatus>>> GetHealth()
        => Ok(ApiResponse<IReadOnlyList<FeedHealthStatus>>.Ok(healthMonitor.GetAllStatuses()));

    /// <summary>Health status for a specific broker.</summary>
    [HttpGet("health/{brokerName}")]
    public ActionResult<ApiResponse<FeedHealthStatus>> GetBrokerHealth([FromRoute] string brokerName)
    {
        var status = healthMonitor.GetStatus(brokerName);
        return status is null
            ? NotFound(ApiResponse<FeedHealthStatus>.Fail($"No feed data for broker '{brokerName}'."))
            : Ok(ApiResponse<FeedHealthStatus>.Ok(status));
    }

    /// <summary>Returns true if the named broker feed is stale (no tick within the threshold).</summary>
    [HttpGet("stale/{brokerName}")]
    public ActionResult<ApiResponse<bool>> IsStale(
        [FromRoute] string brokerName,
        [FromQuery] int maxStalenessSeconds = 60)
        => Ok(ApiResponse<bool>.Ok(healthMonitor.IsStale(brokerName, maxStalenessSeconds)));
}
