using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EnumsController(AlgoTraderDbContext db) : ControllerBase
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
        var rows = await db.EnumValues
            .Where(e => e.IsActive)
            .OrderBy(e => e.Domain)
            .ThenBy(e => e.SortOrder)
            .ToListAsync(ct);

        var grouped = rows
            .GroupBy(e => e.Domain)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => new EnumOptionDto(e.Value, e.Label)).ToList());

        return Ok(ApiResponse<Dictionary<string, List<EnumOptionDto>>>.Ok(grouped));
    }
}

public record EnumOptionDto(string Value, string Label);
