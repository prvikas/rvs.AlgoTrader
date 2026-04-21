using Microsoft.EntityFrameworkCore;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.Strategy;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Infrastructure.Persistence;
using rvs.AlgoTrader.Infrastructure.Services;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

// ─────────────────────────────────────────────────────────────────────────────
// Stub implementations for repositories that don't yet have full EF Core
// implementations. These allow the application to start without throwing
// InvalidOperationException on DI resolution. Replace with real
// implementations backed by AlgoTraderDbContext as needed.
// ─────────────────────────────────────────────────────────────────────────────

public class AuditLogRepository : IAuditLogRepository
{
    public Task AppendAsync(string action, string actor, string entityType, string entityId,
        object? details, string correlationId, Instant occurredAt, CancellationToken ct)
        => Task.CompletedTask;

    public Task<IReadOnlyList<AuditLogEntry>> GetPagedAsync(int page, int pageSize,
        string? entityType, string? actor, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<AuditLogEntry>>(Array.Empty<AuditLogEntry>());
}

public class AlertLogRepository : IAlertLogRepository
{
    public Task AddAsync(AlertLogEntry entry, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<AlertLogEntry>> GetPagedAsync(int page, int pageSize, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<AlertLogEntry>>(Array.Empty<AlertLogEntry>());
}

public class DownloadJobRepository : IDownloadJobRepository
{
    public Task<DownloadJob?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult<DownloadJob?>(null);

    public Task<IReadOnlyList<DownloadJob>> GetInProgressAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<DownloadJob>>(Array.Empty<DownloadJob>());

    public Task AddAsync(DownloadJob job, CancellationToken ct) => Task.CompletedTask;

    public Task UpdateAsync(DownloadJob job, CancellationToken ct) => Task.CompletedTask;
}

public class SignalJournalRepository : ISignalJournalRepository
{
    public Task AppendAsync(SignalJournalEntry entry, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<SignalJournalEntry>> GetFilteredAsync(
        Guid? strategyId, string? symbol, string? signal, string? skippedReason,
        int page, int pageSize, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<SignalJournalEntry>>(Array.Empty<SignalJournalEntry>());

    public Task<(IReadOnlyList<SignalJournalEntryDto> Items, int Total)> GetPagedAsync(
        Guid? strategyId, string? symbol, string? signal, string? skippedReason,
        int page, int pageSize, CancellationToken ct)
        => Task.FromResult<(IReadOnlyList<SignalJournalEntryDto>, int)>(
            (Array.Empty<SignalJournalEntryDto>(), 0));
}

public class CapitalAllocationRepository : ICapitalAllocationRepository
{
    public Task<IReadOnlyList<CapitalAllocation>> GetAllAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CapitalAllocation>>(Array.Empty<CapitalAllocation>());

    public Task<CapitalAllocation?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult<CapitalAllocation?>(null);

    public Task<CapitalAllocation?> GetByInstanceAsync(Guid instanceId, CancellationToken ct)
        => Task.FromResult<CapitalAllocation?>(null);

    public Task AddAsync(CapitalAllocation alloc, CancellationToken ct) => Task.CompletedTask;

    public Task UpdateAsync(CapitalAllocation alloc, CancellationToken ct) => Task.CompletedTask;
    public Task DeleteByInstanceAsync(Guid instanceId, CancellationToken ct) => Task.CompletedTask;
}

public class UserPreferencesRepository : IUserPreferencesRepository
{
    public Task<UserPreferencesDto?> GetByUserAsync(string userId, CancellationToken ct)
        => Task.FromResult<UserPreferencesDto?>(null);

    public Task UpsertAsync(string userId, UserPreferencesDto prefs, CancellationToken ct)
        => Task.CompletedTask;
}

public class AppConfigRepository : IAppConfigRepository
{
    private readonly Dictionary<string, string> _store = new();

    public Task<string?> GetAsync(string key, CancellationToken ct)
        => Task.FromResult(_store.TryGetValue(key, out var v) ? v : (string?)null);

    public Task SetAsync(string key, string value, Instant updatedAt, CancellationToken ct)
    {
        _store[key] = value;
        return Task.CompletedTask;
    }
}

public class BrokerLatencyRepository : IBrokerLatencyRepository
{
    public Task<IReadOnlyList<LatencyReport>> GetLatestAsync(string? brokerName, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<LatencyReport>>(Array.Empty<LatencyReport>());
}

public class BacktestRunRepository(AlgoTraderDbContext db, rvs.AlgoTrader.Domain.Interfaces.IClock clock) : IBacktestRunRepository
{
    public async Task<BacktestResultDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var run = await db.BacktestRuns.FirstOrDefaultAsync(r => r.Id == id, ct);
        return run == null ? null : ToDto(run);
    }

    public async Task<IReadOnlyList<BacktestResultDto>> GetAllAsync(string? strategyName, CancellationToken ct)
    {
        var q = db.BacktestRuns.AsQueryable();
        if (!string.IsNullOrEmpty(strategyName))
            q = q.Where(r => r.StrategyName == strategyName);
        var runs = await q.OrderByDescending(r => r.RanAt).ToListAsync(ct);
        return runs.Select(ToDto).ToList();
    }

    public async Task<(IReadOnlyList<BacktestResultDto> Items, int Total)> GetPagedAsync(
        Guid? strategyInstanceId, int page, int pageSize, CancellationToken ct, string? strategyName = null)
    {
        var q = db.BacktestRuns.AsQueryable();
        if (!string.IsNullOrEmpty(strategyName))
            q = q.Where(r => r.StrategyName == strategyName);
        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(r => r.RanAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);
        return (items.Select(ToDto).ToList(), total);
    }

    public async Task<byte[]?> GetReportAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.BacktestRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run == null) return null;
        var dto = ToDto(run);
        return BacktestReportGenerator.Generate(dto);
    }

    public async Task<IReadOnlyList<BacktestResultDto>> GetByScenarioAsync(Guid scenarioId, int page, int pageSize, CancellationToken ct)
    {
        var runs = await db.BacktestRuns
            .Where(r => r.ScenarioId == scenarioId)
            .OrderByDescending(r => r.RanAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);
        return runs.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<BacktestResultDto>> GetByDefinitionAsync(
        Guid definitionId, int page, int pageSize, CancellationToken ct)
    {
        var offset = (page - 1) * pageSize;
        // Non-interpolated raw string: {0},{1},{2} are EF positional placeholders (→ $1,$2,$3 in Npgsql).
        // strategy_definition_scenarios is managed via raw SQL so it is not in the DbContext.
        const string sql = """
            SELECT br.* FROM backtest_runs br
            INNER JOIN strategy_definition_scenarios sds ON br.scenario_id = sds.id
            WHERE sds.strategy_definition_id = {0}
            ORDER BY br.ran_at DESC
            LIMIT {1} OFFSET {2}
            """;
        var items = await db.BacktestRuns
            .FromSqlRaw(sql, definitionId, pageSize, offset)
            .AsNoTracking()
            .ToListAsync(ct);
        return items.Select(ToDto).ToList();
    }

    public async Task<Guid> SaveAsync(BacktestResultDto result, CancellationToken ct)
    {
        var tradesJson = result.Trades != null
            ? System.Text.Json.JsonSerializer.Serialize(result.Trades)
            : "[]";

        var extJson = System.Text.Json.JsonSerializer.Serialize(new {
            result.SortinoRatio, result.DailySharpe, result.MonthlySharpe,
            result.MonthlyWinRate, result.DrawdownRecoveryBars, result.MaxLots,
            MonthlyBreakdown = result.MonthlyBreakdown ?? [],
            YearlyBreakdown  = result.YearlyBreakdown  ?? [],
            result.VaR95, result.CVaR95, result.OmegaRatio,
            result.Skewness, result.Kurtosis,
            result.DeploymentRating, result.DeploymentRationale,
            result.CircuitBreakerHit, result.CircuitBreakerReason,
            // Store chart sample here so it is available when loading a saved run.
            // ChartSample is downsampled to ≤ 2000 bars — safe to persist as JSON.
            ChartSample = result.ChartSample ?? []
        });

        // ── Upsert by ScenarioId (re-run overwrites previous result) ─────────
        // If the run was triggered from the UI scenarios panel, overwrite the
        // existing row so the user always sees the latest result for each scenario.
        if (result.ScenarioId.HasValue)
        {
            var existing = await db.BacktestRuns
                .FirstOrDefaultAsync(r => r.ScenarioId == result.ScenarioId.Value, ct);
            if (existing != null)
            {
                // Overwrite metrics and data in-place; keep the original row ID.
                existing.StrategyName         = result.StrategyName;
                existing.InternalSymbol       = result.Symbol;
                existing.Timeframe            = result.Timeframe;
                existing.FromDate             = result.FromDate;
                existing.ToDate               = result.ToDate;
                existing.InitialCapital       = result.InitialCapital;
                existing.FinalEquity          = result.FinalEquity;
                existing.TotalPnl             = result.TotalPnl;
                existing.TotalReturn          = result.TotalReturn;
                existing.MaxDrawdown          = result.MaxDrawdown;
                existing.SharpeRatio          = result.SharpeRatio;
                existing.CalmarRatio          = result.CalmarRatio;
                existing.ProfitFactor         = result.ProfitFactor;
                existing.WinRate              = result.WinRate;
                existing.TotalTrades          = result.TotalTrades;
                existing.WinCount             = result.WinCount;
                existing.LossCount            = result.LossCount;
                existing.AvgWin               = result.AvgWin;
                existing.AvgLoss              = result.AvgLoss;
                existing.MaxConsecutiveLosses  = result.MaxConsecutiveLosses;
                existing.ExpectancyPerTrade    = result.ExpectancyPerTrade;
                existing.DataHash             = result.DataHash;
                existing.TradesJson           = tradesJson;
                existing.ExtendedStatsJson    = extJson;
                existing.RanAt               = clock.NowInstant();
                await db.SaveChangesAsync(ct);
                return existing.Id;
            }
        }
        else if (result.DataHash != null)
        {
            // No ScenarioId — fall back to DataHash idempotency for ad-hoc runs.
            var existing = await db.BacktestRuns
                .FirstOrDefaultAsync(r => r.DataHash == result.DataHash, ct);
            if (existing != null) return existing.Id;
        }

        // ── Insert new row ────────────────────────────────────────────────────
        var run = new BacktestRun
        {
            Id                   = Guid.NewGuid(),
            ScenarioId           = result.ScenarioId,
            StrategyName         = result.StrategyName,
            InternalSymbol       = result.Symbol,
            Timeframe            = result.Timeframe,
            FromDate             = result.FromDate,
            ToDate               = result.ToDate,
            InitialCapital       = result.InitialCapital,
            FinalEquity          = result.FinalEquity,
            TotalPnl             = result.TotalPnl,
            TotalReturn          = result.TotalReturn,
            MaxDrawdown          = result.MaxDrawdown,
            SharpeRatio          = result.SharpeRatio,
            CalmarRatio          = result.CalmarRatio,
            ProfitFactor         = result.ProfitFactor,
            WinRate              = result.WinRate,
            TotalTrades          = result.TotalTrades,
            WinCount             = result.WinCount,
            LossCount            = result.LossCount,
            AvgWin               = result.AvgWin,
            AvgLoss              = result.AvgLoss,
            MaxConsecutiveLosses  = result.MaxConsecutiveLosses,
            ExpectancyPerTrade    = result.ExpectancyPerTrade,
            DataHash             = result.DataHash,
            TradesJson           = tradesJson,
            ExtendedStatsJson    = extJson,
            RanAt                = clock.NowInstant()
        };

        await db.BacktestRuns.AddAsync(run, ct);
        await db.SaveChangesAsync(ct);
        return run.Id;
    }

    private static BacktestResultDto ToDto(BacktestRun r)
    {
        IReadOnlyList<BacktestTradeDto>? trades = null;
        if (!string.IsNullOrEmpty(r.TradesJson) && r.TradesJson != "[]")
        {
            try { trades = System.Text.Json.JsonSerializer.Deserialize<List<BacktestTradeDto>>(r.TradesJson); }
            catch { /* ignore deserialisation errors */ }
        }

        decimal sortinoRatio = 0, dailySharpe = 0, monthlySharpe = 0, monthlyWinRate = 0;
        int ddRecoveryBars = 0, maxLots = 0;
        decimal var95 = 0, cVar95 = 0, omegaRatio = 0, skewness = 0, kurtosis = 0;
        string deploymentRating = "", deploymentRationale = "";
        bool circuitBreakerHit = false;
        string? circuitBreakerReason = null;
        IReadOnlyList<BacktestMonthlyBreakdownDto>? monthlyBreakdown = null;
        IReadOnlyList<BacktestYearlyBreakdownDto>? yearlyBreakdown = null;
        IReadOnlyList<BacktestChartBarDto>? chartSample = null;
        if (!string.IsNullOrEmpty(r.ExtendedStatsJson))
        {
            try
            {
                var ext = System.Text.Json.JsonDocument.Parse(r.ExtendedStatsJson).RootElement;
                if (ext.TryGetProperty("SortinoRatio", out var sr)) sortinoRatio = sr.GetDecimal();
                if (ext.TryGetProperty("DailySharpe", out var ds)) dailySharpe = ds.GetDecimal();
                if (ext.TryGetProperty("MonthlySharpe", out var ms)) monthlySharpe = ms.GetDecimal();
                if (ext.TryGetProperty("MonthlyWinRate", out var mwr)) monthlyWinRate = mwr.GetDecimal();
                if (ext.TryGetProperty("DrawdownRecoveryBars", out var drb)) ddRecoveryBars = drb.GetInt32();
                if (ext.TryGetProperty("MaxLots", out var ml)) maxLots = ml.GetInt32();
                if (ext.TryGetProperty("MonthlyBreakdown", out var mb))
                    monthlyBreakdown = System.Text.Json.JsonSerializer.Deserialize<List<BacktestMonthlyBreakdownDto>>(mb.GetRawText());
                if (ext.TryGetProperty("YearlyBreakdown", out var yb))
                    yearlyBreakdown = System.Text.Json.JsonSerializer.Deserialize<List<BacktestYearlyBreakdownDto>>(yb.GetRawText());
                if (ext.TryGetProperty("VaR95", out var v95)) var95 = v95.GetDecimal();
                if (ext.TryGetProperty("CVaR95", out var cv95)) cVar95 = cv95.GetDecimal();
                if (ext.TryGetProperty("OmegaRatio", out var om)) omegaRatio = om.GetDecimal();
                if (ext.TryGetProperty("Skewness", out var sk)) skewness = sk.GetDecimal();
                if (ext.TryGetProperty("Kurtosis", out var ku)) kurtosis = ku.GetDecimal();
                if (ext.TryGetProperty("DeploymentRating", out var dr)) deploymentRating = dr.GetString() ?? "";
                if (ext.TryGetProperty("DeploymentRationale", out var drat)) deploymentRationale = drat.GetString() ?? "";
                if (ext.TryGetProperty("CircuitBreakerHit", out var cbh)) circuitBreakerHit = cbh.GetBoolean();
                if (ext.TryGetProperty("CircuitBreakerReason", out var cbr)) circuitBreakerReason = cbr.GetString();
                // ChartSample persisted since fix-3 — deserialise for full replay chart
                if (ext.TryGetProperty("ChartSample", out var cs) && cs.ValueKind == System.Text.Json.JsonValueKind.Array)
                    chartSample = System.Text.Json.JsonSerializer.Deserialize<List<BacktestChartBarDto>>(cs.GetRawText());
            }
            catch { /* ignore */ }
        }

        return new BacktestResultDto(
            Id: r.Id.ToString(),
            Success: true,
            StrategyName: r.StrategyName,
            Symbol: r.InternalSymbol,
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
            SortinoRatio: sortinoRatio,
            DailySharpe: dailySharpe,
            MonthlySharpe: monthlySharpe,
            MonthlyWinRate: monthlyWinRate,
            DrawdownRecoveryBars: ddRecoveryBars,
            MaxLots: maxLots,
            DataHash: r.DataHash,
            Error: null,
            StartedAt: r.RanAt.ToDateTimeOffset(),
            Trades: trades,
            MonthlyBreakdown: monthlyBreakdown,
            YearlyBreakdown: yearlyBreakdown,
            ScenarioId: r.ScenarioId,
            VaR95: var95,
            CVaR95: cVar95,
            OmegaRatio: omegaRatio,
            Skewness: skewness,
            Kurtosis: kurtosis,
            DeploymentRating: deploymentRating,
            DeploymentRationale: deploymentRationale,
            CircuitBreakerHit: circuitBreakerHit,
            CircuitBreakerReason: circuitBreakerReason,
            ChartSample: chartSample);
    }
}

public class BacktestCostProfileRepository : IBacktestCostProfileRepository
{
    public Task<IReadOnlyList<BacktestCostProfileDto>> GetAllAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<BacktestCostProfileDto>>(Array.Empty<BacktestCostProfileDto>());

    public Task<BacktestCostProfileDto?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult<BacktestCostProfileDto?>(null);
}

public class ForwardTestSessionRepository(AlgoTraderDbContext db) : IForwardTestSessionRepository
{
    public async Task<ForwardTestSession?> GetByIdAsync(Guid id, CancellationToken ct)
        => await db.ForwardTestSessions.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<ForwardTestSession>> GetByInstanceAsync(Guid instanceId, CancellationToken ct)
        => await db.ForwardTestSessions
            .Where(s => s.StrategyInstanceId == instanceId)
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ForwardTestSession>> GetRunningAsync(CancellationToken ct)
        => await db.ForwardTestSessions
            .Where(s => s.Status == "Running")
            .ToListAsync(ct);

    public async Task AddAsync(ForwardTestSession session, CancellationToken ct)
    {
        await db.ForwardTestSessions.AddAsync(session, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ForwardTestSession session, CancellationToken ct)
    {
        db.ForwardTestSessions.Update(session);
        await db.SaveChangesAsync(ct);
    }
}

public class ForwardTestTradeRepository(AlgoTraderDbContext db) : IForwardTestTradeRepository
{
    public async Task AddAsync(ForwardTestTrade trade, CancellationToken ct)
    {
        await db.ForwardTestTrades.AddAsync(trade, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ForwardTestTrade>> GetBySessionAsync(Guid sessionId, CancellationToken ct)
        => await db.ForwardTestTrades
            .Where(t => t.SessionId == sessionId)
            .OrderBy(t => t.EntryTime)
            .ToListAsync(ct);
}

public class WatchlistRepository : IWatchlistRepository
{
    public Task<Watchlist?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult<Watchlist?>(null);

    public Task<IReadOnlyList<Watchlist>> GetByUserAsync(string userId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Watchlist>>(Array.Empty<Watchlist>());

    public Task AddAsync(Watchlist watchlist, CancellationToken ct) => Task.CompletedTask;

    public Task UpdateAsync(Watchlist watchlist, CancellationToken ct) => Task.CompletedTask;

    public Task DeleteAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
}
