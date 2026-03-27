using Microsoft.EntityFrameworkCore;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.Strategy;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Infrastructure.Persistence;

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

public class BacktestRunRepository(AlgoTraderDbContext db) : IBacktestRunRepository
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
        Guid? strategyInstanceId, int page, int pageSize, CancellationToken ct)
    {
        // BacktestRun doesn't track StrategyInstanceId — return all, paged
        var total = await db.BacktestRuns.CountAsync(ct);
        var items = await db.BacktestRuns
            .OrderByDescending(r => r.RanAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);
        return (items.Select(ToDto).ToList(), total);
    }

    public Task<byte[]?> GetReportAsync(Guid runId, CancellationToken ct)
        => Task.FromResult<byte[]?>(null); // PDF reports not yet implemented

    public async Task SaveAsync(BacktestResultDto result, CancellationToken ct)
    {
        // Idempotent on DataHash — skip if same run already stored
        if (result.DataHash != null &&
            await db.BacktestRuns.AnyAsync(r => r.DataHash == result.DataHash, ct))
            return;

        var tradesJson = result.Trades != null
            ? System.Text.Json.JsonSerializer.Serialize(result.Trades)
            : "[]";

        var run = new BacktestRun
        {
            Id = Guid.NewGuid(),
            StrategyName = result.StrategyName,
            InternalSymbol = result.Symbol,
            Timeframe = result.Timeframe,
            FromDate = result.FromDate,
            ToDate = result.ToDate,
            InitialCapital = result.InitialCapital,
            FinalEquity = result.FinalEquity,
            TotalPnl = result.TotalPnl,
            TotalReturn = result.TotalReturn,
            MaxDrawdown = result.MaxDrawdown,
            SharpeRatio = result.SharpeRatio,
            CalmarRatio = result.CalmarRatio,
            ProfitFactor = result.ProfitFactor,
            WinRate = result.WinRate,
            TotalTrades = result.TotalTrades,
            WinCount = result.WinCount,
            LossCount = result.LossCount,
            AvgWin = result.AvgWin,
            AvgLoss = result.AvgLoss,
            MaxConsecutiveLosses = result.MaxConsecutiveLosses,
            ExpectancyPerTrade = result.ExpectancyPerTrade,
            DataHash = result.DataHash,
            TradesJson = tradesJson,
            RanAt = NodaTime.SystemClock.Instance.GetCurrentInstant()
        };

        await db.BacktestRuns.AddAsync(run, ct);
        await db.SaveChangesAsync(ct);
    }

    private static BacktestResultDto ToDto(BacktestRun r)
    {
        IReadOnlyList<BacktestTradeDto>? trades = null;
        if (!string.IsNullOrEmpty(r.TradesJson) && r.TradesJson != "[]")
        {
            try { trades = System.Text.Json.JsonSerializer.Deserialize<List<BacktestTradeDto>>(r.TradesJson); }
            catch { /* ignore deserialisation errors */ }
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
            DataHash: r.DataHash,
            Error: null,
            StartedAt: r.RanAt.ToDateTimeOffset(),
            Trades: trades);
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
