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
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

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

    // ── ICandleRepository.GetOrAggregateAsync ────────────────────────────────

    public async Task<IReadOnlyList<ClosedCandle>> GetOrAggregateAsync(
        string symbol, string targetTimeframe, Instant from, Instant to, CancellationToken ct)
    {
        // 1. Try stored candles at the exact target timeframe
        var stored = await GetAsync(symbol, targetTimeframe, from, to, ct);
        if (stored.Count >= 1)
            return stored;

        // 2. No stored data — fall back to 1m candles and aggregate on the fly
        // (1d is the one case where we still prefer direct download — not aggregated from 1m
        //  because an NSE trading day is 375 bars = expensive + boundary complex)
        if (targetTimeframe == "1m" || targetTimeframe == "1d")
            return stored; // nothing to aggregate from/to

        var oneMinute = await GetAsync(symbol, "1m", from, to, ct);
        if (oneMinute.Count == 0)
            return stored; // no source data at all

        return AggregateCandles(oneMinute, symbol, targetTimeframe);
    }

    /// <summary>
    /// Aggregates a list of 1m ClosedCandles into larger bars for the target timeframe.
    /// Groups bars by their aligned bar-start time; produces OHLCV for each group.
    /// </summary>
    private static IReadOnlyList<ClosedCandle> AggregateCandles(
        IReadOnlyList<ClosedCandle> oneMinuteCandles, string symbol, string targetTimeframe)
    {
        var minutes = TimeframeToMinutes(targetTimeframe);
        if (minutes <= 0) return Array.Empty<ClosedCandle>();

        // Group 1m candles by their aligned bar-start (floor to nearest N minutes)
        var groups = oneMinuteCandles
            .GroupBy(c =>
            {
                var local = c.OpenTime.LocalDateTime;
                // Align to the start of the N-minute bar
                var alignedMinute = (local.Minute / minutes) * minutes;
                return new LocalDateTime(local.Year, local.Month, local.Day, local.Hour, alignedMinute, 0)
                    .InZoneLeniently(Ist);
            })
            .OrderBy(g => g.Key.ToInstant())
            .ToList();

        var result = new List<ClosedCandle>(groups.Count);
        foreach (var group in groups)
        {
            var bars = group.OrderBy(c => c.OpenTime.ToInstant()).ToList();
            if (bars.Count == 0) continue;

            var open    = bars[0].Open;
            var high    = bars.Max(b => b.High);
            var low     = bars.Min(b => b.Low);
            var close   = bars[^1].Close;
            var volume  = bars.Sum(b => b.Volume);
            var barEnd  = group.Key.Plus(Duration.FromMinutes(minutes));

            result.Add(new ClosedCandle(symbol, targetTimeframe, group.Key, barEnd,
                open, high, low, close, volume));
        }

        return result;
    }

    /// <summary>Returns the number of minutes in a timeframe string, -1 if unsupported.</summary>
    private static int TimeframeToMinutes(string tf) => tf switch
    {
        "3m"  => 3,
        "5m"  => 5,
        "15m" => 15,
        "30m" => 30,
        "60m" => 60,
        _     => -1   // "1m" and "1d" handled separately above
    };

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
