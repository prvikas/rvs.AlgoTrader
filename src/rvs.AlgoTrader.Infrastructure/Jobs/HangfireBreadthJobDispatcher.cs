using Hangfire;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Jobs;

/// <summary>
/// Dispatches the breadth computation job via Hangfire.
/// Controllers inject <see cref="IBreadthJobDispatcher"/> (Application interface)
/// so they don't need a hard dependency on Infrastructure.Jobs.
/// </summary>
public sealed class HangfireBreadthJobDispatcher : IBreadthJobDispatcher
{
    public string Enqueue()
        => BackgroundJob.Enqueue<BreadthCalculatorJob>(j => j.RunAsync(CancellationToken.None));
}
