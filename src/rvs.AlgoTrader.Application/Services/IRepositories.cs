using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.ValueObjects;
using NodaTime;

namespace rvs.AlgoTrader.Application.Services;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Order?> GetByIdempotencyKeyAsync(string key, CancellationToken ct);
    Task<IReadOnlyList<Order>> GetByStrategyRunAsync(Guid strategyRunId, CancellationToken ct);
    Task<IReadOnlyList<Order>> GetRecentAsync(int count, CancellationToken ct);
    Task AddAsync(Order order, CancellationToken ct);
    Task UpdateAsync(Order order, CancellationToken ct);
}

public interface IPositionRepository
{
    Task<Position?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Position>> GetOpenAsync(CancellationToken ct);
    Task<IReadOnlyList<Position>> GetOpenPositionsAsync(string brokerName, CancellationToken ct);
    Task<IReadOnlyList<Position>> GetBySymbolAsync(string symbol, CancellationToken ct);
    Task<Position?> GetOpenPositionForSymbolAsync(string brokerName, string symbol, CancellationToken ct);
    Task AddAsync(Position position, CancellationToken ct);
    Task UpdateAsync(Position position, CancellationToken ct);
}

public interface IInstrumentRepository
{
    Task<Instrument?> GetBySymbolAsync(string internalSymbol, CancellationToken ct);
    Task<Instrument?> GetByInternalSymbolAsync(string symbol, CancellationToken ct);
    Task<IReadOnlyList<Instrument>> SearchAsync(string query, int limit, CancellationToken ct);
    Task<IReadOnlyList<Instrument>> GetAllActiveAsync(CancellationToken ct);
    Task UpsertAsync(Instrument instrument, CancellationToken ct);
    Task UpsertAsync(IEnumerable<Instrument> instruments, CancellationToken ct);
}

public interface IStrategyInstanceRepository
{
    Task<StrategyInstance?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<StrategyInstance>> GetAllAsync(CancellationToken ct);
    Task<IReadOnlyList<StrategyInstance>> GetAllActiveAsync(CancellationToken ct);
    Task<IReadOnlyList<StrategyInstance>> GetRunningAsync(CancellationToken ct);
    Task AddAsync(StrategyInstance instance, CancellationToken ct);
    Task UpdateAsync(StrategyInstance instance, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public interface IStrategyRunRepository
{
    Task<Domain.Entities.StrategyRun?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Domain.Entities.StrategyRun>> GetByInstanceAsync(Guid instanceId, CancellationToken ct);
    Task AddAsync(Domain.Entities.StrategyRun run, CancellationToken ct);
    Task UpdateAsync(Domain.Entities.StrategyRun run, CancellationToken ct);
}

public interface ICandleRepository
{
    Task<IReadOnlyList<ClosedCandle>> GetAsync(string symbol, string timeframe, Instant from, Instant to, CancellationToken ct);
    Task<IReadOnlyList<ClosedCandle>> GetLastNAsync(string symbol, string timeframe, int count, CancellationToken ct);
    Task BulkInsertAsync(IEnumerable<ClosedCandle> candles, CancellationToken ct);
}

public interface IWatchlistRepository
{
    Task<Watchlist?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Watchlist>> GetByUserAsync(string userId, CancellationToken ct);
    Task AddAsync(Watchlist watchlist, CancellationToken ct);
    Task UpdateAsync(Watchlist watchlist, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public interface IAuditLogRepository
{
    Task AppendAsync(string action, string actor, string entityType, string entityId, object? details, string correlationId, Instant occurredAt, CancellationToken ct);
    Task<IReadOnlyList<AuditLogEntry>> GetPagedAsync(int page, int pageSize, string? entityType, string? actor, CancellationToken ct);
}

public record AuditLogEntry(long Id, string Action, string Actor, string EntityType, string EntityId, object? Details, string CorrelationId, DateTimeOffset OccurredAt);

public interface IAlertLogRepository
{
    Task AddAsync(AlertLogEntry entry, CancellationToken ct);
    Task<IReadOnlyList<AlertLogEntry>> GetPagedAsync(int page, int pageSize, CancellationToken ct);
}

public record AlertLogEntry(Guid Id, string AlertType, string Severity, string Message, DateTimeOffset OccurredAt);

public interface IDownloadJobRepository
{
    Task<DownloadJob?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<DownloadJob>> GetInProgressAsync(CancellationToken ct);
    Task AddAsync(DownloadJob job, CancellationToken ct);
    Task UpdateAsync(DownloadJob job, CancellationToken ct);
}

public record DownloadJob(Guid Id, string Symbol, string Timeframe, DateOnly FromDate, DateOnly ToDate, string BrokerName, string Status, int CompletedChunks, int TotalChunks, Instant CreatedAt);

public interface ISignalJournalRepository
{
    Task AppendAsync(SignalJournalEntry entry, CancellationToken ct);
    Task<IReadOnlyList<SignalJournalEntry>> GetFilteredAsync(Guid? strategyId, string? symbol, string? signal, string? skippedReason, int page, int pageSize, CancellationToken ct);
    Task<(IReadOnlyList<DTOs.Strategy.SignalJournalEntryDto> Items, int Total)> GetPagedAsync(Guid? strategyId, string? symbol, string? signal, string? skippedReason, int page, int pageSize, CancellationToken ct);
}

public record SignalJournalEntry(long Id, Guid StrategyInstanceId, string InternalSymbol, Instant EvaluatedAt, string Timeframe, string Signal, decimal? EntryPrice, decimal? StopLoss, decimal? TakeProfit, string? Reason, object? DiagnosticsJson, bool ActedOn, string? SkippedReason);

public interface ICapitalAllocationRepository
{
    Task<IReadOnlyList<Domain.Entities.CapitalAllocation>> GetAllAsync(CancellationToken ct);
    Task<Domain.Entities.CapitalAllocation?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Domain.Entities.CapitalAllocation?> GetByInstanceAsync(Guid instanceId, CancellationToken ct);
    Task AddAsync(Domain.Entities.CapitalAllocation alloc, CancellationToken ct);
    Task UpdateAsync(Domain.Entities.CapitalAllocation alloc, CancellationToken ct);
}

public interface IUserPreferencesRepository
{
    Task<DTOs.Common.UserPreferencesDto?> GetByUserAsync(string userId, CancellationToken ct);
    Task UpsertAsync(string userId, DTOs.Common.UserPreferencesDto prefs, CancellationToken ct);
}

public interface IAppConfigRepository
{
    Task<string?> GetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, string value, NodaTime.Instant updatedAt, CancellationToken ct);
}

public interface IBrokerLatencyRepository
{
    Task<IReadOnlyList<Brokers.Abstractions.LatencyReport>> GetLatestAsync(string? brokerName, CancellationToken ct);
}

public interface IBacktestRunRepository
{
    Task<DTOs.Backtest.BacktestResultDto?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<DTOs.Backtest.BacktestResultDto>> GetAllAsync(string? strategyName, CancellationToken ct);
    Task<(IReadOnlyList<DTOs.Backtest.BacktestResultDto> Items, int Total)> GetPagedAsync(Guid? strategyInstanceId, int page, int pageSize, CancellationToken ct);
    Task<byte[]?> GetReportAsync(Guid runId, CancellationToken ct);
}

public interface IBacktestCostProfileRepository
{
    Task<IReadOnlyList<DTOs.Backtest.BacktestCostProfileDto>> GetAllAsync(CancellationToken ct);
    Task<DTOs.Backtest.BacktestCostProfileDto?> GetByIdAsync(Guid id, CancellationToken ct);
}

public interface IForwardTestSessionRepository
{
    Task<Domain.Entities.ForwardTestSession?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Domain.Entities.ForwardTestSession>> GetByInstanceAsync(Guid instanceId, CancellationToken ct);
    Task AddAsync(Domain.Entities.ForwardTestSession session, CancellationToken ct);
    Task UpdateAsync(Domain.Entities.ForwardTestSession session, CancellationToken ct);
}

public interface IForwardTestTradeRepository
{
    Task AddAsync(Domain.Entities.ForwardTestTrade trade, CancellationToken ct);
    Task<IReadOnlyList<Domain.Entities.ForwardTestTrade>> GetBySessionAsync(Guid sessionId, CancellationToken ct);
}
