using Microsoft.EntityFrameworkCore;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

public class StrategyInstanceRepository(
    AlgoTraderDbContext db,
    Domain.Interfaces.IClock clock) : IStrategyInstanceRepository
{
    public async Task<StrategyInstance?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await db.StrategyInstances
            .Include(s => s.RuntimeState)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<IReadOnlyList<StrategyInstance>> GetAllAsync(CancellationToken ct = default)
    {
        return await db.StrategyInstances
            .Include(s => s.RuntimeState)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StrategyInstance>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return await db.StrategyInstances
            .Include(s => s.RuntimeState)
            .Where(s => s.Status != StrategyStatus.Stopped)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StrategyInstance>> GetRunningAsync(CancellationToken ct = default)
    {
        return await db.StrategyInstances
            .Include(s => s.RuntimeState)
            .Where(s => s.Status == StrategyStatus.Running)
            .ToListAsync(ct);
    }

    public async Task AddAsync(StrategyInstance instance, CancellationToken ct = default)
    {
        var runtimeState = StrategyRuntimeState.Create(instance.Id, clock.NowInstant());
        instance.RuntimeState = runtimeState;

        // NOTE: BrokerCredential is NOT created here.
        // broker_credentials is now an independent table keyed by broker_name.
        // Credential must already exist — seeded by Migration 020 or via the Broker API.

        await db.StrategyInstances.AddAsync(instance, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(StrategyInstance instance, CancellationToken ct = default)
    {
        db.StrategyInstances.Update(instance);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var instance = await db.StrategyInstances.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (instance != null)
        {
            db.StrategyInstances.Remove(instance);
            await db.SaveChangesAsync(ct);
        }
    }
}
