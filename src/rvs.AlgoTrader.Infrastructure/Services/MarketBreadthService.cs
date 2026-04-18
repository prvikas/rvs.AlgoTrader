using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.MarketData;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Computes NSE market-breadth indicators from the candle history stored in TimescaleDB.
/// <para>
/// <c>ComputeAndSaveAsync</c> runs directly against the <c>candles</c> table using window
/// functions rather than loading per-symbol data through the repository, so it stays efficient
/// on a universe of 1 000–2 000 symbols.
/// </para>
/// </summary>
public class MarketBreadthService(
    AlgoTraderDbContext db,
    IClock clock,
    ILogger<MarketBreadthService> log) : IMarketBreadthService
{
    public async Task<MarketBreadthDto?> GetLatestAsync(CancellationToken ct = default)
    {
        var row = await db.Database
            .SqlQueryRaw<BreadthRow>(BreadthSelectSql + " ORDER BY snapshot_date DESC LIMIT 1")
            .FirstOrDefaultAsync(ct);
        return row is null ? null : ToDto(row);
    }

    public async Task<MarketBreadthDto?> GetByDateAsync(LocalDate date, CancellationToken ct = default)
    {
        var iso = date.ToString("yyyy-MM-dd", null);
        var row = await db.Database
            .SqlQueryRaw<BreadthRow>(BreadthSelectSql + $" WHERE snapshot_date = '{iso}'")
            .FirstOrDefaultAsync(ct);
        return row is null ? null : ToDto(row);
    }

    public async Task<IReadOnlyList<MarketBreadthDto>> GetHistoryAsync(int days = 30, CancellationToken ct = default)
    {
        var rows = await db.Database
            .SqlQueryRaw<BreadthRow>(BreadthSelectSql + $" ORDER BY snapshot_date DESC LIMIT {days}")
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<MarketRegime> GetCurrentRegimeAsync(CancellationToken ct = default)
    {
        var latest = await GetLatestAsync(ct);
        if (latest is null) return MarketRegime.Neutral;
        return Enum.TryParse<MarketRegime>(latest.Regime, out var r) ? r : MarketRegime.Neutral;
    }

    public async Task<MarketBreadthDto> ComputeAndSaveAsync(LocalDate date, CancellationToken ct = default)
    {
        log.LogInformation("Computing market breadth for {Date}", date);

        // Timestamps bounding the target date (UTC — candles stored as UTC InstantZ)
        var dayStart = date.AtStartOfDayInZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"]).ToInstant();
        var dayEnd   = date.PlusDays(1).AtStartOfDayInZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"]).ToInstant();
        var lookback200Start = date.PlusDays(-300)
            .AtStartOfDayInZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"]).ToInstant();
        var yearStart = date.PlusDays(-365)
            .AtStartOfDayInZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"]).ToInstant();

        var ds = dayStart.ToDateTimeOffset().ToString("O");
        var de = dayEnd.ToDateTimeOffset().ToString("O");
        var lb200 = lookback200Start.ToDateTimeOffset().ToString("O");
        var ys = yearStart.ToDateTimeOffset().ToString("O");

        // Single SQL pass: for each symbol that has a candle on the target date,
        // compute SMA200, SMA50, 52-week hi/lo, advance vs prior close.
        var breadthSql = $@"
WITH
  today AS (
    SELECT internal_symbol, close, high, low
    FROM   candles
    WHERE  timeframe = '1D' AND open_time >= '{ds}' AND open_time < '{de}'
  ),
  prior AS (
    SELECT DISTINCT ON (internal_symbol) internal_symbol, close AS prior_close
    FROM   candles
    WHERE  timeframe = '1D' AND open_time >= '{lb200}' AND open_time < '{ds}'
    ORDER  BY internal_symbol, open_time DESC
  ),
  sma200 AS (
    SELECT internal_symbol,
           AVG(close) FILTER (WHERE rn <= 200) AS sma200_val,
           AVG(close) FILTER (WHERE rn <= 50)  AS sma50_val
    FROM (
      SELECT internal_symbol, close,
             ROW_NUMBER() OVER (PARTITION BY internal_symbol ORDER BY open_time DESC) AS rn
      FROM   candles
      WHERE  timeframe = '1D' AND open_time >= '{lb200}' AND open_time < '{de}'
    ) ranked
    GROUP BY internal_symbol
  ),
  yearhl AS (
    SELECT internal_symbol, MAX(high) AS year_high, MIN(low) AS year_low
    FROM   candles
    WHERE  timeframe = '1D' AND open_time >= '{ys}' AND open_time < '{de}'
    GROUP  BY internal_symbol
  )
SELECT
  COUNT(*)                                                       AS total_symbols,
  COUNT(*) FILTER (WHERE t.close > s.sma200_val)                AS above_200_sma,
  COUNT(*) FILTER (WHERE t.close > s.sma50_val)                 AS above_50_sma,
  COUNT(*) FILTER (WHERE t.high  >= y.year_high)                AS highs_count,
  COUNT(*) FILTER (WHERE t.low   <= y.year_low)                 AS lows_count,
  COUNT(*) FILTER (WHERE p.prior_close IS NOT NULL AND t.close > p.prior_close) AS advancing_count,
  COUNT(*) FILTER (WHERE p.prior_close IS NOT NULL AND t.close < p.prior_close) AS declining_count
FROM today t
LEFT JOIN sma200 s ON s.internal_symbol = t.internal_symbol
LEFT JOIN yearhl y ON y.internal_symbol = t.internal_symbol
LEFT JOIN prior  p ON p.internal_symbol = t.internal_symbol";

        var result = await db.Database
            .SqlQueryRaw<BreadthComputeRow>(breadthSql)
            .FirstOrDefaultAsync(ct);

        result ??= new BreadthComputeRow();

        var snapshot = MarketBreadthSnapshot.Create(
            date,
            result.TotalSymbols,
            result.Above200Sma,
            result.Above50Sma,
            result.HighsCount,
            result.LowsCount,
            result.AdvancingCount,
            result.DecliningCount,
            clock.NowInstant());

        // Upsert by snapshot_date
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO market_breadth_snapshots
                (id, snapshot_date, total_symbols, above_200_sma, above_50_sma,
                 highs_count, lows_count, advancing_count, declining_count, regime, computed_at)
            VALUES
                ({snapshot.Id}, {date.ToString("yyyy-MM-dd", null)}::date,
                 {snapshot.TotalSymbols}, {snapshot.Above200Sma}, {snapshot.Above50Sma},
                 {snapshot.HighsCount}, {snapshot.LowsCount},
                 {snapshot.AdvancingCount}, {snapshot.DecliningCount},
                 {snapshot.Regime.ToString()}, {snapshot.ComputedAt.ToDateTimeOffset()})
            ON CONFLICT (snapshot_date) DO UPDATE SET
                total_symbols    = EXCLUDED.total_symbols,
                above_200_sma    = EXCLUDED.above_200_sma,
                above_50_sma     = EXCLUDED.above_50_sma,
                highs_count      = EXCLUDED.highs_count,
                lows_count       = EXCLUDED.lows_count,
                advancing_count  = EXCLUDED.advancing_count,
                declining_count  = EXCLUDED.declining_count,
                regime           = EXCLUDED.regime,
                computed_at      = EXCLUDED.computed_at
            """, ct);

        log.LogInformation(
            "Market breadth for {Date}: {Total} symbols, {Pct200}% above 200-SMA, regime={Regime}",
            date, snapshot.TotalSymbols, snapshot.Pct200Sma, snapshot.Regime);

        return MarketBreadthDto.From(snapshot);
    }

    // ── SQL helpers ───────────────────────────────────────────────────────────

    private const string BreadthSelectSql =
        "SELECT id, snapshot_date, total_symbols, above_200_sma, above_50_sma, " +
        "highs_count, lows_count, advancing_count, declining_count, regime, computed_at " +
        "FROM market_breadth_snapshots";

    private static LocalDate ToLocalDate(DateOnly d) => new(d.Year, d.Month, d.Day);

    private static MarketBreadthDto ToDto(BreadthRow r)
    {
        var snapshotDate = ToLocalDate(r.SnapshotDate);
        var s = MarketBreadthSnapshot.Create(
            snapshotDate,
            r.TotalSymbols, r.Above200Sma, r.Above50Sma,
            r.HighsCount, r.LowsCount, r.AdvancingCount, r.DecliningCount,
            Instant.FromDateTimeOffset(r.ComputedAt));

        return new MarketBreadthDto(
            r.Id,
            snapshotDate,
            r.TotalSymbols, r.Above200Sma, r.Above50Sma, s.Pct200Sma,
            r.HighsCount, r.LowsCount, r.AdvancingCount, r.DecliningCount, s.AdRatio,
            r.Regime, r.ComputedAt);
    }

    // ── Projection types ──────────────────────────────────────────────────────

    private sealed class BreadthRow
    {
        [Column("id")]              public Guid Id { get; set; }
        [Column("snapshot_date")]   public DateOnly SnapshotDate { get; set; }
        [Column("total_symbols")]   public int TotalSymbols { get; set; }
        [Column("above_200_sma")]   public int Above200Sma { get; set; }
        [Column("above_50_sma")]    public int Above50Sma { get; set; }
        [Column("highs_count")]     public int HighsCount { get; set; }
        [Column("lows_count")]      public int LowsCount { get; set; }
        [Column("advancing_count")] public int AdvancingCount { get; set; }
        [Column("declining_count")] public int DecliningCount { get; set; }
        [Column("regime")]          public string Regime { get; set; } = "Neutral";
        [Column("computed_at")]     public DateTimeOffset ComputedAt { get; set; }
    }

    private sealed class BreadthComputeRow
    {
        [Column("total_symbols")]   public int TotalSymbols { get; set; }
        [Column("above_200_sma")]   public int Above200Sma { get; set; }
        [Column("above_50_sma")]    public int Above50Sma { get; set; }
        [Column("highs_count")]     public int HighsCount { get; set; }
        [Column("lows_count")]      public int LowsCount { get; set; }
        [Column("advancing_count")] public int AdvancingCount { get; set; }
        [Column("declining_count")] public int DecliningCount { get; set; }
    }
}
