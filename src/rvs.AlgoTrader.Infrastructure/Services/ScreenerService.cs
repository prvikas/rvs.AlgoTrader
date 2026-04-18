using Microsoft.Extensions.Configuration;
using NodaTime;
using Npgsql;
using rvs.AlgoTrader.Application.DTOs.Screener;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Equity screener — runs a single SQL CTE pass over the candles table
/// to find symbols in a confirmed uptrend that are near a 52-week breakout.
/// <para>
/// Signal taxonomy:
/// <list type="bullet">
///   <item><c>VCP_BREAKOUT</c>  — above SMA200/50, close ≥ 95% of 52-week high, volume > 1.2× avg</item>
///   <item><c>NEAR_BREAKOUT</c> — above SMA200/50, close ≥ 85% of 52-week high</item>
///   <item><c>UPTREND</c>       — above SMA200 and SMA50, below the breakout zone</item>
/// </list>
/// </para>
/// </summary>
public class ScreenerService(IConfiguration config, IClock clock) : IScreenerService
{
    private string Cs => config.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Database=algotrader;Username=postgres;Password=postgres";

    public async Task<IReadOnlyList<ScreenerResultDto>> ScanAsync(
        ScreenerFilters filters, CancellationToken ct = default)
    {
        var now       = clock.NowInstant().InZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"]);
        var scannedAt = now.ToDateTimeOffset();
        var lb200     = now.Date.PlusDays(-300)  // >200 trading days back
                           .AtStartOfDayInZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"])
                           .ToInstant().ToDateTimeOffset().ToString("O");
        var yearStart = now.Date.PlusDays(-366)
                           .AtStartOfDayInZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"])
                           .ToInstant().ToDateTimeOffset().ToString("O");
        var vol20Start = now.Date.PlusDays(-30)
                            .AtStartOfDayInZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"])
                            .ToInstant().ToDateTimeOffset().ToString("O");

        int cap = Math.Min(filters.MaxResults, 200);

        var sql = $@"
WITH
  ranked AS (
    SELECT internal_symbol, close, high, low, volume,
           ROW_NUMBER() OVER (PARTITION BY internal_symbol ORDER BY open_time DESC) AS rn
    FROM   candles
    WHERE  timeframe = '1D' AND open_time >= '{lb200}'
  ),
  latest AS (
    SELECT internal_symbol, close, high, low, volume FROM ranked WHERE rn = 1
  ),
  smas AS (
    SELECT internal_symbol,
           AVG(close) FILTER (WHERE rn <= 200) AS sma200,
           AVG(close) FILTER (WHERE rn <= 50)  AS sma50
    FROM   ranked
    GROUP BY internal_symbol
  ),
  yearhl AS (
    SELECT internal_symbol, MAX(high) AS year_high, MIN(low) AS year_low
    FROM   candles
    WHERE  timeframe = '1D' AND open_time >= '{yearStart}'
    GROUP BY internal_symbol
  ),
  vol_avg AS (
    SELECT internal_symbol, AVG(volume) AS avg_vol
    FROM   candles
    WHERE  timeframe = '1D' AND open_time >= '{vol20Start}'
    GROUP BY internal_symbol
  ),
  candidates AS (
    SELECT
      l.internal_symbol                                                          AS symbol,
      l.close                                                                    AS last_close,
      s.sma200                                                                   AS sma200,
      s.sma50                                                                    AS sma50,
      y.year_high                                                                AS year_high,
      y.year_low                                                                 AS year_low,
      ROUND(
        CAST((l.close - y.year_low)
             / NULLIF(y.year_high - y.year_low, 0) * 100 AS NUMERIC), 1)        AS rs_score,
      l.volume                                                                   AS last_volume,
      COALESCE(v.avg_vol, 0)                                                     AS avg_vol,
      CASE
        WHEN l.close > s.sma200 AND l.close > s.sma50
             AND l.close >= y.year_high * 0.95
             AND l.volume > COALESCE(v.avg_vol, 0) * 1.2 THEN 'VCP_BREAKOUT'
        WHEN l.close > s.sma200 AND l.close > s.sma50
             AND l.close >= y.year_high * 0.85             THEN 'NEAR_BREAKOUT'
        WHEN l.close > s.sma200 AND l.close > s.sma50     THEN 'UPTREND'
        ELSE NULL
      END                                                                        AS signal
    FROM latest l
    JOIN smas   s ON s.internal_symbol = l.internal_symbol
    JOIN yearhl y ON y.internal_symbol = l.internal_symbol
    LEFT JOIN vol_avg v ON v.internal_symbol = l.internal_symbol
    WHERE l.close > COALESCE(s.sma200, 0)
      AND s.sma200 IS NOT NULL
  )
SELECT symbol, last_close, sma200, sma50, year_high, year_low,
       COALESCE(rs_score, 0) AS rs_score, last_volume, avg_vol, signal
FROM   candidates
WHERE  signal IS NOT NULL
  AND  COALESCE(rs_score, 0) >= {filters.MinRsScore}
  {(filters.Signal != null ? $"AND signal = '{filters.Signal}'" : "")}
ORDER BY rs_score DESC NULLS LAST
LIMIT  {cap}";

        await using var conn   = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);
        await using var cmd    = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var results = new List<ScreenerResultDto>();
        while (await reader.ReadAsync(ct))
        {
            var avgVol  = reader.GetDecimal(8);
            var lastVol = reader.GetDecimal(7);
            results.Add(new ScreenerResultDto(
                Symbol:           reader.GetString(0),
                LastClose:        reader.GetDecimal(1),
                Sma200:           reader.GetDecimal(2),
                Sma50:            reader.GetDecimal(3),
                YearHigh:         reader.GetDecimal(4),
                YearLow:          reader.GetDecimal(5),
                RsScore:          reader.GetDecimal(6),
                Signal:           reader.GetString(9),
                VolumeConfirmed:  avgVol > 0 && lastVol > avgVol * 1.2m,
                ScannedAt:        scannedAt));
        }

        return results;
    }
}
