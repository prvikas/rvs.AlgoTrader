using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.Settings;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// Manages instrument type classifications (futures vs options).
/// Users can customize which broker-specific type codes are classified as futures or options,
/// affecting which instruments are downloaded during refresh operations.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public class InstrumentTypesController(IAppConfigService config) : ControllerBase
{
    /// <summary>Get configured futures instrument type codes.</summary>
    /// <remarks>
    /// Returns a comma-separated list of type codes that classify instruments as futures.
    /// Example: "FUT,FUTIDX,FUTSTK,FUTURES,IF,SF,FUTCOM,FUTCUR,FUTIRD"
    /// </remarks>
    [HttpGet("futures")]
    public async Task<ActionResult<ApiResponse<string>>> GetFuturesTypes(CancellationToken ct)
    {
        var types = await config.GetAsync<string>("InstrumentFilter:FuturesTypes", ct)
            ?? "FUT,FUTIDX,FUTSTK,FUTURES,IF,SF,FUTCOM,FUTCUR,FUTIRD";
        return Ok(ApiResponse<string>.Ok(types));
    }

    /// <summary>Get configured options instrument type codes.</summary>
    /// <remarks>
    /// Returns a comma-separated list of type codes that classify instruments as options.
    /// Example: "OPT,OPTIDX,OPTSTK,OPTIONS,CE,PE,IO,SO"
    /// </remarks>
    [HttpGet("options")]
    public async Task<ActionResult<ApiResponse<string>>> GetOptionsTypes(CancellationToken ct)
    {
        var types = await config.GetAsync<string>("InstrumentFilter:OptionsTypes", ct)
            ?? "OPT,OPTIDX,OPTSTK,OPTIONS,CE,PE,IO,SO";
        return Ok(ApiResponse<string>.Ok(types));
    }

    /// <summary>Update configured futures instrument type codes.</summary>
    /// <remarks>
    /// Accepts a comma-separated list of type codes. Whitespace is trimmed automatically.
    /// Changes take effect on the next instrument refresh operation.
    /// </remarks>
    [HttpPut("futures")]
    public async Task<ActionResult<ApiResponse<string>>> UpdateFuturesTypes(
        [FromBody] UpdateInstrumentTypesRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Types))
            return BadRequest(ApiResponse<string>.Fail("Types list cannot be empty."));

        var actor = User.Identity?.Name ?? "API";
        var correlationId = HttpContext.TraceIdentifier;
        var normalized = NormalizeTypesList(request.Types);
        await config.SetAsync("InstrumentFilter:FuturesTypes", normalized, actor, correlationId, ct);
        return Ok(ApiResponse<string>.Ok(normalized));
    }

    /// <summary>Update configured options instrument type codes.</summary>
    /// <remarks>
    /// Accepts a comma-separated list of type codes. Whitespace is trimmed automatically.
    /// Changes take effect on the next instrument refresh operation.
    /// </remarks>
    [HttpPut("options")]
    public async Task<ActionResult<ApiResponse<string>>> UpdateOptionsTypes(
        [FromBody] UpdateInstrumentTypesRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Types))
            return BadRequest(ApiResponse<string>.Fail("Types list cannot be empty."));

        var actor = User.Identity?.Name ?? "API";
        var correlationId = HttpContext.TraceIdentifier;
        var normalized = NormalizeTypesList(request.Types);
        await config.SetAsync("InstrumentFilter:OptionsTypes", normalized, actor, correlationId, ct);
        return Ok(ApiResponse<string>.Ok(normalized));
    }

    /// <summary>Reset to default instrument type mappings.</summary>
    [HttpPost("reset-defaults")]
    public async Task<ActionResult<ApiResponse<string>>> ResetDefaults(CancellationToken ct)
    {
        const string defaultFutures = "FUT,FUTIDX,FUTSTK,FUTURES,IF,SF,FUTCOM,FUTCUR,FUTIRD";
        const string defaultOptions = "OPT,OPTIDX,OPTSTK,OPTIONS,CE,PE,IO,SO";

        var actor = User.Identity?.Name ?? "API";
        var correlationId = HttpContext.TraceIdentifier;

        await config.SetAsync("InstrumentFilter:FuturesTypes", defaultFutures, actor, correlationId, ct);
        await config.SetAsync("InstrumentFilter:OptionsTypes", defaultOptions, actor, correlationId, ct);

        return Ok(ApiResponse<string>.Ok("Defaults restored"));
    }

    /// <summary>Normalize a comma-separated type list: trim whitespace, uppercase, deduplicate.</summary>
    private static string NormalizeTypesList(string raw) =>
        string.Join(",", raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToUpperInvariant())
            .Distinct()
            .OrderBy(t => t));
}

// UpdateInstrumentTypesRequest moved to Application/DTOs/Settings/InstrumentTypesDtos.cs
