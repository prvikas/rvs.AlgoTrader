using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Stores daily IV snapshots from mStock option chain into iv_history and pre-computes
/// IV Rank (20-day and 52-week) and IV Percentile (52-week) using SQL PERCENT_RANK().
///
/// Called by the EOD job after market close, once the option chain snapshot is available.
/// The iv_history table (migration 034) is the source for the pre-computed rank columns.
/// The pre-computed values avoid repeated computation in OptionIvRankService hot path.
///
/// Note: OptionIvRankService (IOptionIvRankService) is the authoritative live-computation path.
/// This service provides persistence and pre-computed snapshots for analytics/reporting.
/// </summary>
public sealed class IvHistoryService(
    AlgoTraderDbContext db,
    IClock clock,
    ILogger<IvHistoryService> logger) : IIvHistoryService
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    /// <summary>
    /// Records today's ATM IV for <paramref name="symbol"/> and re-computes rank columns
    /// using SQL PERCENT_RANK() over the prior 252 trading days.
    /// Idempotent — ON CONFLICT updates the row.
    /// </summary>
    public async Task RecordAndComputeAsync(string symbol, decimal ivClose, CancellationToken ct)
    {
        var today = clock.NowInstant().InZone(Ist).Date;
        var todayStr = today.ToString("yyyy-MM-dd", null);

        // Upsert today's raw IV
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO iv_history (internal_symbol, date, iv_close)
            VALUES ({0}, {1}::date, {2})
            ON CONFLICT (internal_symbol, date)
            DO UPDATE SET iv_close = EXCLUDED.iv_close
            """,
            symbol, todayStr, ivClose, ct);

        // Recompute ranks using SQL PERCENT_RANK() — efficient single-query update
        await db.Database.ExecuteSqlRawAsync(
            """
            WITH ranked AS (
                SELECT
                    id,
                    PERCENT_RANK() OVER (PARTITION BY internal_symbol ORDER BY iv_close) AS pct_rank_52w,
                    (
                        SELECT PERCENT_RANK() OVER (ORDER BY iv_close)
                        FROM (
                            SELECT iv_close
                            FROM iv_history h2
                            WHERE h2.internal_symbol = h1.internal_symbol
                              AND h2.date > CURRENT_DATE - INTERVAL '20 days'
                            ORDER BY date DESC
                        ) sub
                        LIMIT 1
                    ) AS pct_rank_20d
                FROM iv_history h1
                WHERE h1.internal_symbol = {0}
                  AND h1.date >= CURRENT_DATE - INTERVAL '365 days'
            )
            UPDATE iv_history
            SET
                iv_rank_52w       = ROUND((ranked.pct_rank_52w * 100)::numeric, 4),
                iv_percentile_52w = ROUND((ranked.pct_rank_52w * 100)::numeric, 4),
                iv_rank_20d       = ROUND((COALESCE(ranked.pct_rank_20d, ranked.pct_rank_52w) * 100)::numeric, 4)
            FROM ranked
            WHERE iv_history.id = ranked.id
              AND iv_history.date = {1}::date
            """,
            symbol, todayStr, ct);

        logger.LogInformation("[IvHistoryService] Recorded iv_close={Iv} for {Symbol} on {Date}", ivClose, symbol, today);
    }

    /// <summary>
    /// Returns the latest pre-computed IVP row for <paramref name="symbol"/>.
    /// Returns null when fewer than 30 days of history exist.
    /// </summary>
    public async Task<IvHistoryRow?> GetLatestAsync(string symbol, CancellationToken ct)
    {
        var rows = await db.Database.SqlQueryRaw<IvHistoryRow>(
            """
            SELECT internal_symbol, date, iv_close, iv_rank_20d, iv_rank_52w, iv_percentile_52w
            FROM iv_history
            WHERE internal_symbol = {0}
            ORDER BY date DESC
            LIMIT 1
            """,
            symbol).ToListAsync(ct);

        return rows.FirstOrDefault();
    }
}

/// <summary>Pre-computed IV history row from the iv_history table.</summary>
public record IvHistoryRow(
    string  InternalSymbol,
    DateOnly Date,
    decimal IvClose,
    decimal? IvRank20d,
    decimal? IvRank52w,
    decimal? IvPercentile52w);

/// <summary>Records and retrieves pre-computed IV history snapshots.</summary>
public interface IIvHistoryService
{
    Task RecordAndComputeAsync(string symbol, decimal ivClose, CancellationToken ct);
    Task<IvHistoryRow?> GetLatestAsync(string symbol, CancellationToken ct);
}
