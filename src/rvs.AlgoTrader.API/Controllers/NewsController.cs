using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.News;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// P9 News — market news items for symbols and the broader market.
/// External NSE feed auto-ingestion is VERIFY_LIVE; items can be created manually
/// via POST /api/news until a live feed is confirmed.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NewsController(INewsService news) : ControllerBase
{
    /// <summary>
    /// Get recent news. Pass <c>symbol</c> to filter (also returns market-wide items).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<NewsItemDto>>>> GetRecent(
        [FromQuery] string? symbol  = null,
        [FromQuery] int     days    = 7,
        CancellationToken ct        = default)
    {
        var items = await news.GetRecentAsync(days, symbol, ct);
        return Ok(ApiResponse<IReadOnlyList<NewsItemDto>>.Ok(items));
    }

    /// <summary>Manually create a news item.</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<Guid>>> Create(
        [FromBody] CreateNewsItemRequest request, CancellationToken ct)
    {
        var id = await news.CreateAsync(request, ct);
        return Ok(ApiResponse<Guid>.Ok(id));
    }

    /// <summary>Delete a news item by ID.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id, CancellationToken ct)
    {
        await news.DeleteAsync(id, ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
