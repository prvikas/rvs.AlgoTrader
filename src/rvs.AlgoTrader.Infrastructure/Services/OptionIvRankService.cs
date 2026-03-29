using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Computes IV Rank and IV Percentile from 252 days of stored ATM IV snapshots.
/// Requires at least 30 data points to return a result (avoids misleading values early on).
/// </summary>
public class OptionIvRankService(
    IOptionIvHistoryRepository repo,
    IClock clock) : IOptionIvRankService
{
    private const int MinDataPoints = 30;
    private const int LookbackDays  = 252; // 1 trading year

    public async Task<IvRankSnapshot?> GetAsync(string underlyingSymbol, CancellationToken ct)
    {
        var history = await repo.GetRecentAsync(underlyingSymbol, LookbackDays, ct);
        if (history.Count < MinDataPoints) return null;

        var ivValues = history.Select(h => h.AtmIv).ToList();
        decimal currentIv = ivValues[0]; // most recent first
        decimal high52    = ivValues.Max();
        decimal low52     = ivValues.Min();
        decimal range     = high52 - low52;

        decimal ivRank = range == 0
            ? 50m
            : Math.Round((currentIv - low52) / range * 100m, 1);

        int below = ivValues.Count(v => v < currentIv);
        decimal ivPercentile = Math.Round((decimal)below / ivValues.Count * 100m, 1);

        return new IvRankSnapshot(
            UnderlyingSymbol: underlyingSymbol,
            CurrentIv:        currentIv,
            IvRank:           ivRank,
            IvPercentile:     ivPercentile,
            WeekHigh52:       high52,
            WeekLow52:        low52,
            Regime:           IvRankSnapshot.ClassifyRegime(ivRank),
            DataPointsUsed:   ivValues.Count);
    }

    public async Task RecordAsync(string underlyingSymbol, decimal atmIv, CancellationToken ct)
    {
        var today = clock.NowInstant()
            .InZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"])
            .Date;

        var record = new OptionIvHistory
        {
            Id               = Guid.NewGuid(),
            UnderlyingSymbol = underlyingSymbol,
            Date             = today,
            AtmIv            = atmIv,
            RecordedAt       = clock.NowInstant()
        };

        await repo.UpsertAsync(record, ct);
    }
}
