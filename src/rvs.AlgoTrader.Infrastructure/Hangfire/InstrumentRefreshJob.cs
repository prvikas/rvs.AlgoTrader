using Hangfire;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Hangfire;

public class InstrumentRefreshJob(IInstrumentRefreshService refreshService, ILogger<InstrumentRefreshJob> logger)
{
    [AutomaticRetry(Attempts = 3)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("[InstrumentRefresh] Starting daily instrument refresh");
        await refreshService.RefreshAllBrokersAsync(ct);
        logger.LogInformation("[InstrumentRefresh] Complete");
    }
}
