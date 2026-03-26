using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

public class InstrumentRepository(AlgoTraderDbContext db, ILogger<InstrumentRepository> logger) : IInstrumentRepository
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

    /// <summary>
    /// Server-side paged + sorted + filtered query used by the Instruments page.
    /// Returns the current page AND the total matching count so the frontend can
    /// render pagination without a separate COUNT(*) request.
    /// sortBy: "symbol" | "name" | "exchange" | "type" | "trading"
    /// </summary>
    public async Task<(IReadOnlyList<Instrument> Items, int TotalCount)> FilterPagedAsync(
        string? search, string? exchange, string? instrumentType, bool? active,
        string sortBy, bool sortDesc, int pageSize, int offset, CancellationToken ct = default)
    {
        var q = db.Instruments.AsNoTracking().AsQueryable();

        // ── WHERE predicates ─────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(search))
        {
            var lower = search.ToLower();
            q = q.Where(i =>
                i.InternalSymbol.ToLower().Contains(lower) ||
                i.TradingSymbol.ToLower().Contains(lower) ||
                i.Name.ToLower().Contains(lower));
        }
        if (!string.IsNullOrWhiteSpace(exchange))
            q = q.Where(i => i.Exchange == exchange);
        if (!string.IsNullOrWhiteSpace(instrumentType) &&
            Enum.TryParse<InstrumentType>(instrumentType, ignoreCase: true, out var parsedType))
            q = q.Where(i => i.InstrumentType == parsedType);
        if (active.HasValue)
            q = q.Where(i => i.IsActive == active.Value);

        // ── COUNT (same predicates, no ordering/paging) ───────────────────────
        var total = await q.CountAsync(ct);

        // ── ORDER BY ─────────────────────────────────────────────────────────
        q = (sortBy.ToLower(), sortDesc) switch
        {
            ("name",     false) => q.OrderBy(i => i.Name).ThenBy(i => i.InternalSymbol),
            ("name",     true)  => q.OrderByDescending(i => i.Name).ThenBy(i => i.InternalSymbol),
            ("exchange", false) => q.OrderBy(i => i.Exchange).ThenBy(i => i.InternalSymbol),
            ("exchange", true)  => q.OrderByDescending(i => i.Exchange).ThenBy(i => i.InternalSymbol),
            ("type",     false) => q.OrderBy(i => i.InstrumentType).ThenBy(i => i.InternalSymbol),
            ("type",     true)  => q.OrderByDescending(i => i.InstrumentType).ThenBy(i => i.InternalSymbol),
            ("trading",  false) => q.OrderBy(i => i.TradingSymbol),
            ("trading",  true)  => q.OrderByDescending(i => i.TradingSymbol),
            (_,          false) => q.OrderBy(i => i.InternalSymbol),   // "symbol" default
            (_,          true)  => q.OrderByDescending(i => i.InternalSymbol),
        };

        var items = await q.Skip(offset).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    /// <summary>
    /// Loads all instruments whose InternalSymbol is in the supplied set.
    /// One DB round trip. Returned entities are EF-tracked so in-place
    /// mutations are picked up by the next SaveChanges.
    /// </summary>
    public async Task<Dictionary<string, Instrument>> GetBatchByInternalSymbolAsync(
        IReadOnlyList<string> symbols, CancellationToken ct = default)
        => await db.Instruments
            .Where(i => symbols.Contains(i.InternalSymbol))
            .ToDictionaryAsync(i => i.InternalSymbol, StringComparer.OrdinalIgnoreCase, ct);

    /// <summary>
    /// Inserts toAdd in one AddRange then flushes all pending changes
    /// (including updates to tracked entities in toUpdate) in one SaveChanges.
    /// Replaces the N+1 UpsertAsync loop — reduces 200k DB calls to 3.
    /// Success only confirmed after SaveChangesAsync returns rowsAffected > 0.
    /// </summary>
    public async Task BulkUpsertAsync(
        IReadOnlyList<Instrument> toAdd, IReadOnlyList<Instrument> toUpdate, CancellationToken ct = default)
    {
        try
        {
            logger.LogDebug("[BulkUpsert] Starting: {AddCount} to add, {UpdateCount} to update", toAdd.Count, toUpdate.Count);

            // Log DB context state before changes
            var changeTracker = db.ChangeTracker;
            var trackedCount = changeTracker.Entries().Count();
            logger.LogDebug("[BulkUpsert] DbContext tracked entries before: {Count}", trackedCount);

            if (toAdd.Count > 0)
                await db.Instruments.AddRangeAsync(toAdd, ct);

            // Log context state after AddRange
            var afterAddCount = db.ChangeTracker.Entries().Count();
            logger.LogDebug("[BulkUpsert] DbContext tracked entries after AddRange: {Count}", afterAddCount);

            // toUpdate entities are already tracked by the context (returned from
            // GetBatchByInternalSymbolAsync) — their mutations are auto-detected.
            _ = toUpdate; // explicit no-op; keeps the signature symmetric

            logger.LogDebug("[BulkUpsert] Calling SaveChangesAsync...");
            int rowsAffected = await db.SaveChangesAsync(ct);

            logger.LogInformation("[BulkUpsert] SaveChangesAsync completed: {RowsAffected} rows affected", rowsAffected);

            // Success only after rows > 0 confirmed
            if (rowsAffected == 0 && (toAdd.Count > 0 || toUpdate.Count > 0))
            {
                logger.LogError("[BulkUpsert] CRITICAL: SaveChangesAsync returned 0 rows but {AddCount} adds + {UpdateCount} updates were queued!",
                    toAdd.Count, toUpdate.Count);
                throw new InvalidOperationException(
                    $"SaveChangesAsync returned 0 rows affected but {toAdd.Count} adds + {toUpdate.Count} updates were queued");
            }

            logger.LogDebug("[BulkUpsert] Success confirmed with {RowsAffected} rows affected", rowsAffected);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[BulkUpsert] EXCEPTION during SaveChangesAsync: {Message}", ex.Message);
            throw;
        }
    }

    public async Task<IReadOnlyList<Instrument>> GetAllActiveAsync(CancellationToken ct = default)
        => await db.Instruments.Where(i => i.IsActive).ToListAsync(ct);

    // ── Single-row upsert kept for ad-hoc use ─────────────────────────────

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

    /// <summary>
    /// Returns all option legs (CE + PE) for a given underlying and expiry date.
    /// AP-014 compliant: the underlying and expiry form the equivalent of a time-range filter.
    /// </summary>
    public async Task<IReadOnlyList<Instrument>> GetOptionsByUnderlyingAndExpiryAsync(
        string underlyingSymbol, LocalDate expiry, CancellationToken ct = default)
        => await db.Instruments
            .Where(i => i.InstrumentType == InstrumentType.Options
                     && i.Underlying != null
                     && i.Underlying.ToLower() == underlyingSymbol.ToLower()
                     && i.Expiry == expiry
                     && i.IsActive)
            .OrderBy(i => i.StrikePrice)
            .ThenBy(i => i.OptionType.ToString())
            .ToListAsync(ct);

    // ── Additional methods used by Infrastructure internally ─────────────────

    public async Task<IReadOnlyList<Instrument>> GetByExchangeAsync(string exchange, CancellationToken ct = default)
        => await db.Instruments.Where(i => i.Exchange == exchange && i.IsActive).ToListAsync(ct);

    public async Task<Instrument?> GetByTradingSymbolAsync(string tradingSymbol, string exchange, CancellationToken ct = default)
        => await db.Instruments.FirstOrDefaultAsync(i => i.TradingSymbol == tradingSymbol && i.Exchange == exchange, ct);
}
