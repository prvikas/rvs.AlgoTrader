using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.API.Authorization;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// #137: Pre-market readiness check endpoint.
///
/// Run GET /api/readiness/pre-market each morning before market open.
/// Returns a structured report of CRITICAL / WARNING / INFO checks.
/// If IsReady is false, at least one CRITICAL check failed — do NOT start live trading.
/// </summary>
[ApiController]
[Route("api/readiness")]
[Authorize]
public class ReadinessController(IPreMarketReadinessService readinessService) : ControllerBase
{
    /// <summary>
    /// Run all pre-market readiness checks.
    /// Checks: DB connectivity, Redis, market calendar, broker token validity,
    /// active strategy instances with allocated capital.
    /// </summary>
    [HttpGet("pre-market")]
    [Authorize(Policy = PolicyNames.Trader)]
    public async Task<ActionResult<ApiResponse<PreMarketReadinessReport>>> CheckPreMarket(
        CancellationToken ct)
    {
        var report = await readinessService.CheckAsync(ct);
        // Return 200 OK regardless — callers inspect IsReady / AllCriticalPass.
        // Using 503 would break monitoring dashboards that expect a body.
        return Ok(ApiResponse<PreMarketReadinessReport>.Ok(report));
    }
}
