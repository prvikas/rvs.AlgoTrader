using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using rvs.AlgoTrader.API.Authorization;
using rvs.AlgoTrader.Application.Commands.Strategy;
using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;
// Alias resolves IClock to the domain interface (not NodaTime.IClock) — consistent with AP-001
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// P8 MCP server — exposes trading state, backtest results, and kill-switch
/// to Claude Code agents and LLM tools without them needing to know internal
/// REST conventions.  See docs/MCP_DESIGN.md for the full API contract.
///
/// All endpoints share the same JWT bearer token as the main API.
/// POST /mcp/kill-switch additionally requires the RiskManager policy.
/// </summary>
[ApiController]
[Route("mcp")]
[Authorize]
public class McpController(
    IMediator                    mediator,
    IKillSwitchService           killSwitch,
    IBacktestJobManager          jobManager,
    IBacktestRunRepository       backtestRunRepo,
    IStrategyInstanceRepository  strategyRepo,
    IClock                       clock) : ControllerBase
{
    // ── GET /mcp/strategy-status ─────────────────────────────────────────────

    /// <summary>
    /// Current runtime state of every running strategy instance.
    /// Reads from DB (instances) + Redis (kill switch). Sub-100 ms target.
    /// </summary>
    [HttpGet("strategy-status")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<McpStrategyStatusDto>>>> GetStrategyStatus(
        CancellationToken ct)
    {
        var instances = await strategyRepo.GetRunningAsync(ct);
        var ksActive  = await killSwitch.IsActiveAsync(ct);

        var result = instances
            .Select(i => new McpStrategyStatusDto(
                StrategyInstanceId: i.Id,
                StrategyName:       i.StrategyType,
                Symbol:             i.InternalSymbol,
                ExecutionMode:      i.ExecutionMode.ToString(),
                SessionState:       MapSessionState(i.Status),
                // Live position count and P&L are not pre-fetched here to stay sub-100 ms.
                // Query GET /api/positions?strategyInstanceId={id} and /api/portfolio for those.
                OpenPositions:      0,
                DailyPnl:           0m,
                KillSwitchActive:   ksActive,
                LastSignalAt:       null))
            .ToList();

        return Ok(ApiResponse<IReadOnlyList<McpStrategyStatusDto>>.Ok(result));
    }

    // ── GET /mcp/backtest-results/{id} ───────────────────────────────────────

    /// <summary>
    /// Summary metrics for a completed backtest job.
    /// Checks in-memory job manager first (recent runs), then falls back to the DB.
    /// Returns HTTP 409 when the job is still running.
    /// </summary>
    [HttpGet("backtest-results/{id}")]
    public async Task<ActionResult<ApiResponse<McpBacktestSummaryDto>>> GetBacktestResult(
        string id, CancellationToken ct)
    {
        // 1. In-memory job (running or recently completed within the 24-hour eviction window).
        var job = jobManager.GetStatus(id);
        if (job is not null)
        {
            if (job.Status is BacktestJobStatus.Queued or BacktestJobStatus.Running)
                return Conflict(ApiResponse<McpBacktestSummaryDto>.Fail(
                    $"Backtest job '{id}' is still {job.Status}. Poll again when status is Completed.",
                    GetCorrelationId()));

            if (job.Result is not null)
                return Ok(ApiResponse<McpBacktestSummaryDto>.Ok(ToMcpSummary(id, job)));
        }

        // 2. Persisted run in DB (completed job whose ID is a GUID).
        if (!Guid.TryParse(id, out var guid))
            return NotFound(ApiResponse<McpBacktestSummaryDto>.Fail(
                $"Backtest run '{id}' not found.", GetCorrelationId()));

        var saved = await backtestRunRepo.GetByIdAsync(guid, ct);
        if (saved is null)
            return NotFound(ApiResponse<McpBacktestSummaryDto>.Fail(
                $"Backtest run '{id}' not found.", GetCorrelationId()));

        return Ok(ApiResponse<McpBacktestSummaryDto>.Ok(new McpBacktestSummaryDto(
            JobId:            saved.Id ?? id,
            StrategyName:     saved.StrategyName,
            Symbol:           saved.Symbol,
            Status:           "Completed",
            Cagr:             saved.TotalReturn,
            MaxDrawdownPct:   saved.MaxDrawdown,
            SharpeRatio:      saved.SharpeRatio,
            TotalTrades:      saved.TotalTrades,
            WinRate:          saved.WinRate,
            NetPnl:           saved.TotalPnl,
            DeploymentRating: saved.DeploymentRating,
            CompletedAt:      saved.StartedAt)));
    }

    // ── POST /mcp/kill-switch ────────────────────────────────────────────────

    /// <summary>
    /// Activates or deactivates the global kill switch.
    /// Dual-writes Redis + DB per AP-015. Requires RiskManager role.
    /// Per-strategy scope is not supported via this endpoint — use
    /// POST /api/strategy/{id}/pause for fine-grained control.
    /// </summary>
    [HttpPost("kill-switch")]
    [Authorize(Policy = PolicyNames.RiskManager)]
    public async Task<ActionResult<ApiResponse<McpKillSwitchResponseDto>>> SetKillSwitch(
        [FromBody] McpKillSwitchRequest request, CancellationToken ct)
    {
        if (request.Scope == "strategy")
            return BadRequest(ApiResponse<McpKillSwitchResponseDto>.Fail(
                "Per-strategy scope is not supported via MCP. Use POST /api/strategy/{id}/pause instead.",
                GetCorrelationId()));

        if (request.Active && string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(ApiResponse<McpKillSwitchResponseDto>.Fail(
                "reason is required when active=true.", GetCorrelationId()));

        var actor        = User.Identity?.Name ?? "MCP";
        var correlationId = GetCorrelationId();

        if (request.Active)
            await mediator.Send(new ActivateKillSwitchCommand(actor, request.Reason!, correlationId), ct);
        else
            await mediator.Send(new DeactivateKillSwitchCommand(actor, correlationId), ct);

        return Ok(ApiResponse<McpKillSwitchResponseDto>.Ok(new McpKillSwitchResponseDto(
            Scope:     "global",
            Active:    request.Active,
            AppliedAt: clock.NowInstant().ToDateTimeOffset(),
            AuditId:   Guid.NewGuid())));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string GetCorrelationId() =>
        HttpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

    /// <summary>Maps StrategyStatus → MCP sessionState vocabulary.</summary>
    private static string MapSessionState(StrategyStatus status) => status switch
    {
        StrategyStatus.Scheduled => "Waiting",
        StrategyStatus.Running   => "Active",
        _                        => "Closed"
    };

    private static McpBacktestSummaryDto ToMcpSummary(string id, BacktestJobStatusDto job)
    {
        var r = job.Result!;
        return new McpBacktestSummaryDto(
            JobId:            id,
            StrategyName:     r.StrategyName,
            Symbol:           r.Symbol,
            Status:           job.Status.ToString(),
            Cagr:             r.TotalReturn,
            MaxDrawdownPct:   r.MaxDrawdown,
            SharpeRatio:      r.SharpeRatio,
            TotalTrades:      r.TotalTrades,
            WinRate:          r.WinRate,
            NetPnl:           r.TotalPnl,
            DeploymentRating: r.DeploymentRating,
            CompletedAt:      r.StartedAt);
    }
}

// ── MCP-specific DTOs ─────────────────────────────────────────────────────────

/// <summary>Runtime state of one strategy instance, as seen by an LLM agent.</summary>
public record McpStrategyStatusDto(
    Guid             StrategyInstanceId,
    string           StrategyName,
    string           Symbol,
    string           ExecutionMode,     // Backtest | Paper | Live
    string           SessionState,      // Waiting | Active | Closed
    int              OpenPositions,
    decimal          DailyPnl,
    bool             KillSwitchActive,
    DateTimeOffset?  LastSignalAt);

/// <summary>Compact backtest summary returned to LLM agents.</summary>
public record McpBacktestSummaryDto(
    string           JobId,
    string           StrategyName,
    string           Symbol,
    string           Status,            // Queued | Running | Completed | Failed
    decimal          Cagr,
    decimal          MaxDrawdownPct,
    decimal          SharpeRatio,
    int              TotalTrades,
    decimal          WinRate,
    decimal          NetPnl,
    string           DeploymentRating,
    DateTimeOffset?  CompletedAt);

/// <summary>Request body for POST /mcp/kill-switch.</summary>
public record McpKillSwitchRequest(
    string   Scope,                     // "global" | "strategy"
    Guid?    StrategyInstanceId,
    bool     Active,
    string?  Reason);

/// <summary>Response body from POST /mcp/kill-switch.</summary>
public record McpKillSwitchResponseDto(
    string          Scope,
    bool            Active,
    DateTimeOffset  AppliedAt,
    Guid            AuditId);
