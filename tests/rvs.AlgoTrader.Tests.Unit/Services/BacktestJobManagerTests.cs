using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Infrastructure.Services;

namespace rvs.AlgoTrader.Tests.Unit.Services;

/// <summary>
/// UT-2: BJ-1 — completed jobs older than 24h are evicted on next Enqueue.
/// Uses an internal accessor via reflection since BacktestJob is internal.
/// </summary>
public class BacktestJobManagerTests
{
    private static BacktestRequestDto MakeDto() => new(
        StrategyName:   "VcpSwing",
        ParametersJson: "{}",
        InternalSymbol: "RELIANCE",
        Timeframe:      "1d",
        FromDate:       new LocalDate(2023, 1, 1),
        ToDate:         new LocalDate(2023, 12, 31));

    [Fact]
    public async Task Eviction_RemovesCompletedJobsOlderThan24h()
    {
        // BJ-1: CleanupOldJobs() evicts terminal jobs older than 24h on each EnqueueAsync.
        // Strategy:
        //   • Enqueue jobId1 and immediately mark stale (Status=Completed, StartedAt=-25h).
        //   • Enqueue jobId2 — this call to EnqueueAsync triggers CleanupOldJobs → jobId1 evicted.
        //   • Assert jobId1 is gone.
        var scopeFactory = BuildMinimalScopeFactory();
        var pusher       = new Mock<IBacktestProgressPusher>();
        pusher.Setup(p => p.PushProgressAsync(It.IsAny<string>(), It.IsAny<BacktestJobStatusDto>()))
              .Returns(Task.CompletedTask);
        pusher.Setup(p => p.PushCompletedAsync(It.IsAny<string>(), It.IsAny<BacktestJobStatusDto>()))
              .Returns(Task.CompletedTask);

        var manager = new BacktestJobManager(scopeFactory, pusher.Object,
            Mock.Of<rvs.AlgoTrader.Domain.Interfaces.IClock>(),
            NullLogger<BacktestJobManager>.Instance);

        // Enqueue first job, then immediately mark it stale (before next Enqueue triggers eviction)
        var jobId1 = await manager.EnqueueAsync(MakeDto(), CancellationToken.None);
        MarkJobStale(jobId1, manager);

        // Act: enqueue another job — triggers CleanupOldJobs(), which evicts jobId1
        await manager.EnqueueAsync(MakeDto(), CancellationToken.None);

        // Assert: stale job evicted
        manager.GetStatus(jobId1).Should().BeNull("completed job >24h should be evicted on next EnqueueAsync");
    }

    [Fact]
    public async Task Eviction_DoesNotRemoveRecentCompletedJobs()
    {
        var scopeFactory = BuildMinimalScopeFactory();
        var pusher       = new Mock<IBacktestProgressPusher>();
        pusher.Setup(p => p.PushProgressAsync(It.IsAny<string>(), It.IsAny<BacktestJobStatusDto>()))
              .Returns(Task.CompletedTask);
        pusher.Setup(p => p.PushCompletedAsync(It.IsAny<string>(), It.IsAny<BacktestJobStatusDto>()))
              .Returns(Task.CompletedTask);

        var manager = new BacktestJobManager(scopeFactory, pusher.Object,
            Mock.Of<rvs.AlgoTrader.Domain.Interfaces.IClock>(),
            NullLogger<BacktestJobManager>.Instance);

        var jobId = await manager.EnqueueAsync(MakeDto(), CancellationToken.None);
        // Mark completed but StartedAt = now (< 24h)
        MarkJobCompleted(jobId, manager);

        // Trigger eviction
        await manager.EnqueueAsync(MakeDto(), CancellationToken.None);

        manager.GetStatus(jobId).Should().NotBeNull("recently completed job must not be evicted");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void MarkJobStale(string jobId, BacktestJobManager manager)
    {
        var job = GetJob(jobId, manager);
        if (job == null) return;
        // Set StartedAt to 25h ago and Status to Completed via reflection
        var startedAt = job.GetType().GetProperty("StartedAt")!;
        startedAt.SetValue(job, DateTimeOffset.UtcNow.AddHours(-25));
        var status = job.GetType().GetProperty("Status")!;
        status.SetValue(job, BacktestJobStatus.Completed);
    }

    private static void MarkJobCompleted(string jobId, BacktestJobManager manager)
    {
        var job = GetJob(jobId, manager);
        if (job == null) return;
        var status = job.GetType().GetProperty("Status")!;
        status.SetValue(job, BacktestJobStatus.Completed);
        // Leave StartedAt = UtcNow (default — recent)
    }

    private static object? GetJob(string jobId, BacktestJobManager manager)
    {
        var field = typeof(BacktestJobManager)
            .GetField("_jobs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        // The field is ConcurrentDictionary<string, BacktestJob> — BacktestJob is internal so we
        // use the non-generic IDictionary interface to avoid a hard reference to the internal type.
        var dict = (System.Collections.IDictionary)field.GetValue(manager)!;
        return dict.Contains(jobId) ? dict[jobId] : null;
    }

    private static IServiceScopeFactory BuildMinimalScopeFactory()
    {
        // Returns a scope factory whose services always throw — tests only exercise
        // the eviction logic (CleanupOldJobs), not RunJobAsync internals.
        var scope   = new Mock<IServiceScope>();
        var sp      = new Mock<IServiceProvider>();
        sp.Setup(s => s.GetService(It.IsAny<Type>())).Returns((object?)null);
        scope.Setup(s => s.ServiceProvider).Returns(sp.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }
}
