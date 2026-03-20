using NodaTime;
using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.Strategy;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Entities;

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

public class BacktestRunRepository : IBacktestRunRepository
{
    public Task<BacktestResultDto?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult<BacktestResultDto?>(null);

    public Task<IReadOnlyList<BacktestResultDto>> GetAllAsync(string? strategyName, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<BacktestResultDto>>(Array.Empty<BacktestResultDto>());

    public Task<(IReadOnlyList<BacktestResultDto> Items, int Total)> GetPagedAsync(
        Guid? strategyInstanceId, int page, int pageSize, CancellationToken ct)
        => Task.FromResult<(IReadOnlyList<BacktestResultDto>, int)>(
            (Array.Empty<BacktestResultDto>(), 0));

    public Task<byte[]?> GetReportAsync(Guid runId, CancellationToken ct)
        => Task.FromResult<byte[]?>(null);
}

public class BacktestCostProfileRepository : IBacktestCostProfileRepository
{
    public Task<IReadOnlyList<BacktestCostProfileDto>> GetAllAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<BacktestCostProfileDto>>(Array.Empty<BacktestCostProfileDto>());

    public Task<BacktestCostProfileDto?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult<BacktestCostProfileDto?>(null);
}

public class ForwardTestSessionRepository : IForwardTestSessionRepository
{
    public Task<ForwardTestSession?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult<ForwardTestSession?>(null);

    public Task<IReadOnlyList<ForwardTestSession>> GetByInstanceAsync(Guid instanceId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ForwardTestSession>>(Array.Empty<ForwardTestSession>());

    public Task AddAsync(ForwardTestSession session, CancellationToken ct) => Task.CompletedTask;

    public Task UpdateAsync(ForwardTestSession session, CancellationToken ct) => Task.CompletedTask;
}

public class ForwardTestTradeRepository : IForwardTestTradeRepository
{
    public Task AddAsync(ForwardTestTrade trade, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<ForwardTestTrade>> GetBySessionAsync(Guid sessionId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ForwardTestTrade>>(Array.Empty<ForwardTestTrade>());
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
