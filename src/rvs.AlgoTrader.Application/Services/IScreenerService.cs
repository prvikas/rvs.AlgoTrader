using NodaTime;
using rvs.AlgoTrader.Application.DTOs.Screener;

namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// Symbol screener — scans the candles table for equity instruments that meet
/// trend-template criteria (above SMA-200/50, near 52-week high, volume confirmation).
/// Results are computed on-demand via a single SQL CTE; no separate cache table needed.
/// </summary>
public interface IScreenerService
{
    /// <summary>
    /// Scan for symbols matching the filter criteria.
    /// Returns results ranked by RS score descending, capped at <see cref="ScreenerFilters.MaxResults"/>.
    /// </summary>
    Task<IReadOnlyList<ScreenerResultDto>> ScanAsync(
        ScreenerFilters filters,
        CancellationToken ct = default);
}
