using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// Strategy correlation and portfolio construction endpoints (#95).
///
/// POST /api/correlation/matrix       — Pearson correlation matrix for supplied return series
/// POST /api/correlation/portfolio    — Monte Carlo efficient frontier + optimal weights
/// POST /api/correlation/check        — Deployment-time warning: is a new strategy highly correlated with live ones?
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CorrelationController(IStrategyCorrelationAnalyser analyser) : ControllerBase
{
    /// <summary>
    /// Computes a pairwise Pearson correlation matrix for the submitted return series.
    /// Returns the full coefficient grid and any high-correlation pairs (|r| ≥ threshold).
    /// </summary>
    [HttpPost("matrix")]
    public ActionResult<ApiResponse<CorrelationMatrix>> GetMatrix(
        [FromBody] CorrelationMatrixRequest request)
    {
        if (request.Series.Count < 2)
            return BadRequest(ApiResponse<CorrelationMatrix>.Fail("At least 2 return series are required"));

        var matrix = analyser.ComputeMatrix(request.Series, request.HighCorrThreshold);
        return Ok(ApiResponse<CorrelationMatrix>.Ok(matrix));
    }

    /// <summary>
    /// Runs a Monte Carlo efficient frontier and returns optimal portfolio weights (max Sharpe).
    /// Also returns all sampled portfolios for rendering an efficient frontier chart on the frontend.
    /// </summary>
    [HttpPost("portfolio")]
    public ActionResult<ApiResponse<PortfolioConstructionResult>> ConstructPortfolio(
        [FromBody] PortfolioConstructionRequest request)
    {
        if (request.Series.Count < 2)
            return BadRequest(ApiResponse<PortfolioConstructionResult>.Fail("At least 2 return series are required"));

        var result = analyser.ConstructPortfolio(
            request.Series,
            request.PortfolioCount,
            request.RiskFreeRate);

        return Ok(ApiResponse<PortfolioConstructionResult>.Ok(result));
    }

    /// <summary>
    /// Deployment-time check: returns high-correlation pairs between a candidate strategy
    /// and the existing live strategies.  Callers should warn the user when any pair exceeds threshold.
    /// </summary>
    [HttpPost("check")]
    public ActionResult<ApiResponse<IReadOnlyList<HighCorrelationPair>>> CheckDeployment(
        [FromBody] DeploymentCheckRequest request)
    {
        if (request.CandidateSeries is null)
            return BadRequest(ApiResponse<IReadOnlyList<HighCorrelationPair>>.Fail("candidateSeries is required"));

        var allSeries = new List<StrategyReturnSeries>(request.LiveStrategySeries)
        {
            request.CandidateSeries
        };

        var matrix = analyser.ComputeMatrix(allSeries, request.HighCorrThreshold);
        var candidateName = request.CandidateSeries.StrategyName;

        // Filter only pairs involving the candidate
        var warnings = matrix.HighCorrelationPairs
            .Where(p => p.StrategyA == candidateName || p.StrategyB == candidateName)
            .ToList();

        return Ok(ApiResponse<IReadOnlyList<HighCorrelationPair>>.Ok(warnings));
    }
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

public record CorrelationMatrixRequest(
    IReadOnlyList<StrategyReturnSeries> Series,
    double HighCorrThreshold = 0.7);

public record PortfolioConstructionRequest(
    IReadOnlyList<StrategyReturnSeries> Series,
    int PortfolioCount = 10_000,
    double RiskFreeRate = 0.065);

public record DeploymentCheckRequest(
    StrategyReturnSeries CandidateSeries,
    IReadOnlyList<StrategyReturnSeries> LiveStrategySeries,
    double HighCorrThreshold = 0.7);
