using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Constants;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Runs backtests asynchronously in the background.
/// POST /backtest/start → EnqueueAsync → returns jobId immediately.
/// Progress is pushed via IBacktestProgressPusher (SignalR in API layer) and
/// polled via GET /backtest/{jobId}/status.
/// All state is in-memory — jobs are cleared on server restart (by design).
/// Singleton: creates a DI scope per job run to resolve scoped services safely.
/// </summary>
public class BacktestJobManager(
    IServiceScopeFactory scopeFactory,
    IBacktestProgressPusher pusher,
    ILogger<BacktestJobManager> logger) : IBacktestJobManager
{
    private readonly ConcurrentDictionary<string, BacktestJob> _jobs = new();

    public Task<string> EnqueueAsync(BacktestRequestDto dto, CancellationToken ct)
    {
        // BJ-1: Evict completed/failed/cancelled jobs older than 24h to prevent unbounded memory growth.
        // Called on each enqueue rather than a background timer — low-frequency, no thread needed.
        CleanupOldJobs();

        var jobId = Guid.NewGuid().ToString("N")[..TradingDefaults.JobIdLength];
        var job   = new BacktestJob(jobId) { ScenarioId = dto.ScenarioId };
        _jobs[jobId] = job;

        logger.LogInformation("[BacktestJob] Enqueued job {JobId} — {Strategy}/{Symbol}/{Tf}",
            jobId, dto.StrategyName, dto.InternalSymbol, dto.Timeframe);

        // Fire and forget — background task
        _ = Task.Run(async () => await RunJobAsync(jobId, dto, job.Cts.Token), CancellationToken.None);

        return Task.FromResult(jobId);
    }

    public BacktestJobStatusDto? GetStatus(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job)) return null;
        return job.ToDto();
    }

    public void CancelJob(string jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Cts.Cancel();
            job.Status = BacktestJobStatus.Cancelled;
        }
    }

    public IReadOnlyList<string> GetActiveJobIds()
        => _jobs.Values
            .Where(j => j.Status is BacktestJobStatus.Queued or BacktestJobStatus.Running)
            .Select(j => j.JobId)
            .ToList();

    public Task<string> EnqueueScenarioAsync(Guid scenarioId, BacktestRequestDto mergedRequest, CancellationToken ct)
    {
        CleanupOldJobs();

        var jobId = Guid.NewGuid().ToString("N")[..TradingDefaults.JobIdLength];
        var job   = new BacktestJob(jobId) { ScenarioId = scenarioId };
        _jobs[jobId] = job;

        logger.LogInformation("[BacktestJob] Enqueued scenario job {JobId} — scenario={ScenarioId} {Strategy}/{Symbol}",
            jobId, scenarioId, mergedRequest.StrategyName, mergedRequest.InternalSymbol);

        _ = Task.Run(async () => await RunJobAsync(jobId, mergedRequest, job.Cts.Token), CancellationToken.None);
        return Task.FromResult(jobId);
    }

    private async Task RunJobAsync(string jobId, BacktestRequestDto dto, CancellationToken ct)
    {
        var job = _jobs[jobId];
        // Create a DI scope so scoped services (IBacktestEngine, IHistoricalDownloadService, etc.) resolve correctly
        using var scope = scopeFactory.CreateScope();
        var engine    = scope.ServiceProvider.GetRequiredService<IBacktestEngine>();
        var downloader = scope.ServiceProvider.GetRequiredService<IHistoricalDownloadService>();
        var runRepo   = scope.ServiceProvider.GetRequiredService<IBacktestRunRepository>();
        try
        {
            // BJ-2: Stamp actual execution start (not enqueue time) so StartedAt reflects when
            // the job began running, not when it was queued.
            job.StartedAt = DateTimeOffset.UtcNow;
            job.Status = BacktestJobStatus.Running;
            await pusher.PushProgressAsync(jobId, job.ToDto());

            // Build the request
            var request = BuildRequest(dto, jobId);

            // Progress callback — updates in-memory state and pushes SignalR
            var progress = new Progress<BacktestProgress>(p =>
            {
                job.ProgressPct    = p.ProgressPct;
                job.CurrentBar     = p.CurrentBar;
                job.TotalBars      = p.TotalBars;
                job.TradesSoFar    = p.TradesSoFar;
                job.CurrentEquity  = p.CurrentEquity;
                // Push async but don't await in callback (fire-and-forget)
                _ = pusher.PushProgressAsync(jobId, job.ToDto());
                // Push chart batch separately to keep status DTO lean
                if (p.ChartBatch?.Count > 0)
                {
                    var dtos = p.ChartBatch.Select(MapToChartBarDto).ToList();
                    _ = pusher.PushChartUpdateAsync(jobId, dtos);
                }
            });

            // First attempt
            var result = await engine.RunAsync(request, ct, progress);

            // Auto-download on insufficient data, then retry
            if (!result.Success && result.Error != null && result.Error.StartsWith("Insufficient candle data"))
            {
                logger.LogInformation("[BacktestJob] {JobId} — insufficient data, auto-downloading...", jobId);
                job.Status = BacktestJobStatus.Downloading;
                await pusher.PushProgressAsync(jobId, job.ToDto());

                var from       = new DateOnly(dto.FromDate.Year, dto.FromDate.Month, dto.FromDate.Day);
                var to         = new DateOnly(dto.ToDate.Year,   dto.ToDate.Month,   dto.ToDate.Day);
                // BJ-3: Use the actual requested timeframe, not a hardcoded "1m" fallback.
                // Hardcoding "1m" would download minute data for a 1H or daily backtest — wasteful.
                var downloadTf = dto.Timeframe;
                var dl         = await downloader.DownloadAsync(dto.InternalSymbol, dto.BrokerName, downloadTf, from, to, ct);

                if (!dl.Success || dl.BarCount == 0)
                {
                    job.Status = BacktestJobStatus.Failed;
                    job.Error  = $"No data for {dto.InternalSymbol}/{dto.Timeframe}. {dl.Error ?? "0 bars returned."}";
                    await pusher.PushCompletedAsync(jobId, job.ToDto());
                    return;
                }

                job.Status = BacktestJobStatus.Running;
                result = await engine.RunAsync(request, ct, progress);
            }

            // Map result → DTO
            var resultDto = MapToDto(result);

            // Persist if successful
            if (result.Success)
            {
                try
                {
                    if (job.ScenarioId.HasValue)
                        resultDto = resultDto with { ScenarioId = job.ScenarioId.Value };
                    var savedId = await runRepo.SaveAsync(resultDto, CancellationToken.None);
                    resultDto = resultDto with { Id = savedId.ToString() };

                    // Update scenario status → Backtested when this was a scenario-tagged run
                    if (job.ScenarioId.HasValue)
                    {
                        var scenarioRepo = scope.ServiceProvider.GetRequiredService<IStrategyScenarioRepository>();
                        var scenario = await scenarioRepo.GetByIdAsync(job.ScenarioId.Value, CancellationToken.None);
                        if (scenario != null)
                        {
                            scenario.Status            = ScenarioStatus.Backtested;
                            scenario.LastBacktestRunId = savedId;
                            scenario.UpdatedAt         = SystemClock.Instance.GetCurrentInstant();
                            await scenarioRepo.UpdateAsync(scenario, CancellationToken.None);
                            logger.LogInformation("[BacktestJob] {JobId} — scenario {ScenarioId} promoted to Backtested",
                                jobId, job.ScenarioId.Value);
                        }
                    }
                }
                catch (Exception ex) { logger.LogWarning(ex, "[BacktestJob] {JobId} — failed to persist result", jobId); }
            }

            job.Status      = result.Success ? BacktestJobStatus.Completed : BacktestJobStatus.Failed;
            job.Error       = result.Error;
            job.ProgressPct = 100m;
            job.Result      = resultDto;
            await pusher.PushCompletedAsync(jobId, job.ToDto());
        }
        catch (OperationCanceledException)
        {
            job.Status = BacktestJobStatus.Cancelled;
            await pusher.PushCompletedAsync(jobId, job.ToDto());
            logger.LogInformation("[BacktestJob] {JobId} cancelled", jobId);
        }
        catch (Exception ex)
        {
            job.Status = BacktestJobStatus.Failed;
            job.Error  = ex.Message;
            await pusher.PushCompletedAsync(jobId, job.ToDto());
            logger.LogError(ex, "[BacktestJob] {JobId} failed", jobId);
        }
    }

    // BJ-1: Remove completed/failed/cancelled jobs older than 24h from the in-memory dictionary.
    private void CleanupOldJobs()
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
        foreach (var (id, job) in _jobs)
        {
            if (job.Status is BacktestJobStatus.Completed or BacktestJobStatus.Failed or BacktestJobStatus.Cancelled
                && job.StartedAt < cutoff)
            {
                _jobs.TryRemove(id, out _);
            }
        }
    }

    private static BacktestRequest BuildRequest(BacktestRequestDto dto, string jobId) => new(
        StrategyName:         dto.StrategyName,
        ParametersJson:       dto.ParametersJson,
        InternalSymbol:       dto.InternalSymbol,
        Timeframe:            dto.Timeframe,
        FromDate:             dto.FromDate,
        ToDate:               dto.ToDate,
        InitialCapital:       dto.InitialCapital,
        RiskPerTradePercent:  dto.RiskPerTradePercent,
        FillModel:            (FillModel)dto.FillModel,
        SlippageBasisPoints:  dto.SlippageBasisPoints,
        BrokerageFlatPerSide: dto.BrokerageFlatPerSide,
        JobId:                jobId,
        TrailActivationR:     dto.TrailActivationR,
        TrailOffsetR:         dto.TrailOffsetR,
        BreakEvenAt1R:        dto.BreakEvenAt1R,
        CircuitBreakerPct:    dto.CircuitBreakerPct);

    private static BacktestResultDto MapToDto(BacktestResult r) => new(
        Id: null,
        Success: r.Success,
        StrategyName: r.StrategyName,
        Symbol: r.Symbol,
        Timeframe: r.Timeframe,
        FromDate: r.FromDate,
        ToDate: r.ToDate,
        InitialCapital: r.InitialCapital,
        FinalEquity: r.FinalEquity,
        TotalPnl: r.TotalPnl,
        TotalReturn: r.TotalReturn,
        MaxDrawdown: r.MaxDrawdown,
        SharpeRatio: r.SharpeRatio,
        CalmarRatio: r.CalmarRatio,
        ProfitFactor: r.ProfitFactor,
        WinRate: r.WinRate,
        TotalTrades: r.TotalTrades,
        WinCount: r.WinCount,
        LossCount: r.LossCount,
        AvgWin: r.AvgWin,
        AvgLoss: r.AvgLoss,
        MaxConsecutiveLosses: r.MaxConsecutiveLosses,
        ExpectancyPerTrade: r.ExpectancyPerTrade,
        SortinoRatio: r.SortinoRatio,
        DailySharpe: r.DailySharpe,
        MonthlySharpe: r.MonthlySharpe,
        MonthlyWinRate: r.MonthlyWinRate,
        DrawdownRecoveryBars: r.DrawdownRecoveryBars,
        MaxLots: r.MaxLots,
        DataHash: r.DataHash,
        Error: r.Error,
        StartedAt: DateTimeOffset.UtcNow,
        Trades: r.Trades.Select(t => new BacktestTradeDto(
            Direction: t.Direction,
            EntryPrice: t.EntryPrice,
            ExitPrice: t.ExitPrice,
            Quantity: t.Quantity,
            ExitReason: t.ExitReason,
            GrossPnl: t.GrossPnl,
            NetPnl: t.NetPnl,
            EntryTime: t.EntryTime.ToInstant().ToDateTimeOffset().ToString("o"),
            ExitTime: t.ExitTime.ToInstant().ToDateTimeOffset().ToString("o"),
            Mae: t.Direction == "BUY"
                ? Math.Max(0m, t.EntryPrice - t.WorstPrice)
                : Math.Max(0m, t.WorstPrice - t.EntryPrice),
            Mfe: t.Direction == "BUY"
                ? Math.Max(0m, t.BestPrice - t.EntryPrice)
                : Math.Max(0m, t.EntryPrice - t.BestPrice)
        )).ToList(),
        MonthlyBreakdown: r.MonthlyBreakdown.Select(m => new BacktestMonthlyBreakdownDto(m.Year, m.Month, m.Pnl, m.Trades, m.WinRate)).ToList(),
        YearlyBreakdown: r.YearlyBreakdown.Select(y => new BacktestYearlyBreakdownDto(y.Year, y.Pnl, y.Return, y.Trades, y.WinRate)).ToList(),
        ChartSample: r.ChartSample?.Select(MapToChartBarDto).ToList(),
        CircuitBreakerHit: r.CircuitBreakerHit,
        CircuitBreakerReason: r.CircuitBreakerReason,
        VaR95: r.VaR95,
        CVaR95: r.CVaR95,
        OmegaRatio: r.OmegaRatio,
        Skewness: r.Skewness,
        Kurtosis: r.Kurtosis,
        DeploymentRating: r.DeploymentRating,
        DeploymentRationale: r.DeploymentRationale,
        SkippedSignalCount: r.SkippedSignalCount);

    private static BacktestChartBarDto MapToChartBarDto(BacktestChartBar b) => new(
        TimeMs: b.TimeMs,
        Open: b.Open, High: b.High, Low: b.Low, Close: b.Close,
        Volume: b.Volume,
        Signal: b.Signal,
        SignalPrice: b.SignalPrice,
        StopLoss: b.StopLoss,
        TakeProfit: b.TakeProfit,
        Indicators: b.Indicators);
}

/// <summary>In-memory job state — not persisted.</summary>
internal sealed class BacktestJob(string jobId)
{
    public string  JobId          { get; } = jobId;
    /// <summary>Set at actual execution start (RunJobAsync) — not enqueue time — so it reflects when work began.</summary>
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Set when this job was triggered by EnqueueScenarioAsync or when ScenarioId is provided in BacktestRequestDto.</summary>
    public Guid?   ScenarioId     { get; init; }
    public BacktestJobStatus Status { get; set; } = BacktestJobStatus.Queued;
    public decimal ProgressPct   { get; set; } = 0m;
    public int CurrentBar        { get; set; } = 0;
    public int TotalBars         { get; set; } = 0;
    public int TradesSoFar       { get; set; } = 0;
    public decimal CurrentEquity { get; set; } = 0m;
    public string? Error         { get; set; }
    public BacktestResultDto? Result { get; set; }
    public CancellationTokenSource Cts { get; } = new();

    public BacktestJobStatusDto ToDto() => new(
        JobId: JobId,
        Status: Status,
        ProgressPct: ProgressPct,
        CurrentBar: CurrentBar,
        TotalBars: TotalBars,
        TradesSoFar: TradesSoFar,
        CurrentEquity: CurrentEquity,
        Error: Error,
        Result: Status is BacktestJobStatus.Completed or BacktestJobStatus.Failed ? Result : null,
        StartedAt: StartedAt);
}
