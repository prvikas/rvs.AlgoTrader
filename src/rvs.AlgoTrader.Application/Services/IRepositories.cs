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
    /// <summary>
    /// Returns closed positions belonging to the given strategy runs whose
    /// <c>ClosedAt</c> falls within <paramref name="dateIst"/> (IST calendar day).
    /// Used by MonitoringAlertJob for same-day realized P&amp;L.
    /// </summary>
    Task<IReadOnlyList<Position>> GetClosedTodayAsync(IEnumerable<Guid> strategyRunIds, LocalDate dateIst, CancellationToken ct);
}

public interface IInstrumentRepository
{
    Task<Instrument?> GetBySymbolAsync(string internalSymbol, CancellationToken ct);
    Task<Instrument?> GetByInternalSymbolAsync(string symbol, CancellationToken ct);
    Task<IReadOnlyList<Instrument>> SearchAsync(string query, int limit, CancellationToken ct);
    Task<IReadOnlyList<Instrument>> GetAllActiveAsync(CancellationToken ct);
    Task UpsertAsync(Instrument instrument, CancellationToken ct);
    Task UpsertAsync(IEnumerable<Instrument> instruments, CancellationToken ct);

    /// <summary>
    /// Server-side paged + sorted + filtered query.
    /// Returns the current page of instruments AND the total matching count (for pagination UI).
    /// sortBy: "symbol" | "name" | "exchange" | "type" — default "symbol".
    /// </summary>
    Task<(IReadOnlyList<Instrument> Items, int TotalCount)> FilterPagedAsync(
        string? search, string? exchange, string? instrumentType, bool? active,
        string sortBy, bool sortDesc, int pageSize, int offset, CancellationToken ct);

    /// <summary>
    /// Loads all instruments whose InternalSymbol is in the given set — one DB round trip.
    /// Returns a case-insensitive dictionary keyed by InternalSymbol.
    /// Used by InstrumentRefreshService for O(1) lookup during batch upsert.
    /// </summary>
    Task<Dictionary<string, Instrument>> GetBatchByInternalSymbolAsync(
        IReadOnlyList<string> symbols, CancellationToken ct);

    /// <summary>
    /// Inserts new instruments in one AddRange + SaveChanges.
    /// Updates to existing (already-tracked) instruments are flushed by the same SaveChanges.
    /// Replaces the N+1 UpsertAsync loop used during master-data refresh.
    /// </summary>
    Task BulkUpsertAsync(
        IReadOnlyList<Instrument> toAdd, IReadOnlyList<Instrument> toUpdate, CancellationToken ct);

    /// <summary>
    /// Returns all option instruments (CE + PE legs) for a given underlying symbol and expiry date.
    /// Used by IOptionChainService to resolve broker tokens before batch quote fetch.
    /// Example: underlying="NIFTY 50", expiry=2024-12-26 → all NIFTY option strikes for that expiry.
    /// </summary>
    Task<IReadOnlyList<Instrument>> GetOptionsByUnderlyingAndExpiryAsync(
        string underlyingSymbol, NodaTime.LocalDate expiry, CancellationToken ct);
}

public interface ISpreadPositionRepository
{
    Task<Domain.Entities.SpreadPosition?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Domain.Entities.SpreadPosition>> GetOpenAsync(CancellationToken ct);
    Task AddAsync(Domain.Entities.SpreadPosition spread, CancellationToken ct);
    Task UpdateAsync(Domain.Entities.SpreadPosition spread, CancellationToken ct);
}

public interface IOptionIvHistoryRepository
{
    /// <summary>Returns the last N daily ATM IV records for a symbol, ordered by date descending.</summary>
    Task<IReadOnlyList<Domain.Entities.OptionIvHistory>> GetRecentAsync(
        string underlyingSymbol, int days, CancellationToken ct);

    /// <summary>Upsert a daily ATM IV snapshot (by underlying + date).</summary>
    Task UpsertAsync(Domain.Entities.OptionIvHistory record, CancellationToken ct);
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

    /// <summary>
    /// Returns candles for the requested timeframe.
    /// If no stored candles exist at that timeframe, falls back to 1m candles
    /// and aggregates them up to the target timeframe at runtime.
    /// Always prefer downloading 1m data — never request multiple timeframes separately.
    /// </summary>
    Task<IReadOnlyList<ClosedCandle>> GetOrAggregateAsync(
        string symbol, string targetTimeframe, Instant from, Instant to, CancellationToken ct);
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
    Task<(IReadOnlyList<DTOs.Backtest.BacktestResultDto> Items, int Total)> GetPagedAsync(Guid? strategyInstanceId, int page, int pageSize, CancellationToken ct, string? strategyName = null);
    Task<byte[]?> GetReportAsync(Guid runId, CancellationToken ct);
    Task<IReadOnlyList<DTOs.Backtest.BacktestResultDto>> GetByScenarioAsync(Guid scenarioId, int page, int pageSize, CancellationToken ct);
    /// <summary>Persist a completed backtest result. Idempotent on DataHash. Returns the persisted run ID.</summary>
    Task<Guid> SaveAsync(DTOs.Backtest.BacktestResultDto result, CancellationToken ct);
}

public interface IStrategyScenarioRepository
{
    Task<Domain.Entities.StrategyScenario?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Domain.Entities.StrategyScenario>> GetByInstanceAsync(Guid instanceId, CancellationToken ct);
    Task AddAsync(Domain.Entities.StrategyScenario scenario, CancellationToken ct);
    Task UpdateAsync(Domain.Entities.StrategyScenario scenario, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    /// <summary>True if the scenario has at least one persisted BacktestRun.</summary>
    Task<bool> HasBacktestAsync(Guid scenarioId, CancellationToken ct);
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

public interface IInstrumentUniverseRepository
{
    Task<(IReadOnlyList<Domain.Entities.InstrumentUniverse> Items, int TotalCount)> GetPagedAsync(
        string? category, bool? active, int page, int pageSize, CancellationToken ct);

    Task<Domain.Entities.InstrumentUniverse?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<Domain.Entities.InstrumentUniverse?> GetBySymbolAndCategoryAsync(
        string symbol, string category, CancellationToken ct);

    Task AddAsync(Domain.Entities.InstrumentUniverse entry, CancellationToken ct);
    Task UpdateAsync(Domain.Entities.InstrumentUniverse entry, CancellationToken ct);
    Task DeleteAsync(Domain.Entities.InstrumentUniverse entry, CancellationToken ct);

    /// <summary>Returns existing (symbol, category) pairs to allow idempotent bulk insert.</summary>
    Task<HashSet<string>> GetExistingKeysAsync(CancellationToken ct);
    Task AddRangeAsync(IReadOnlyList<Domain.Entities.InstrumentUniverse> entries, CancellationToken ct);
}

// ── Strategy Approvals ────────────────────────────────────────────────────────

public record ApprovalCheckResult(
    bool AutomatedChecksPassed,
    decimal? Cagr,
    decimal? Drawdown,
    decimal? Sharpe,
    int?     ForwardTestDays,
    decimal? ForwardWinRate,
    IReadOnlyList<string> FailedChecks);

public interface IStrategyApprovalRepository
{
    Task<Domain.Entities.StrategyApproval?> GetByIdAsync(Guid approvalId, CancellationToken ct);
    Task<Domain.Entities.StrategyApproval?> GetActiveAsync(Guid instanceId, CancellationToken ct);
    Task<IReadOnlyList<Domain.Entities.StrategyApproval>> GetHistoryAsync(Guid instanceId, CancellationToken ct);
    Task AddAsync(Domain.Entities.StrategyApproval approval, CancellationToken ct);
    Task InvalidateAsync(Guid approvalId, string reason, NodaTime.Instant now, CancellationToken ct);
}

// ── Trade Journal ─────────────────────────────────────────────────────────────

public interface ITradeJournalRepository
{
    Task AddAsync(Domain.Entities.TradeJournalEntry entry, CancellationToken ct);
    Task<(IReadOnlyList<DTOs.TradeJournal.TradeJournalEntryDto> Items, int Total)> GetPagedAsync(
        Guid? strategyInstanceId, string? symbol, string? exitReason, string? source,
        int page, int pageSize, CancellationToken ct);
    Task<DTOs.TradeJournal.TradeJournalEntryDto?> GetByIdAsync(Guid id, CancellationToken ct);
    Task UpdateNotesAndTagsAsync(Guid id, string? notes, string[] tags, CancellationToken ct);
    Task<DTOs.TradeJournal.PnlAttributionDto> GetAttributionAsync(
        Guid? strategyInstanceId, string? symbol, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct);
}
