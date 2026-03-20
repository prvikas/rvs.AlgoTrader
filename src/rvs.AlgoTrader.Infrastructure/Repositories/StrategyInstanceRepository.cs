using Microsoft.EntityFrameworkCore;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

public class StrategyInstanceRepository(AlgoTraderDbContext db) : IStrategyInstanceRepository
{

    public async Task<StrategyInstance?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.StrategyInstances.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<StrategyInstance>> GetAllAsync(CancellationToken ct = default)
        => await db.StrategyInstances.ToListAsync(ct);

    public async Task<IReadOnlyList<StrategyInstance>> GetAllActiveAsync(CancellationToken ct = default)
        => await db.StrategyInstances.Where(s => s.IsActive).ToListAsync(ct);

    public async Task<IReadOnlyList<StrategyInstance>> GetRunningAsync(CancellationToken ct = default)
        => await db.StrategyInstances.Where(s => s.Status == StrategyStatus.Running).ToListAsync(ct);

    public async Task AddAsync(StrategyInstance instance, CancellationToken ct = default)
    {
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
        var instance = await GetByIdAsync(id, ct);
        if (instance != null)
        {
            db.StrategyInstances.Remove(instance);
            await db.SaveChangesAsync(ct);
        }
    }
}
