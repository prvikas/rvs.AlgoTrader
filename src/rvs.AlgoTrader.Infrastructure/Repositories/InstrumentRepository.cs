using Microsoft.EntityFrameworkCore;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

public class InstrumentRepository(AlgoTraderDbContext db) : IInstrumentRepository
{

    // ── IInstrumentRepository interface methods ──────────────────────────────

    public async Task<Instrument?> GetBySymbolAsync(string internalSymbol, CancellationToken ct = default)
        => await db.Instruments.FirstOrDefaultAsync(i => i.InternalSymbol == internalSymbol, ct);

    public async Task<Instrument?> GetByInternalSymbolAsync(string symbol, CancellationToken ct = default)
        => await db.Instruments.FirstOrDefaultAsync(i => i.InternalSymbol == symbol, ct);

    public async Task<IReadOnlyList<Instrument>> SearchAsync(string query, int limit, CancellationToken ct = default)
    {
        var q = db.Instruments.AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var lower = query.ToLower();
            q = q.Where(i =>
                i.InternalSymbol.ToLower().Contains(lower) ||
                i.TradingSymbol.ToLower().Contains(lower) ||
                i.Name.ToLower().Contains(lower) ||
                i.Exchange.ToLower().Contains(lower));
        }
        return await q.Take(limit).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Instrument>> GetAllActiveAsync(CancellationToken ct = default)
        => await db.Instruments.Where(i => i.IsActive).ToListAsync(ct);

    public async Task UpsertAsync(Instrument instrument, CancellationToken ct = default)
    {
        var existing = await GetByInternalSymbolAsync(instrument.InternalSymbol, ct);
        if (existing == null)
            await db.Instruments.AddAsync(instrument, ct);
        else
        {
            existing.TradingSymbol    = instrument.TradingSymbol;
            existing.Name             = instrument.Name;
            existing.Exchange         = instrument.Exchange;
            existing.InstrumentType   = instrument.InstrumentType;
            existing.Underlying       = instrument.Underlying;
            existing.StrikePrice      = instrument.StrikePrice;
            existing.OptionType       = instrument.OptionType;
            existing.Expiry           = instrument.Expiry;
            existing.LotSize          = instrument.LotSize;
            existing.TickSize         = instrument.TickSize;
            existing.IsActive         = instrument.IsActive;
            existing.ZerodhaToken     = instrument.ZerodhaToken;
            existing.UpstoxToken      = instrument.UpstoxToken;
            existing.MStockToken      = instrument.MStockToken;
            existing.LastRefreshedAt  = instrument.LastRefreshedAt;
            db.Instruments.Update(existing);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertAsync(IEnumerable<Instrument> instruments, CancellationToken ct = default)
    {
        foreach (var instrument in instruments)
            await UpsertAsync(instrument, ct);
    }

    // ── Additional methods used by Infrastructure internally ─────────────────

    public async Task<IReadOnlyList<Instrument>> GetByExchangeAsync(string exchange, CancellationToken ct = default)
        => await db.Instruments.Where(i => i.Exchange == exchange && i.IsActive).ToListAsync(ct);

    public async Task<Instrument?> GetByTradingSymbolAsync(string tradingSymbol, string exchange, CancellationToken ct = default)
        => await db.Instruments.FirstOrDefaultAsync(i => i.TradingSymbol == tradingSymbol && i.Exchange == exchange, ct);
}
