using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.HistoricalData;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// Admin data-manager endpoints: quality reports, gap filling, CSV import.
/// All endpoints require authentication; production should add an admin role gate.
/// </summary>
[ApiController]
[Route("api/data-manager")]
[Authorize]
public class DataManagerController(IHistoricalDataManager manager) : ControllerBase
{
    /// <summary>Data quality report for a single symbol/timeframe.</summary>
    [HttpGet("quality/{symbol}/{timeframe}")]
    public async Task<ActionResult<ApiResponse<DataQualityReportDto>>> GetQuality(
        [FromRoute] string symbol,
        [FromRoute] string timeframe,
        CancellationToken ct)
    {
        var report = await manager.GetQualityReportAsync(symbol, timeframe, ct);
        return Ok(ApiResponse<DataQualityReportDto>.Ok(report));
    }

    /// <summary>Quality reports for all symbols at a given timeframe (default 1D).</summary>
    [HttpGet("quality")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DataQualityReportDto>>>> GetAllQuality(
        [FromQuery] string timeframe = "1D",
        CancellationToken ct = default)
    {
        var reports = await manager.GetAllQualityReportsAsync(timeframe, ct);
        return Ok(ApiResponse<IReadOnlyList<DataQualityReportDto>>.Ok(reports));
    }

    /// <summary>Enqueue a historical download job for a symbol/timeframe range.</summary>
    [HttpPost("download")]
    public async Task<ActionResult<ApiResponse<Guid>>> EnqueueDownload(
        [FromBody] HistoricalDownloadRequest request, CancellationToken ct)
    {
        var jobId = await manager.EnqueueDownloadAsync(request, ct);
        return Accepted(ApiResponse<Guid>.Ok(jobId));
    }

    /// <summary>Detect and fill data gaps for a symbol/timeframe by re-downloading missing ranges.</summary>
    [HttpPost("fill-gaps/{symbol}/{timeframe}")]
    public async Task<ActionResult<ApiResponse<int>>> FillGaps(
        [FromRoute] string symbol,
        [FromRoute] string timeframe,
        [FromQuery] string? brokerName = null,
        CancellationToken ct = default)
    {
        var count = await manager.FillGapsAsync(symbol, timeframe, brokerName, ct);
        return Ok(ApiResponse<int>.Ok(count));
    }

    /// <summary>
    /// Bulk import OHLCV data from a CSV file.
    /// Expected format: <c>date,open,high,low,close,volume</c> (header row required).
    /// </summary>
    [HttpPost("import-csv/{symbol}/{timeframe}")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB
    public async Task<ActionResult<ApiResponse<CsvImportResultDto>>> ImportCsv(
        [FromRoute] string symbol,
        [FromRoute] string timeframe,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<CsvImportResultDto>.Fail("No file uploaded."));

        await using var stream = file.OpenReadStream();
        var result = await manager.ImportCsvAsync(symbol, timeframe, stream, ct);
        return Ok(ApiResponse<CsvImportResultDto>.Ok(result));
    }
}
