using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.API.Authorization;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// #136: SLO / error-budget tracking endpoint.
///
/// Returns current SLO targets and whether the error budget is exhausted.
/// Detailed time-series metrics are in Prometheus/Grafana at /metrics.
/// This endpoint provides a human-readable summary for operations dashboards.
/// </summary>
[ApiController]
[Route("api/health/slo")]
[Authorize]
public class SloController(IClock clock) : ControllerBase
{
    /// <summary>
    /// Returns the SLO target definitions and the Prometheus scrape endpoint.
    /// Actual real-time compliance should be queried from Grafana using the
    /// metrics published by SloTracker (algotrader_* meter names).
    /// </summary>
    [HttpGet]
    [Authorize(Policy = PolicyNames.Analyst)]
    public ActionResult<ApiResponse<SloReport>> GetSloReport()
    {
        var report = new SloReport(
            AsOf: clock.NowInstant().ToDateTimeOffset(),
            MetricsEndpoint: "/metrics",
            Targets:
            [
                new SloTarget("API p95 Latency",          "algotrader_api_request_duration_ms",          "<= 200 ms",  SloSeverity.Critical),
                new SloTarget("Order Placement p95",       "algotrader_order_placement_duration_ms",      "<= 500 ms",  SloSeverity.Critical),
                new SloTarget("Strategy Evaluation p95",   "algotrader_strategy_evaluation_duration_ms",  "<= 50 ms",   SloSeverity.High),
                new SloTarget("Broker API p95",            "algotrader_broker_api_duration_ms",           "<= 1000 ms", SloSeverity.High),
                new SloTarget("API Error Rate",            "algotrader_api_errors_total",                 "< 0.1%",     SloSeverity.Critical),
                new SloTarget("Order Success Rate",        "algotrader_order_errors_total",               "< 0.5%",     SloSeverity.Critical),
            ]);

        return Ok(ApiResponse<SloReport>.Ok(report));
    }
}

public record SloReport(
    DateTimeOffset AsOf,
    string MetricsEndpoint,
    IReadOnlyList<SloTarget> Targets);

public record SloTarget(
    string Name,
    string MetricName,
    string Target,
    SloSeverity Severity);

public enum SloSeverity { Critical, High, Warning }
