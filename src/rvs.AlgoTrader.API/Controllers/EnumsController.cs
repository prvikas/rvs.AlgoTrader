using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EnumsController(IEnumValuesService enumValues) : ControllerBase
{
    /// <summary>
    /// Returns all active enum values grouped by domain, sorted by sort_order.
    /// This is the single source of truth for all UI dropdown option lists.
    /// Cache-Control: public, max-age=300 (values rarely change).
    /// </summary>
    [HttpGet]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Client)]
    public async Task<ActionResult<ApiResponse<Dictionary<string, List<EnumOptionDto>>>>> GetAll(
        CancellationToken ct)
    {
        var grouped = await enumValues.GetAllAsync(ct);
        return Ok(ApiResponse<Dictionary<string, List<EnumOptionDto>>>.Ok(grouped));
    }
}
