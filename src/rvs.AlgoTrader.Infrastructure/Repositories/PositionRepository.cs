using Microsoft.EntityFrameworkCore;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

public class PositionRepository(AlgoTraderDbContext db) : IPositionRepository
{

    public async Task<Position?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Positions.FindAsync([id], ct);

    public async Task<IReadOnlyList<Position>> GetOpenAsync(CancellationToken ct = default)
        => await db.Positions.Where(p => p.IsOpen).ToListAsync(ct);

    public async Task<IReadOnlyList<Position>> GetBySymbolAsync(string symbol, CancellationToken ct = default)
        => await db.Positions.Where(p => p.InternalSymbol == symbol).ToListAsync(ct);

    // ── Additional methods used by Infrastructure internally ─────────────────

    public async Task<IReadOnlyList<Position>> GetOpenPositionsAsync(string brokerName, CancellationToken ct = default)
        => await db.Positions.Where(p => p.BrokerName == brokerName && p.IsOpen).ToListAsync(ct);

    public async Task<IReadOnlyList<Position>> GetByStrategyRunAsync(Guid strategyRunId, CancellationToken ct = default)
        => await db.Positions.Where(p => p.StrategyRunId == strategyRunId).ToListAsync(ct);

    public async Task<Position?> GetOpenPositionForSymbolAsync(string brokerName, string symbol, CancellationToken ct = default)
        => await db.Positions.FirstOrDefaultAsync(p => p.BrokerName == brokerName && p.InternalSymbol == symbol && p.IsOpen, ct);

    public async Task AddAsync(Position position, CancellationToken ct = default)
    {
        await db.Positions.AddAsync(position, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Position position, CancellationToken ct = default)
    {
        db.Positions.Update(position);
        await db.SaveChangesAsync(ct);
    }
}
