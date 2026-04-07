using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.MarketData;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// Economic and corporate event calendar endpoints.
/// Distinct from market-calendar (hours/holidays) — this covers RBI policy,
/// earnings, budget day, F&amp;O expiry, etc.
/// </summary>
[ApiController]
[Route("api/event-calendar")]
[Authorize]
public class EventCalendarController(IEventCalendarService events) : ControllerBase
{
    /// <summary>Get events for a specific date (yyyy-MM-dd).</summary>
    [HttpGet("{date}")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MarketEventDto>>>> GetByDate(
        [FromRoute] string date, CancellationToken ct)
    {
        var dp = LocalDatePattern.Iso.Parse(date);
        if (!dp.Success) return BadRequest(ApiResponse<IReadOnlyList<MarketEventDto>>.Fail("date must be ISO: yyyy-MM-dd"));
        return Ok(ApiResponse<IReadOnlyList<MarketEventDto>>.Ok(await events.GetByDateAsync(dp.Value, ct)));
    }

    /// <summary>Get events in a date range.</summary>
    [HttpGet("range")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MarketEventDto>>>> GetRange(
        [FromQuery] string from,
        [FromQuery] string to,
        [FromQuery] string? eventType = null,
        [FromQuery] string? symbol    = null,
        CancellationToken ct = default)
    {
        var fp = LocalDatePattern.Iso.Parse(from);
        var tp = LocalDatePattern.Iso.Parse(to);
        if (!fp.Success || !tp.Success)
            return BadRequest(ApiResponse<IReadOnlyList<MarketEventDto>>.Fail("from/to must be ISO: yyyy-MM-dd"));
        var f = fp.Value; var t = tp.Value;
        return Ok(ApiResponse<IReadOnlyList<MarketEventDto>>.Ok(
            await events.GetRangeAsync(f, t, eventType, symbol, ct)));
    }

    /// <summary>Upcoming events in the next N days (default 7).</summary>
    [HttpGet("upcoming")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MarketEventDto>>>> GetUpcoming(
        [FromQuery] int days = 7, CancellationToken ct = default)
    {
        return Ok(ApiResponse<IReadOnlyList<MarketEventDto>>.Ok(
            await events.GetUpcomingAsync(Math.Clamp(days, 1, 90), ct)));
    }

    /// <summary>[Admin] Create a new market event.</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<Guid>>> Create(
        [FromBody] CreateMarketEventRequest request, CancellationToken ct)
    {
        var id = await events.CreateAsync(request, ct);
        return Ok(ApiResponse<Guid>.Ok(id));
    }

    /// <summary>[Admin] Update title/description/impact for an existing event.</summary>
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Update(
        [FromRoute] Guid id, [FromBody] UpdateMarketEventRequest request, CancellationToken ct)
    {
        await events.UpdateAsync(id, request, ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>
    /// [Admin] Import corporate actions from NSE Bhavcopy CSV.
    /// Expected columns: SYMBOL, PURPOSE, EX_DATE (yyyy-MM-dd or dd/MM/yyyy).
    /// Returns the count of events created.
    /// </summary>
    [HttpPost("import")]
    public async Task<ActionResult<ApiResponse<int>>> ImportCsv(
        IFormFile file,
        [FromServices] INseEventCalendarImporter importer,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<int>.Fail("CSV file is required"));

        using var reader = new System.IO.StreamReader(file.OpenReadStream());
        var csv = await reader.ReadToEndAsync(ct);
        var count = await importer.ImportCsvAsync(csv, ct);
        return Ok(ApiResponse<int>.Ok(count));
    }

    /// <summary>[Admin] Delete an event.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(
        [FromRoute] Guid id, CancellationToken ct)
    {
        await events.DeleteAsync(id, ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>[Admin] Seed NSE F&amp;O weekly/monthly expiry dates for a year.</summary>
    [HttpPost("seed-fno-expiries/{year:int}")]
    public async Task<ActionResult<ApiResponse<bool>>> SeedFnoExpiries(
        [FromRoute] int year, CancellationToken ct)
    {
        if (year < 2020 || year > 2040)
            return BadRequest(ApiResponse<bool>.Fail("year must be between 2020 and 2040"));
        await events.SeedFnoExpiriesAsync(year, ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
