using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.Commands.Instruments;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// Manages the three scope filters that control what gets saved to the database
/// during a master-data refresh:
///
///   1. IncludedExchanges     — which exchanges to download (NSE, BSE, NFO, BFO, …)
///   2. IncludedInstrumentTypes — which instrument types to save (Equity, Futures, Options, Index)
///   3. IncludedEquityCategories — which universe categories contribute equity symbols
///
/// All filters are stored in app_config and take effect on the next refresh.
/// Defaults: NSE+BSE+NFO, all four instrument types, NSE_EQUITY category only.
/// MCX / CDS / BFO / NCDEX can be added via PUT and take effect on the next refresh.
/// </summary>
[Route("api/refresh-filters")]
[ApiController]
public class RefreshFiltersController(IAppConfigService config) : ControllerBase
{
    // ── Defaults ──────────────────────────────────────────────────────────────

    // BSE is now included by default so that equity instruments traded on both exchanges
    // are downloaded in a single refresh.  MCX / CDS remain opt-in (large data volumes).
    private const string DefaultExchanges         = "NSE,BSE,NFO";
    private const string DefaultInstrumentTypes   = "Equity,Futures,Options,Index";
    private const string DefaultEquityCategories  = "NSE_EQUITY";

    // Known exchange values shown in the UI
    private static readonly string[] KnownExchanges =
        ["NSE", "BSE", "NFO", "BFO", "CDS", "MCX", "NCDEX", "BCD"];

    // ── GET ───────────────────────────────────────────────────────────────────

    /// <summary>Get all three refresh-scope filters.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<RefreshFiltersDto>>> Get(CancellationToken ct)
    {
        var exchanges   = await config.GetAsync<string>("InstrumentFilter:IncludedExchanges", ct)
                          ?? DefaultExchanges;
        var types       = await config.GetAsync<string>("InstrumentFilter:IncludedInstrumentTypes", ct)
                          ?? DefaultInstrumentTypes;
        var categories  = await config.GetAsync<string>("InstrumentFilter:IncludedEquityCategories", ct)
                          ?? DefaultEquityCategories;

        var dto = new RefreshFiltersDto(
            IncludedExchanges:        NormalizeList(exchanges),
            IncludedInstrumentTypes:  NormalizeList(types),
            IncludedEquityCategories: NormalizeList(categories),
            KnownExchanges:           KnownExchanges,
            KnownInstrumentTypes:     ["Equity", "Futures", "Options", "Index"],
            KnownEquityCategories:    [.. UniverseCategories.EquityCategories]
        );

        return Ok(ApiResponse<RefreshFiltersDto>.Ok(dto));
    }

    // ── PUT ───────────────────────────────────────────────────────────────────

    /// <summary>Update all three refresh-scope filters at once.</summary>
    [HttpPut]
    public async Task<ActionResult<ApiResponse<RefreshFiltersDto>>> Update(
        [FromBody] UpdateRefreshFiltersRequest request,
        CancellationToken ct)
    {
        var actor         = User.Identity?.Name ?? "API";
        var correlationId = HttpContext.TraceIdentifier;

        if (request.IncludedExchanges is not null)
        {
            var normalized = NormalizeList(request.IncludedExchanges);
            if (normalized.Length == 0)
                return BadRequest(ApiResponse<RefreshFiltersDto>.Fail("IncludedExchanges cannot be empty."));
            await config.SetAsync("InstrumentFilter:IncludedExchanges", normalized, actor, correlationId, ct);
        }

        if (request.IncludedInstrumentTypes is not null)
        {
            var normalized = NormalizeList(request.IncludedInstrumentTypes);
            if (normalized.Length == 0)
                return BadRequest(ApiResponse<RefreshFiltersDto>.Fail("IncludedInstrumentTypes cannot be empty."));
            await config.SetAsync("InstrumentFilter:IncludedInstrumentTypes", normalized, actor, correlationId, ct);
        }

        if (request.IncludedEquityCategories is not null)
        {
            var normalized = NormalizeList(request.IncludedEquityCategories);
            // Empty is allowed — means no equity symbols will be included (derivatives-only mode)
            await config.SetAsync("InstrumentFilter:IncludedEquityCategories", normalized, actor, correlationId, ct);
        }

        return await Get(ct);
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    /// <summary>Reset all three filters to their defaults.</summary>
    [HttpPost("reset-defaults")]
    public async Task<ActionResult<ApiResponse<RefreshFiltersDto>>> ResetDefaults(CancellationToken ct)
    {
        var actor         = User.Identity?.Name ?? "API";
        var correlationId = HttpContext.TraceIdentifier;

        await config.SetAsync("InstrumentFilter:IncludedExchanges",        DefaultExchanges,        actor, correlationId, ct);
        await config.SetAsync("InstrumentFilter:IncludedInstrumentTypes",  DefaultInstrumentTypes,  actor, correlationId, ct);
        await config.SetAsync("InstrumentFilter:IncludedEquityCategories", DefaultEquityCategories, actor, correlationId, ct);

        return await Get(ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Normalise a comma-separated list: trim, preserve original casing, deduplicate, sort.</summary>
    private static string NormalizeList(string raw) =>
        string.Join(",", raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => v.Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase));
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

/// <summary>Current state of all three refresh-scope filters plus metadata for the UI.</summary>
public record RefreshFiltersDto(
    string   IncludedExchanges,
    string   IncludedInstrumentTypes,
    string   IncludedEquityCategories,
    string[] KnownExchanges,
    string[] KnownInstrumentTypes,
    string[] KnownEquityCategories
);

/// <summary>Partial-update request — null fields are left unchanged.</summary>
public record UpdateRefreshFiltersRequest(
    string? IncludedExchanges,
    string? IncludedInstrumentTypes,
    string? IncludedEquityCategories
);
