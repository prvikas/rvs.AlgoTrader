using Microsoft.EntityFrameworkCore;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

public class CandleRepository(AlgoTraderDbContext db) : ICandleRepository
{
    private static readonly DateTimeZone Utc = DateTimeZone.Utc;

    // ── ICandleRepository interface methods ──────────────────────────────────

    public async Task<IReadOnlyList<ClosedCandle>> GetAsync(
        string symbol, string timeframe, Instant from, Instant to, CancellationToken ct = default)
    {
        var candles = await db.Candles
            .Where(c => c.InternalSymbol == symbol && c.Timeframe == timeframe
                        && c.IsClosed && c.OpenTime >= from && c.CloseTime <= to)
            .OrderBy(c => c.OpenTime)
            .ToListAsync(ct);
        return candles.Select(ToClosedCandle).ToList();
    }

    public async Task<IReadOnlyList<ClosedCandle>> GetLastNAsync(
        string symbol, string timeframe, int count, CancellationToken ct = default)
    {
        var candles = await db.Candles
            .Where(c => c.InternalSymbol == symbol && c.Timeframe == timeframe && c.IsClosed)
            .OrderByDescending(c => c.OpenTime)
            .Take(count)
            .OrderBy(c => c.OpenTime)
            .ToListAsync(ct);
        return candles.Select(ToClosedCandle).ToList();
    }

    public async Task BulkInsertAsync(IEnumerable<ClosedCandle> candles, CancellationToken ct = default)
    {
        var entities = candles.Select(ToEntity).ToList();
        await db.Candles.AddRangeAsync(entities, ct);
        await db.SaveChangesAsync(ct);
    }

    // ── Additional methods used by Infrastructure internally ─────────────────

    public async Task<IReadOnlyList<Candle>> GetLatestCandlesAsync(
        string symbol, string timeframe, int count, CancellationToken ct = default)
        => await db.Candles
            .Where(c => c.InternalSymbol == symbol && c.Timeframe == timeframe && c.IsClosed)
            .OrderByDescending(c => c.OpenTime)
            .Take(count)
            .OrderBy(c => c.OpenTime)
            .ToListAsync(ct);

    public async Task<Candle?> GetLastClosedCandleAsync(string symbol, string timeframe, CancellationToken ct = default)
        => await db.Candles
            .Where(c => c.InternalSymbol == symbol && c.Timeframe == timeframe && c.IsClosed)
            .OrderByDescending(c => c.OpenTime)
            .FirstOrDefaultAsync(ct);

    public async Task UpsertAsync(Candle candle, CancellationToken ct = default)
    {
        var existing = await db.Candles
            .FirstOrDefaultAsync(c => c.InternalSymbol == candle.InternalSymbol
                && c.Timeframe == candle.Timeframe && c.OpenTime == candle.OpenTime, ct);
        if (existing == null)
            await db.Candles.AddAsync(candle, ct);
        else
        {
            existing.Open = candle.Open;
            existing.High = candle.High;
            existing.Low = candle.Low;
            existing.Close = candle.Close;
            existing.Volume = candle.Volume;
            existing.IsClosed = candle.IsClosed;
            existing.CloseTime = candle.CloseTime;
            db.Candles.Update(existing);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> HasDataAsync(string symbol, string timeframe, DateOnly date, CancellationToken ct = default)
        => await db.Candles.AnyAsync(c => c.InternalSymbol == symbol && c.Timeframe == timeframe, ct);

    // ── Conversion helpers ───────────────────────────────────────────────────

    private static ClosedCandle ToClosedCandle(Candle c) => new(
        c.InternalSymbol, c.Timeframe,
        new ZonedDateTime(c.OpenTime, DateTimeZone.Utc),
        new ZonedDateTime(c.CloseTime, DateTimeZone.Utc),
        c.Open, c.High, c.Low, c.Close, c.Volume);

    private static Candle ToEntity(ClosedCandle c) => new()
    {
        Id = Guid.NewGuid(),
        InternalSymbol = c.InternalSymbol,
        Timeframe = c.Timeframe,
        OpenTime = c.OpenTime.ToInstant(),
        CloseTime = c.CloseTime.ToInstant(),
        Open = c.Open,
        High = c.High,
        Low = c.Low,
        Close = c.Close,
        Volume = c.Volume,
        IsClosed = true
    };
}
