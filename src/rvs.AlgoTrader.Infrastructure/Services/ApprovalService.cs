using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Implements the P4 Approval Gate.
/// Thresholds (from APPROVAL_CRITERIA.md):
/// Backtest CAGR &gt;= 20%, max drawdown &lt;= 20%, fwd test days &gt;= 15, fwd win rate &gt;= 40%.
/// Manual approval is ALWAYS required even when all automated checks pass.
/// </summary>
public class ApprovalService(
    IStrategyInstanceRepository instanceRepo,
    IBacktestRunRepository backtestRepo,
    IForwardTestSessionRepository sessionRepo,
    IStrategyApprovalRepository approvalRepo,
    IAuditLogRepository auditLog,
    IClock clock,
    ILogger<ApprovalService> logger) : IApprovalService
{
    // Approval thresholds — authoritative values from APPROVAL_CRITERIA.md
    private const decimal MinCagrPct        = 0.20m;   // 20%
    private const decimal MaxDrawdownPct    = 0.20m;   // 20%
    private const int     MinForwardDays    = 15;
    private const decimal MinForwardWinRate = 0.40m;   // 40%

    public async Task<ApprovalCheckResult> RunChecksAsync(Guid instanceId, CancellationToken ct)
    {
        var failed = new List<string>();

        // ── Fetch latest backtest ──────────────────────────────────────────────
        var (backtests, _) = await backtestRepo.GetPagedAsync(instanceId, 1, 1, ct);
        var bt = backtests.FirstOrDefault();

        decimal? cagr        = null;
        decimal? drawdown    = null;
        decimal? sharpe      = null;

        if (bt == null)
        {
            failed.Add("No backtest result found for this strategy instance.");
        }
        else
        {
            // Compute CAGR from TotalReturn and date range
            cagr     = ComputeCagr(bt.TotalReturn, bt.FromDate, bt.ToDate);
            drawdown = bt.MaxDrawdown;
            sharpe   = bt.DailySharpe;

            if (cagr < MinCagrPct)
                failed.Add($"CAGR {cagr:P1} < required {MinCagrPct:P0}");
            if (drawdown > MaxDrawdownPct)
                failed.Add($"Max drawdown {drawdown:P1} > limit {MaxDrawdownPct:P0}");
        }

        // ── Fetch latest forward test session ─────────────────────────────────
        var sessions = await sessionRepo.GetByInstanceAsync(instanceId, ct);
        var fwdSession = sessions
            .Where(s => s.Status == "Stopped" && s.EndedAt != null)
            .OrderByDescending(s => s.EndedAt)
            .FirstOrDefault();

        int?     fwdDays    = null;
        decimal? fwdWinRate = null;

        if (fwdSession == null)
        {
            failed.Add("No completed forward test session found for this strategy instance.");
        }
        else
        {
            fwdDays    = (int)(fwdSession.EndedAt!.Value - fwdSession.StartedAt).TotalDays;
            fwdWinRate = fwdSession.WinRate;

            if (fwdDays < MinForwardDays)
                failed.Add($"Forward test duration {fwdDays} days < required {MinForwardDays}");
            if (fwdWinRate < MinForwardWinRate)
                failed.Add($"Forward test win rate {fwdWinRate:P1} < required {MinForwardWinRate:P0}");
        }

        bool passed = failed.Count == 0;

        logger.LogInformation(
            "[Approval] Automated checks for instance {InstanceId}: {Result}. Failures: [{Failures}]",
            instanceId, passed ? "PASS" : "FAIL", string.Join("; ", failed));

        return new ApprovalCheckResult(passed, cagr, drawdown, sharpe, fwdDays, fwdWinRate, failed);
    }

    public async Task<StrategyApproval> ApproveAsync(
        Guid instanceId, string approvedBy, string? notes, CancellationToken ct)
    {
        var instance = await instanceRepo.GetByIdAsync(instanceId, ct)
            ?? throw new InvalidOperationException($"Strategy instance {instanceId} not found.");

        var checks = await RunChecksAsync(instanceId, ct);

        // Fetch linked IDs for snapshot
        var (backtests, _) = await backtestRepo.GetPagedAsync(instanceId, 1, 1, ct);
        var bt = backtests.FirstOrDefault();

        var sessions = await sessionRepo.GetByInstanceAsync(instanceId, ct);
        var fwdSession = sessions
            .Where(s => s.Status == "Stopped" && s.EndedAt != null)
            .OrderByDescending(s => s.EndedAt)
            .FirstOrDefault();

        var now = clock.NowInstant();

        var approval = new StrategyApproval
        {
            Id                    = Guid.NewGuid(),
            StrategyInstanceId    = instanceId,
            ApprovedBy            = approvedBy,
            ApprovalNotes         = notes,
            BacktestResultId      = bt?.Id != null ? Guid.TryParse(bt.Id, out var btId) ? btId : null : null,
            ForwardTestSessionId  = fwdSession?.Id,
            CagrAtApproval        = checks.Cagr,
            DrawdownAtApproval    = checks.Drawdown,
            SharpeAtApproval      = checks.Sharpe,
            ForwardTestDays       = checks.ForwardTestDays,
            ForwardWinRate        = checks.ForwardWinRate,
            AutomatedChecksPassed = checks.AutomatedChecksPassed,
            CreatedAt             = now
        };

        await approvalRepo.AddAsync(approval, ct);

        // Update strategy instance approval state
        instance.ApprovalReady = checks.AutomatedChecksPassed;
        instance.ApprovedAt    = now;
        await instanceRepo.UpdateAsync(instance, ct);

        // Audit log — INSERT-only (AP-009)
        await auditLog.AppendAsync(
            action:        "strategy.approved",
            actor:         approvedBy,
            entityType:    "StrategyInstance",
            entityId:      instanceId.ToString(),
            details:       new { approvalId = approval.Id, automatedChecksPassed = checks.AutomatedChecksPassed, notes },
            correlationId: Guid.NewGuid().ToString("N"),
            occurredAt:    now,
            ct:            ct);

        logger.LogInformation(
            "[Approval] Instance {InstanceId} approved by {Actor} (automatedChecks={Passed})",
            instanceId, approvedBy, checks.AutomatedChecksPassed);

        return approval;
    }

    public async Task RevokeAsync(Guid approvalId, string reason, string actor, CancellationToken ct)
    {
        var now = clock.NowInstant();

        var approval = await approvalRepo.GetByIdAsync(approvalId, ct)
            ?? throw new InvalidOperationException($"Approval {approvalId} not found.");

        await approvalRepo.InvalidateAsync(approvalId, reason, now, ct);

        // Clear approved_at on the strategy instance
        var instance = await instanceRepo.GetByIdAsync(approval.StrategyInstanceId, ct);
        if (instance != null)
        {
            instance.ApprovalReady = false;
            instance.ApprovedAt    = null;
            await instanceRepo.UpdateAsync(instance, ct);
        }

        await auditLog.AppendAsync(
            action:        "strategy.approval_revoked",
            actor:         actor,
            entityType:    "StrategyApproval",
            entityId:      approvalId.ToString(),
            details:       new { reason, instanceId = approval.StrategyInstanceId },
            correlationId: Guid.NewGuid().ToString("N"),
            occurredAt:    now,
            ct:            ct);

        logger.LogInformation("[Approval] Approval {ApprovalId} revoked by {Actor}: {Reason}",
            approvalId, actor, reason);
    }

    public Task<StrategyApproval?> GetActiveApprovalAsync(Guid instanceId, CancellationToken ct)
        => approvalRepo.GetActiveAsync(instanceId, ct);

    public Task<IReadOnlyList<StrategyApproval>> GetHistoryAsync(Guid instanceId, CancellationToken ct)
        => approvalRepo.GetHistoryAsync(instanceId, ct);

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static decimal ComputeCagr(decimal totalReturn, NodaTime.LocalDate from, NodaTime.LocalDate to)
    {
        var years = (to - from).Days / 365.25;
        if (years <= 0) return totalReturn;
        return (decimal)(Math.Pow((double)(1m + totalReturn), 1.0 / years) - 1.0);
    }
}
