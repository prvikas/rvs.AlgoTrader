using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Infrastructure.Repositories;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Records daily EOD option chain snapshots and provides pre-load access for BacktestEngine.
///
/// FIB-5: GetHistoryRangeAsync() extends the from-date by 10 days so bars at the very
/// start of a backtest window have a snapshot available (handles weekends / holidays).
/// </summary>
public class OptionChainSnapshotService(
    IOptionChainSnapshotRepository repo) : IOptionChainSnapshotService
{
    // Extra look-back buffer so the first bar of a backtest has a snapshot
    // even when fromDate falls on a weekend or after a long holiday.
    private const int EarlyLoadBuffer = 10;

    public async Task RecordAsync(
        string underlyingSymbol,
        LocalDate date,
        OptionChainSnapshot snapshot,
        CancellationToken ct)
    {
        var legsJson = OptionChainSnapshotSerializer.SerializeLegs(snapshot.Options);

        // Compute aggregates: defend against empty chain
        decimal? pcr        = snapshot.Options.Count > 0 ? snapshot.PutCallRatioOI        : null;
        decimal? pcrChange  = snapshot.Options.Count > 0 ? snapshot.PutCallRatioChangeOI  : null;
        decimal? atmIv      = snapshot.Options.Count > 0 ? snapshot.AtmIv                 : null;
        decimal? maxPain    = snapshot.Options.Count > 0 ? snapshot.MaxPainStrike          : null;
        decimal? ceMoiStrike = snapshot.Options.Count > 0 ? snapshot.CeMaxOiStrike         : null;
        decimal? peMoiStrike = snapshot.Options.Count > 0 ? snapshot.PeMaxOiStrike         : null;

        long? totalCeOi = snapshot.Options.Count > 0
            ? snapshot.Options.Where(o => o.OptionType == "CE").Sum(o => o.OpenInterest)
            : null;
        long? totalPeOi = snapshot.Options.Count > 0
            ? snapshot.Options.Where(o => o.OptionType == "PE").Sum(o => o.OpenInterest)
            : null;

        await repo.UpsertAsync(
            symbol:         underlyingSymbol,
            date:           date,
            expiryDate:     snapshot.Expiry,
            spotPrice:      snapshot.SpotPrice,
            pcr:            pcr,
            pcrChange:      pcrChange,
            atmIv:          atmIv,
            maxPainStrike:  maxPain,
            ceMaxOiStrike:  ceMoiStrike,
            peMaxOiStrike:  peMoiStrike,
            totalCeOi:      totalCeOi,
            totalPeOi:      totalPeOi,
            legsJson:       legsJson,
            ct:             ct);
    }

    public async Task<IReadOnlyList<(LocalDate Date, OptionChainSnapshot Snapshot)>>
        GetHistoryRangeAsync(
            string underlyingSymbol,
            LocalDate from,
            LocalDate to,
            CancellationToken ct)
    {
        // Pull EarlyLoadBuffer extra days before fromDate so the earliest bars
        // in the backtest window have a snapshot even after weekends/holidays.
        var extendedFrom = from.PlusDays(-EarlyLoadBuffer);
        var rows = await repo.GetRangeAsync(underlyingSymbol, extendedFrom, to, ct);

        return rows
            .Select(r => (r.Date, BuildSnapshot(r, underlyingSymbol)))
            .ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static OptionChainSnapshot BuildSnapshot(
        OptionChainSnapshotRow row, string underlyingSymbol)
    {
        var legs = OptionChainSnapshotSerializer.DeserializeLegs(row.LegsJson);

        // Use the stored snapshot's record timestamp as FetchedAt.
        // For backtest purposes any fixed Instant is fine — strategies read
        // context.OptionChain.SpotPrice / PCR, not FetchedAt.
        var fetchedAt = row.Date.AtStartOfDayInZone(
            DateTimeZoneProviders.Tzdb["Asia/Kolkata"]).ToInstant();

        return new OptionChainSnapshot(
            UnderlyingSymbol: underlyingSymbol,
            FetchedAt:        fetchedAt,
            SpotPrice:        row.SpotPrice,
            Expiry:           row.ExpiryDate,
            Options:          legs);
    }
}
