using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using rvs.AlgoTrader.API.Authorization;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/v1/strategy-instances/{instanceId:guid}")]
[Authorize]
public class ApprovalController(
    IApprovalService approvalService,
    ICurrentUser currentUser) : ControllerBase
{
    /// <summary>
    /// Run automated pre-approval checks. Does NOT create an approval record.
    /// Use the result to decide whether to proceed with manual approval.
    /// </summary>
    [HttpGet("approval-checks")]
    public async Task<ActionResult<ApiResponse<ApprovalCheckResultDto>>> GetChecks(
        Guid instanceId, CancellationToken ct)
    {
        var result = await approvalService.RunChecksAsync(instanceId, ct);
        return Ok(ApiResponse<ApprovalCheckResultDto>.Ok(MapChecks(result)));
    }

    /// <summary>
    /// Get the current active approval status for a strategy instance.
    /// Returns null data if the instance has never been approved (or approval was revoked).
    /// </summary>
    [HttpGet("approval-status")]
    public async Task<ActionResult<ApiResponse<ApprovalStatusDto?>>> GetStatus(
        Guid instanceId, CancellationToken ct)
    {
        var approval = await approvalService.GetActiveApprovalAsync(instanceId, ct);
        return Ok(ApiResponse<ApprovalStatusDto?>.Ok(approval == null ? null : MapStatus(approval)));
    }

    /// <summary>Full approval history for a strategy instance, newest first.</summary>
    [HttpGet("approval-history")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ApprovalStatusDto>>>> GetHistory(
        Guid instanceId, CancellationToken ct)
    {
        var history = await approvalService.GetHistoryAsync(instanceId, ct);
        return Ok(ApiResponse<IReadOnlyList<ApprovalStatusDto>>.Ok(
            history.Select(MapStatus).ToList()));
    }

    /// <summary>
    /// Manually approve a strategy instance for live trading.
    /// Automated checks are run internally; manual approval is ALWAYS required.
    /// </summary>
    [HttpPost("approve")]
    [Authorize(Policy = PolicyNames.RiskManager)]
    public async Task<ActionResult<ApiResponse<ApprovalStatusDto>>> Approve(
        Guid instanceId,
        [FromBody] ApproveRequest request,
        CancellationToken ct)
    {
        try
        {
            var approval = await approvalService.ApproveAsync(
                instanceId, currentUser.Actor, request.Notes, ct);
            return Ok(ApiResponse<ApprovalStatusDto>.Ok(MapStatus(approval)));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<ApprovalStatusDto>.Fail(ex.Message));
        }
    }

    /// <summary>Revoke an existing approval (e.g. after strategy config change).</summary>
    [HttpPost("approvals/{approvalId:guid}/revoke")]
    [Authorize(Policy = PolicyNames.RiskManager)]
    public async Task<ActionResult<ApiResponse<bool>>> Revoke(
        Guid instanceId,
        Guid approvalId,
        [FromBody] RevokeRequest request,
        CancellationToken ct)
    {
        try
        {
            await approvalService.RevokeAsync(approvalId, request.Reason, currentUser.Actor, ct);
            return Ok(ApiResponse<bool>.Ok(true));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<bool>.Fail(ex.Message));
        }
    }

    // ── Mapping ──────────────────────────────────────────────────────────────

    private static ApprovalCheckResultDto MapChecks(ApprovalCheckResult r) => new(
        r.AutomatedChecksPassed,
        r.Cagr,
        r.Drawdown,
        r.Sharpe,
        r.ForwardTestDays,
        r.ForwardWinRate,
        r.FailedChecks);

    private static ApprovalStatusDto MapStatus(Domain.Entities.StrategyApproval a) => new(
        a.Id,
        a.StrategyInstanceId,
        a.ApprovedBy,
        a.ApprovalNotes,
        a.CagrAtApproval,
        a.DrawdownAtApproval,
        a.SharpeAtApproval,
        a.ForwardTestDays,
        a.ForwardWinRate,
        a.AutomatedChecksPassed,
        a.IsActive,
        a.InvalidatedAt?.ToDateTimeOffset(),
        a.InvalidationReason,
        a.CreatedAt.ToDateTimeOffset());
}

// ── Request / Response DTOs ───────────────────────────────────────────────────

public record ApproveRequest(string? Notes);

public record RevokeRequest(string Reason);

public record ApprovalCheckResultDto(
    bool AutomatedChecksPassed,
    decimal? Cagr,
    decimal? Drawdown,
    decimal? Sharpe,
    int? ForwardTestDays,
    decimal? ForwardWinRate,
    IReadOnlyList<string> FailedChecks);

public record ApprovalStatusDto(
    Guid   Id,
    Guid   StrategyInstanceId,
    string ApprovedBy,
    string? ApprovalNotes,
    decimal? CagrAtApproval,
    decimal? DrawdownAtApproval,
    decimal? SharpeAtApproval,
    int?    ForwardTestDays,
    decimal? ForwardWinRate,
    bool   AutomatedChecksPassed,
    bool   IsActive,
    DateTimeOffset? InvalidatedAt,
    string? InvalidationReason,
    DateTimeOffset CreatedAt);
