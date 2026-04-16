using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Infrastructure.Options;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// Exposes runtime feature-flag state to the frontend so it can adapt the UI
/// without baking environment assumptions into the client bundle.
/// </summary>
[ApiController]
[Route("api/config")]
[AllowAnonymous] // intentionally public — no secrets here, just capability flags
public class ConfigController(IOptions<FeaturesOptions> featuresOptions) : ControllerBase
{
    /// <summary>
    /// Returns the current feature flags.
    /// <c>brokerRequired: false</c> means the system is in backtest-only mode —
    /// broker login UI and Forward/Live mode options should be hidden.
    /// </summary>
    [HttpGet("features")]
    public ActionResult<ApiResponse<FeaturesDto>> GetFeatures()
    {
        return Ok(ApiResponse<FeaturesDto>.Ok(new FeaturesDto(featuresOptions.Value.BrokerRequired)));
    }
}

public record FeaturesDto(bool BrokerRequired);
