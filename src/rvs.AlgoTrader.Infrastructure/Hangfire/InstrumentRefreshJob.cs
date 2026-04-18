using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Application.Options;

namespace rvs.AlgoTrader.Infrastructure.Hangfire;

public class InstrumentRefreshJob(
    IInstrumentRefreshService refreshService,
    IOptions<FeaturesOptions> featuresOptions,
    ILogger<InstrumentRefreshJob> logger)
{
    [AutomaticRetry(Attempts = 3)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        if (!featuresOptions.Value.BrokerRequired)
        {
            logger.LogDebug("[InstrumentRefresh] Skipped — BrokerRequired=false (backtest-only mode)");
            return;
        }

        logger.LogInformation("[InstrumentRefresh] Starting daily instrument refresh");
        await refreshService.RefreshAllBrokersAsync(ct);
        logger.LogInformation("[InstrumentRefresh] Complete");
    }
}
