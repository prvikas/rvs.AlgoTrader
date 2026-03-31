using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.Commands.TradeJournal;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.TradeJournal;
using rvs.AlgoTrader.Application.Queries.TradeJournal;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TradeJournalController(IMediator mediator, ITaxLotReportService taxLotService) : ControllerBase
{
    /// <summary>Paginated trade journal. Filter by instance, symbol, exit reason, or source.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<TradeJournalEntryDto>>>> GetAll(
        [FromQuery] Guid?   strategyInstanceId = null,
        [FromQuery] string? symbol             = null,
        [FromQuery] string? exitReason         = null,
        [FromQuery] string? source             = null,
        [FromQuery] int     page               = 1,
        [FromQuery] int     pageSize           = 50,
        CancellationToken   ct                 = default)
    {
        var result = await mediator.Send(
            new GetTradeJournalQuery(strategyInstanceId, symbol, exitReason, source,
                page, Math.Clamp(pageSize, 1, 200)), ct);
        return Ok(ApiResponse<PagedResult<TradeJournalEntryDto>>.Ok(result));
    }

    /// <summary>Single trade journal entry by ID.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<TradeJournalEntryDto>>> GetById(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetTradeJournalEntryQuery(id), ct);
        if (result == null) return NotFound(ApiResponse<object>.Fail("Trade journal entry not found"));
        return Ok(ApiResponse<TradeJournalEntryDto>.Ok(result));
    }

    /// <summary>Update notes and tags on a trade (e.g. annotate with "good-setup" or "mistake").</summary>
    [HttpPatch("{id:guid}/notes")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateNotes(
        Guid id, [FromBody] UpdateTradeJournalNotesRequest request, CancellationToken ct)
    {
        await mediator.Send(new UpdateTradeNotesCommand(id, request.Notes, request.Tags), ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>P&amp;L attribution breakdown — by symbol, month, day-of-week, session, exit type.</summary>
    [HttpGet("attribution")]
    public async Task<ActionResult<ApiResponse<PnlAttributionDto>>> GetAttribution(
        [FromQuery] Guid?   strategyInstanceId = null,
        [FromQuery] string? symbol             = null,
        [FromQuery] string? fromDate           = null,
        [FromQuery] string? toDate             = null,
        CancellationToken   ct                 = default)
    {
        var result = await mediator.Send(
            new GetPnlAttributionQuery(strategyInstanceId, symbol, fromDate, toDate), ct);
        return Ok(ApiResponse<PnlAttributionDto>.Ok(result));
    }

    /// <summary>Tax lot report for ITR-3 filing. Returns rows with STCG/LTCG/Speculative classification.</summary>
    [HttpGet("tax-lots")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TaxLotRow>>>> GetTaxLots(
        [FromQuery] Guid?   strategyInstanceId = null,
        [FromQuery] string  financialYear      = "2025-26",
        CancellationToken   ct                 = default)
    {
        var rows = await taxLotService.GetTaxLotsAsync(strategyInstanceId, financialYear, ct);
        return Ok(ApiResponse<IReadOnlyList<TaxLotRow>>.Ok(rows));
    }

    /// <summary>Export tax lot report as CSV (ITR-3 compatible).</summary>
    [HttpGet("tax-lots/export")]
    public async Task<IActionResult> ExportTaxLotsCsv(
        [FromQuery] Guid?  strategyInstanceId = null,
        [FromQuery] string financialYear      = "2025-26",
        CancellationToken  ct                 = default)
    {
        var csv = await taxLotService.ExportCsvAsync(strategyInstanceId, financialYear, ct);
        return File(csv, "text/csv", $"tax-lots-{financialYear}.csv");
    }
}
