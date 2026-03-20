using Microsoft.EntityFrameworkCore;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

public class StrategyRunRepository(AlgoTraderDbContext db) : IStrategyRunRepository
{

    public async Task<StrategyRun?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.StrategyRuns.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<StrategyRun>> GetByInstanceAsync(Guid instanceId, CancellationToken ct = default)
        => await db.StrategyRuns.Where(r => r.StrategyInstanceId == instanceId).ToListAsync(ct);

    public async Task AddAsync(StrategyRun run, CancellationToken ct = default)
    {
        await db.StrategyRuns.AddAsync(run, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(StrategyRun run, CancellationToken ct = default)
    {
        db.StrategyRuns.Update(run);
        await db.SaveChangesAsync(ct);
    }
}
