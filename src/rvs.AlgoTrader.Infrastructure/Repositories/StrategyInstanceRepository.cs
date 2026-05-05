using Microsoft.EntityFrameworkCore;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

public class StrategyInstanceRepository(
    AlgoTraderDbContext db,
    //IFieldEncryptionService encryption,
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
        // Create the runtime state for this new strategy instance.
        var runtimeState = StrategyRuntimeState.Create(instance.Id, clock.NowInstant());
        instance.RuntimeState = runtimeState;

        // NOTE: BrokerCredential is NOT auto-created here anymore.
        // broker_credentials is now an independent table keyed by broker_name.
        // The credential for this instance's broker must already exist in broker_credentials
        // (seeded by Migration 020 or inserted via the Broker management API).
        // To look up the credential at runtime, use:
        //   IBrokerTimezoneResolver.ResolveAsync(instance.BrokerName)
        // or query db.BrokerCredentials.FindAsync(instance.BrokerName)

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
