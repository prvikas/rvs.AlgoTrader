using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.DTOs.Screener;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Jobs;

/// <summary>
/// Hangfire EOD job — runs the equity screener after market close and logs
/// the top VCP breakout candidates for the day.
/// No persistence: results are logged and available on-demand via GET /api/screener.
/// </summary>
public class ScreenerJob(
    IScreenerService screener,
    ILogger<ScreenerJob> log)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        log.LogInformation("[ScreenerJob] Running daily equity scan");

        var results = await screener.ScanAsync(
            new ScreenerFilters(MinRsScore: 70, Signal: "VCP_BREAKOUT", MaxResults: 20), ct);

        if (results.Count == 0)
        {
            log.LogInformation("[ScreenerJob] No VCP_BREAKOUT candidates today");
            return;
        }

        log.LogInformation("[ScreenerJob] {Count} VCP_BREAKOUT candidates: {Symbols}",
            results.Count,
            string.Join(", ", results.Select(r => $"{r.Symbol}({r.RsScore:F0}%)")));
    }
}
