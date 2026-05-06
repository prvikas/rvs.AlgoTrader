using Microsoft.EntityFrameworkCore;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

public class PositionRepository(AlgoTraderDbContext db) : IPositionRepository
{

    public async Task<Position?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Positions.FindAsync([id], ct);

    public async Task<IReadOnlyList<Position>> GetOpenAsync(CancellationToken ct = default)
        => await db.Positions
            .Include(p => p.Broker)
            .Include(p => p.ProductType)
            .Where(p => p.IsOpen)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Position>> GetBySymbolAsync(string symbol, CancellationToken ct = default)
        => await db.Positions
            .Include(p => p.Broker)
            .Include(p => p.ProductType)
            .Where(p => p.InternalSymbol == symbol)
            .ToListAsync(ct);

    // ── Additional methods used by Infrastructure internally ─────────────────

    public async Task<IReadOnlyList<Position>> GetOpenPositionsAsync(string brokerName, CancellationToken ct = default)
    {
        // Resolve broker name to broker ID first
        var broker = await db.Brokers.FirstOrDefaultAsync(b => b.Name == brokerName, ct);
        if (broker is null) return [];
        return await db.Positions
            .Include(p => p.Broker)
            .Include(p => p.ProductType)
            .Where(p => p.BrokerId == broker.Id && p.IsOpen)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Position>> GetByStrategyRunAsync(Guid strategyRunId, CancellationToken ct = default)
        => await db.Positions
            .Include(p => p.Broker)
            .Include(p => p.ProductType)
            .Where(p => p.StrategyRunId == strategyRunId)
            .ToListAsync(ct);

    public async Task<Position?> GetOpenPositionForSymbolAsync(string brokerName, string symbol, CancellationToken ct = default)
    {
        // Resolve broker name to broker ID first
        var broker = await db.Brokers.FirstOrDefaultAsync(b => b.Name == brokerName, ct);
        if (broker is null) return null;
        return await db.Positions
            .Include(p => p.Broker)
            .Include(p => p.ProductType)
            .FirstOrDefaultAsync(p => p.BrokerId == broker.Id && p.InternalSymbol == symbol && p.IsOpen, ct);
    }

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

    public async Task<IReadOnlyList<Position>> GetClosedTodayAsync(
        IEnumerable<Guid> strategyRunIds, LocalDate dateIst, CancellationToken ct = default)
    {
        var tz = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
        var startInstant = dateIst.AtStartOfDayInZone(tz).ToInstant();
        var endInstant   = dateIst.PlusDays(1).AtStartOfDayInZone(tz).ToInstant();
        var ids = strategyRunIds.ToList();

        return await db.Positions
            .Where(p => !p.IsOpen
                     && p.StrategyRunId.HasValue
                     && ids.Contains(p.StrategyRunId!.Value)
                     && p.ClosedAt >= startInstant
                     && p.ClosedAt < endInstant)
            .ToListAsync(ct);
    }
}
