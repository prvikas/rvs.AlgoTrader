using Microsoft.EntityFrameworkCore;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

public class InstrumentUniverseRepository(AlgoTraderDbContext db) : IInstrumentUniverseRepository
{
    public async Task<(IReadOnlyList<InstrumentUniverse> Items, int TotalCount)> GetPagedAsync(
        string? category, bool? active, int page, int pageSize, CancellationToken ct)
    {
        var q = db.InstrumentUniverse.AsQueryable();
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(u => u.Category == category);
        if (active.HasValue)                       q = q.Where(u => u.IsActive == active.Value);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderBy(u => u.Category).ThenBy(u => u.Symbol)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<InstrumentUniverse?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.InstrumentUniverse.FindAsync([id], ct).AsTask();

    public Task<InstrumentUniverse?> GetBySymbolAndCategoryAsync(
        string symbol, string category, CancellationToken ct) =>
        db.InstrumentUniverse.FirstOrDefaultAsync(
            u => u.Symbol == symbol && u.Category == category, ct);

    public async Task AddAsync(InstrumentUniverse entry, CancellationToken ct)
    {
        db.InstrumentUniverse.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(InstrumentUniverse entry, CancellationToken ct)
    {
        db.InstrumentUniverse.Update(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(InstrumentUniverse entry, CancellationToken ct)
    {
        db.InstrumentUniverse.Remove(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task<HashSet<string>> GetExistingKeysAsync(CancellationToken ct)
    {
        var pairs = await db.InstrumentUniverse
            .Select(u => new { u.Symbol, u.Category })
            .ToListAsync(ct);
        return pairs.Select(p => $"{p.Symbol}|{p.Category}").ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task AddRangeAsync(IReadOnlyList<InstrumentUniverse> entries, CancellationToken ct)
    {
        if (entries.Count == 0) return;
        db.InstrumentUniverse.AddRange(entries);
        await db.SaveChangesAsync(ct);
    }
}
