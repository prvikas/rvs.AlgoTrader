using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Hangfire;

/// <summary>
/// Marks expired derivative instruments (Options, Futures) as inactive so they stop
/// appearing in instrument searches and strategy selection.
///
/// Equities and indices are never deactivated by this job (they have no expiry date).
/// The job is idempotent: re-running it is safe.
/// </summary>
public class DeactivateExpiredInstrumentsJob(
    AlgoTraderDbContext db,
    IClock clock,
    ILogger<DeactivateExpiredInstrumentsJob> logger)
{
    [AutomaticRetry(Attempts = 2)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var today = clock.TodayIst();

        // Only deactivate instruments that have an expiry date set AND it is in the past.
        // Equities (null Expiry) are never touched.
        var expired = await db.Instruments
            .Where(i => i.IsActive && i.Expiry != null && i.Expiry < today)
            .ToListAsync(ct);

        if (expired.Count == 0)
        {
            logger.LogDebug("[ExpiredInstruments] No expired instruments to deactivate");
            return;
        }

        foreach (var inst in expired)
            inst.IsActive = false;

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "[ExpiredInstruments] Deactivated {Count} expired instruments (expiry < {Today}). " +
            "Examples: {Examples}",
            expired.Count,
            today,
            string.Join(", ", expired.Take(5).Select(i => $"{i.TradingSymbol}({i.Expiry})")));
    }
}
