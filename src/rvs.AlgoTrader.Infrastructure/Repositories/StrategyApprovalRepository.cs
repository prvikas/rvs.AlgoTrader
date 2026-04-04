using Microsoft.EntityFrameworkCore;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

public class StrategyApprovalRepository(AlgoTraderDbContext db) : IStrategyApprovalRepository
{
    public async Task<StrategyApproval?> GetByIdAsync(Guid approvalId, CancellationToken ct)
        => await db.StrategyApprovals.FindAsync([approvalId], ct);

    public async Task<StrategyApproval?> GetActiveAsync(Guid instanceId, CancellationToken ct)
        => await db.StrategyApprovals
            .Where(a => a.StrategyInstanceId == instanceId && a.InvalidatedAt == null)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<StrategyApproval>> GetHistoryAsync(Guid instanceId, CancellationToken ct)
        => await db.StrategyApprovals
            .Where(a => a.StrategyInstanceId == instanceId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(StrategyApproval approval, CancellationToken ct)
    {
        await db.StrategyApprovals.AddAsync(approval, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task InvalidateAsync(Guid approvalId, string reason, Instant now, CancellationToken ct)
    {
        var approval = await db.StrategyApprovals.FindAsync([approvalId], ct);
        if (approval == null) return;
        approval.InvalidatedAt       = now;
        approval.InvalidationReason  = reason;
        await db.SaveChangesAsync(ct);
    }
}
