using Microsoft.EntityFrameworkCore;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

public class StrategyScenarioRepository(AlgoTraderDbContext db) : IStrategyScenarioRepository
{
    public async Task<StrategyScenario?> GetByIdAsync(Guid id, CancellationToken ct)
        => await db.StrategyScenarios.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<StrategyScenario>> GetByInstanceAsync(Guid instanceId, CancellationToken ct)
        => await db.StrategyScenarios
            .Where(s => s.StrategyInstanceId == instanceId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(StrategyScenario scenario, CancellationToken ct)
    {
        await db.StrategyScenarios.AddAsync(scenario, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(StrategyScenario scenario, CancellationToken ct)
    {
        db.StrategyScenarios.Update(scenario);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var scenario = await GetByIdAsync(id, ct);
        if (scenario != null)
        {
            db.StrategyScenarios.Remove(scenario);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<bool> HasBacktestAsync(Guid scenarioId, CancellationToken ct)
        => await db.BacktestRuns.AnyAsync(r => r.ScenarioId == scenarioId, ct);
}
