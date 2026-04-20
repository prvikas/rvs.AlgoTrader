using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

public class CandleRepository(AlgoTraderDbContext db, ILogger<CandleRepository> logger) : ICandleRepository
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

    // #142: Replace EF Core batched INSERT with a single PostgreSQL COPY FROM STDIN.
    // COPY streams rows directly into the server without per-row round-trips — roughly
    // 10–20× faster than batched INSERT for large historical downloads (10K+ bars).
    //
    // Idempotency: a single range-query fetches existing open_time keys, then new rows
    // are filtered before the COPY so there are no PK conflicts.  Re-downloading the
    // same date range is safe.
    public async Task BulkInsertAsync(IEnumerable<ClosedCandle> candles, CancellationToken ct = default)
    {
        var entities = candles.Select(ToEntity).ToList();
        if (entities.Count == 0) return;

        var symbol    = entities[0].InternalSymbol;
        var timeframe = entities[0].Timeframe;
        var minTime   = entities.Min(e => e.OpenTime);
        var maxTime   = entities.Max(e => e.OpenTime);

        var existingKeys = await db.Candles
            .Where(c => c.InternalSymbol == symbol && c.Timeframe == timeframe
                        && c.OpenTime >= minTime && c.OpenTime <= maxTime)
            .Select(c => c.OpenTime)
            .ToHashSetAsync(ct);

        entities = entities.Where(e => !existingKeys.Contains(e.OpenTime)).ToList();

        if (entities.Count == 0)
        {
            logger.LogDebug("[CandleRepo] BulkInsert: all candles already present — skipped");
            return;
        }

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            const string CopySql =
                "COPY candles " +
                "(internal_symbol, timeframe, open_time, close_time, " +
                " open, high, low, close, volume, is_closed) " +
                "FROM STDIN (FORMAT BINARY)";

            await using var writer = await conn.BeginBinaryImportAsync(CopySql, ct);

            foreach (var e in entities)
            {
                await writer.StartRowAsync(ct);
                await writer.WriteAsync(e.InternalSymbol,             NpgsqlDbType.Varchar,      ct);
                await writer.WriteAsync(e.Timeframe,                  NpgsqlDbType.Varchar,      ct);
                await writer.WriteAsync(e.OpenTime,                   NpgsqlDbType.TimestampTz,  ct);
                await writer.WriteAsync(e.CloseTime,                  NpgsqlDbType.TimestampTz,  ct);
                await writer.WriteAsync(e.Open,                       NpgsqlDbType.Numeric,      ct);
                await writer.WriteAsync(e.High,                       NpgsqlDbType.Numeric,      ct);
                await writer.WriteAsync(e.Low,                        NpgsqlDbType.Numeric,      ct);
                await writer.WriteAsync(e.Close,                      NpgsqlDbType.Numeric,      ct);
                await writer.WriteAsync(e.Volume,                     NpgsqlDbType.Bigint,       ct);
                await writer.WriteAsync(e.IsClosed,                   NpgsqlDbType.Boolean,      ct);
            }

            await writer.CompleteAsync(ct);
        }
        finally
        {
            db.Database.CloseConnection();
        }

        logger.LogDebug("[CandleRepo] BulkInsert: {Count} candles written via COPY", entities.Count);
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

    // #145: Replace the two-round-trip SELECT + INSERT/UPDATE pattern with a single
    // atomic INSERT … ON CONFLICT DO UPDATE (PostgreSQL "upsert").
    // The candles table PRIMARY KEY is (internal_symbol, timeframe, open_time), so the
    // conflict target matches that constraint — no separate SELECT ever needed.
    public async Task UpsertAsync(Candle candle, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO candles
                (internal_symbol, timeframe, open_time, close_time, open, high, low, close, volume, is_closed)
            VALUES
                ({candle.InternalSymbol}, {candle.Timeframe}, {candle.OpenTime}, {candle.CloseTime},
                 {candle.Open}, {candle.High}, {candle.Low}, {candle.Close}, {candle.Volume}, {candle.IsClosed})
            ON CONFLICT (internal_symbol, timeframe, open_time) DO UPDATE SET
                close_time = EXCLUDED.close_time,
                open       = EXCLUDED.open,
                high       = EXCLUDED.high,
                low        = EXCLUDED.low,
                close      = EXCLUDED.close,
                volume     = EXCLUDED.volume,
                is_closed  = EXCLUDED.is_closed
            """, ct);
    }

    public async Task<bool> HasDataAsync(string symbol, string timeframe, DateOnly date, CancellationToken ct = default)
    {
        // AP-014: bound timestamps — check for at least one closed candle on the given IST calendar day.
        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd   = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var from = Instant.FromDateTimeUtc(dayStart);
        var to   = Instant.FromDateTimeUtc(dayEnd);
        return await db.Candles.AnyAsync(
            c => c.InternalSymbol == symbol && c.Timeframe == timeframe
              && c.IsClosed && c.OpenTime >= from && c.OpenTime < to, ct);
    }

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
        {
            logger.LogWarning(
                "[CandleRepo] No stored candles for {Symbol}/{Tf} between {From} and {To}. " +
                "Cannot aggregate (base timeframe). Trigger a historical download first.",
                symbol, targetTimeframe, from, to);
            return stored; // nothing to aggregate from/to
        }

        var oneMinute = await GetAsync(symbol, "1m", from, to, ct);
        if (oneMinute.Count == 0)
        {
            logger.LogWarning(
                "[CandleRepo] No candles for {Symbol} at {Tf} or 1m between {From} and {To}. " +
                "DB is empty for this range — historical download required.",
                symbol, targetTimeframe, from, to);
            return stored; // no source data at all
        }

        logger.LogDebug("[CandleRepo] Aggregating {Count} 1m bars → {Tf} for {Symbol}", oneMinute.Count, targetTimeframe, symbol);
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
