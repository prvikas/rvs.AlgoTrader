using Hangfire;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Jobs;

/// <summary>
/// EOD Hangfire job — records today's option chain snapshot for every symbol
/// that has an active (Live or ForwardTest) strategy instance.
///
/// Scheduled at 3:45 PM IST (10:15 UTC) — same slot as EodReportJob so all
/// EOD data is captured in one market-close sweep.
///
/// Idempotent: UpsertAsync overwrites the row for the same (symbol, date)
/// so re-runs are safe.
///
/// Graceful degradation: if the option chain service fails for a symbol
/// (broker auth expired, network timeout) the job logs a warning and
/// continues to the next symbol rather than aborting the entire run.
/// </summary>
public class OptionChainSnapshotJob(
    IStrategyInstanceRepository instanceRepo,
    IOptionChainService          ocService,
    IOptionChainSnapshotService  snapshotService,
    IMarketCalendarService       calendar,
    IClock                       clock,
    ILogger<OptionChainSnapshotJob> log)
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    [AutomaticRetry(Attempts = 2, DelaysInSeconds = [300, 600])]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var todayIst = clock.NowInstant().InZone(Ist).Date;

        if (!calendar.IsTradingDay(todayIst))
        {
            log.LogInformation("[OCSnapshot] {Date} is not a trading day — skipping", todayIst);
            return;
        }

        // Collect distinct symbols across all running instances.
        // Strategy instances that reference option-chain data share the same
        // underlying (NIFTY, BANKNIFTY) so de-duplication keeps broker API calls low.
        var instances = await instanceRepo.GetRunningAsync(ct);
        var symbols = instances
            .Select(i => i.InternalSymbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (symbols.Count == 0)
        {
            log.LogInformation("[OCSnapshot] No running instances — nothing to snapshot");
            return;
        }

        log.LogInformation("[OCSnapshot] Recording EOD option chain for {Count} symbols on {Date}",
            symbols.Count, todayIst);

        int recorded = 0;
        foreach (var symbol in symbols)
        {
            try
            {
                var nearExpiry = ocService.GetNearestWeeklyExpiry(symbol);
                var snapshot   = await ocService.GetSnapshotAsync(symbol, nearExpiry, ct);

                if (snapshot == null)
                {
                    log.LogWarning("[OCSnapshot] No snapshot returned for {Symbol} — skipping", symbol);
                    continue;
                }

                await snapshotService.RecordAsync(symbol, todayIst, snapshot, ct);
                recorded++;

                log.LogInformation(
                    "[OCSnapshot] {Symbol}: PCR={Pcr:F3} AtmIV={Iv:F1}% MaxPain={MP} legs={Legs}",
                    symbol,
                    snapshot.PutCallRatioOI,
                    snapshot.AtmIv,
                    snapshot.MaxPainStrike,
                    snapshot.Options.Count);
            }
            catch (Exception ex)
            {
                // Soft failure per symbol — don't abort the whole job
                log.LogWarning(ex, "[OCSnapshot] Failed to record snapshot for {Symbol}", symbol);
            }
        }

        log.LogInformation("[OCSnapshot] Done — recorded {Recorded}/{Total} symbols for {Date}",
            recorded, symbols.Count, todayIst);
    }
}
