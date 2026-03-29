using NodaTime;
using rvs.AlgoTrader.Application.DTOs.MarketData;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// Computes and retrieves NSE market-breadth indicators.
/// Breadth data is computed EOD from Bhavcopy + candle history and persisted to
/// <c>market_breadth_snapshots</c>. Strategies query this before generating signals
/// to avoid entering during bear-market regimes.
/// </summary>
public interface IMarketBreadthService
{
    /// <summary>Get the latest available breadth snapshot.</summary>
    Task<MarketBreadthDto?> GetLatestAsync(CancellationToken ct = default);

    /// <summary>Get the snapshot for a specific calendar date (IST).</summary>
    Task<MarketBreadthDto?> GetByDateAsync(LocalDate date, CancellationToken ct = default);

    /// <summary>Get the last <paramref name="days"/> daily snapshots ordered newest-first.</summary>
    Task<IReadOnlyList<MarketBreadthDto>> GetHistoryAsync(int days = 30, CancellationToken ct = default);

    /// <summary>
    /// Compute and persist the breadth snapshot for <paramref name="date"/>.
    /// Called by the BreadthCalculatorJob Hangfire job after Bhavcopy download completes.
    /// Idempotent — re-computing for an existing date overwrites the prior snapshot.
    /// </summary>
    Task<MarketBreadthDto> ComputeAndSaveAsync(LocalDate date, CancellationToken ct = default);

    /// <summary>
    /// Returns the current market regime for the latest snapshot.
    /// Returns <see cref="MarketRegime.Neutral"/> when no snapshot is available.
    /// </summary>
    Task<MarketRegime> GetCurrentRegimeAsync(CancellationToken ct = default);
}
