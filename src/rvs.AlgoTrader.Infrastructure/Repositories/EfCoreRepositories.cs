using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.Text.Json;
using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.Strategy;
using rvs.AlgoTrader.Application.DTOs.TradeJournal;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

// ─────────────────────────────────────────────────────────────────────────────
// EF Core implementations that replace the in-memory stubs in StubRepositories.cs.
// These are backed by the tables created in migration 010_StubRepositoriesSchema.sql.
// ─────────────────────────────────────────────────────────────────────────────

// ── Audit log ─────────────────────────────────────────────────────────────────

public class EfAuditLogRepository(AlgoTraderDbContext db) : IAuditLogRepository
{
    public async Task AppendAsync(string action, string actor, string entityType, string entityId,
        object? details, string correlationId, Instant occurredAt, CancellationToken ct)
    {
        // DB schema from InitialMigration.sql uses event_type + details (JSONB) column names
        var detailsJson = details is null ? null : JsonSerializer.Serialize(details);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO audit_log (event_type, actor, entity_type, entity_id, details, correlation_id, occurred_at)
            VALUES ({action}, {actor}, {entityType}, {entityId}, {detailsJson}::jsonb, {correlationId}, {occurredAt.ToDateTimeOffset()})
            """, ct);
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetPagedAsync(
        int page, int pageSize, string? entityType, string? actor, CancellationToken ct)
    {
        // Parameterized SQL — {0}/{1} become PostgreSQL $1/$2 parameters (never string-concatenated).
        // The IS NULL OR col = param pattern lets us pass null to mean "no filter" without
        // building a dynamic WHERE clause, which avoids any SQL injection vector.
        var offset = (page - 1) * pageSize;
        var rows = await db.Database
            .SqlQueryRaw<AuditLogRow>(
                "SELECT id, event_type AS action, actor, entity_type, entity_id, " +
                "details::text AS details_json, correlation_id, occurred_at " +
                "FROM audit_log " +
                "WHERE ({0}::text IS NULL OR entity_type = {0}) " +
                "  AND ({1}::text IS NULL OR actor = {1}) " +
                "ORDER BY occurred_at DESC LIMIT {2} OFFSET {3}",
                entityType ?? (object)DBNull.Value, actor ?? (object)DBNull.Value, pageSize, offset)
            .ToListAsync(ct);

        return rows.Select(r => new AuditLogEntry(
            r.Id, r.Action, r.Actor, r.EntityType, r.EntityId,
            r.DetailsJson, r.CorrelationId, r.OccurredAt)).ToList();
    }

    // Projection type for EF raw SQL query
    private sealed class AuditLogRow
    {
        public long Id { get; set; }
        public string Action { get; set; } = "";
        public string Actor { get; set; } = "";
        public string EntityType { get; set; } = "";
        public string EntityId { get; set; } = "";
        public string? DetailsJson { get; set; }
        public string CorrelationId { get; set; } = "";
        public DateTimeOffset OccurredAt { get; set; }
    }
}

// ── Alert log ─────────────────────────────────────────────────────────────────

public class EfAlertLogRepository(AlgoTraderDbContext db) : IAlertLogRepository
{
    public async Task AddAsync(AlertLogEntry entry, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO alert_log (id, alert_type, severity, message, occurred_at)
            VALUES ({entry.Id}, {entry.AlertType}, {entry.Severity}, {entry.Message}, {entry.OccurredAt})
            """, ct);
    }

    public async Task<IReadOnlyList<AlertLogEntry>> GetPagedAsync(int page, int pageSize, CancellationToken ct)
    {
        var offset = (page - 1) * pageSize;
        var rows = await db.Database
            .SqlQueryRaw<AlertLogRow>(
                "SELECT id, alert_type, severity, message, occurred_at " +
                "FROM alert_log ORDER BY occurred_at DESC LIMIT {0} OFFSET {1}",
                pageSize, offset)
            .ToListAsync(ct);

        return rows.Select(r => new AlertLogEntry(r.Id, r.AlertType, r.Severity, r.Message, r.OccurredAt)).ToList();
    }

    private sealed class AlertLogRow
    {
        public Guid Id { get; set; }
        public string AlertType { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTimeOffset OccurredAt { get; set; }
    }
}

// ── Download jobs ─────────────────────────────────────────────────────────────

public class EfDownloadJobRepository(AlgoTraderDbContext db) : IDownloadJobRepository
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<DownloadJob?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQueryRaw<DownloadJobRow>(
                "SELECT id, symbol, timeframe, from_date, to_date, broker_name, status, " +
                "completed_chunks, total_chunks, created_at FROM download_jobs WHERE id = {0}", id)
            .ToListAsync(ct);
        return rows.Select(ToDto).FirstOrDefault();
    }

    public async Task<IReadOnlyList<DownloadJob>> GetInProgressAsync(CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQueryRaw<DownloadJobRow>(
                "SELECT id, symbol, timeframe, from_date, to_date, broker_name, status, " +
                "completed_chunks, total_chunks, created_at FROM download_jobs " +
                "WHERE status NOT IN ('Completed', 'Failed') ORDER BY created_at DESC")
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task AddAsync(DownloadJob job, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO download_jobs (id, symbol, timeframe, from_date, to_date, broker_name, status, completed_chunks, total_chunks, created_at)
            VALUES ({job.Id}, {job.Symbol}, {job.Timeframe}, {job.FromDate}, {job.ToDate}, {job.BrokerName}, {job.Status}, {job.CompletedChunks}, {job.TotalChunks}, {job.CreatedAt.ToDateTimeOffset()})
            """, ct);
    }

    public async Task UpdateAsync(DownloadJob job, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE download_jobs SET status = {job.Status}, completed_chunks = {job.CompletedChunks}, total_chunks = {job.TotalChunks}
            WHERE id = {job.Id}
            """, ct);
    }

    private static DownloadJob ToDto(DownloadJobRow r) => new(
        r.Id, r.Symbol, r.Timeframe, r.FromDate, r.ToDate,
        r.BrokerName, r.Status, r.CompletedChunks, r.TotalChunks,
        Instant.FromDateTimeOffset(r.CreatedAt));

    private sealed class DownloadJobRow
    {
        public Guid Id { get; set; }
        public string Symbol { get; set; } = "";
        public string Timeframe { get; set; } = "";
        public DateOnly FromDate { get; set; }
        public DateOnly ToDate { get; set; }
        public string BrokerName { get; set; } = "";
        public string Status { get; set; } = "";
        public int CompletedChunks { get; set; }
        public int TotalChunks { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}

// ── Signal journal ────────────────────────────────────────────────────────────

public class EfSignalJournalRepository(AlgoTraderDbContext db) : ISignalJournalRepository
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task AppendAsync(SignalJournalEntry entry, CancellationToken ct)
    {
        var diagJson = entry.DiagnosticsJson is null ? null : JsonSerializer.Serialize(entry.DiagnosticsJson, _json);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO signal_journal_entries
                (strategy_instance_id, internal_symbol, evaluated_at, timeframe, signal,
                 entry_price, stop_loss, take_profit, reason, diagnostics_json, acted_on, skipped_reason)
            VALUES
                ({entry.StrategyInstanceId}, {entry.InternalSymbol}, {entry.EvaluatedAt.ToDateTimeOffset()},
                 {entry.Timeframe}, {entry.Signal}, {entry.EntryPrice}, {entry.StopLoss}, {entry.TakeProfit},
                 {entry.Reason}, {diagJson}, {entry.ActedOn}, {entry.SkippedReason})
            """, ct);
    }

    public async Task<IReadOnlyList<SignalJournalEntry>> GetFilteredAsync(
        Guid? strategyId, string? symbol, string? signal, string? skippedReason,
        int page, int pageSize, CancellationToken ct)
    {
        var dtos = await GetPagedAsync(strategyId, symbol, signal, skippedReason, page, pageSize, ct);
        return dtos.Items.Select(dto => new SignalJournalEntry(
            dto.Id, dto.StrategyInstanceId, dto.InternalSymbol,
            Instant.FromDateTimeOffset(dto.EvaluatedAt), dto.Timeframe, dto.Signal,
            dto.EntryPrice, dto.StopLoss, dto.TakeProfit, dto.Reason,
            dto.DiagnosticsJson, dto.ActedOn, dto.SkippedReason)).ToList();
    }

    public async Task<(IReadOnlyList<SignalJournalEntryDto> Items, int Total)> GetPagedAsync(
        Guid? strategyId, string? symbol, string? signal, string? skippedReason,
        int page, int pageSize, CancellationToken ct)
    {
        // All optional filters use {N}::type IS NULL OR col = {N} so a single parameterized query
        // covers all combinations — no string concatenation, no Esc() helpers (AP-SQL).
        // skippedReason is used as a presence flag (filter to non-null rows when provided).
        var offset       = (page - 1) * pageSize;
        var strategyIdStr = strategyId?.ToString();

        const string SelectSql =
            "SELECT id, strategy_instance_id, internal_symbol, evaluated_at, timeframe, signal, " +
            "entry_price, stop_loss, take_profit, reason, diagnostics_json, acted_on, skipped_reason " +
            "FROM signal_journal_entries " +
            "WHERE ({0}::uuid IS NULL OR strategy_instance_id = {0}::uuid) " +
            "  AND ({1}::text IS NULL OR internal_symbol = {1}) " +
            "  AND ({2}::text IS NULL OR signal = {2}) " +
            "  AND ({3}::text IS NULL OR skipped_reason IS NOT NULL) " +
            "ORDER BY evaluated_at DESC LIMIT {4} OFFSET {5}";

        const string CountSql =
            "SELECT COUNT(*)::int AS value FROM signal_journal_entries " +
            "WHERE ({0}::uuid IS NULL OR strategy_instance_id = {0}::uuid) " +
            "  AND ({1}::text IS NULL OR internal_symbol = {1}) " +
            "  AND ({2}::text IS NULL OR signal = {2}) " +
            "  AND ({3}::text IS NULL OR skipped_reason IS NOT NULL)";

        var rows = await db.Database
            .SqlQueryRaw<SignalJournalRow>(SelectSql,
                strategyIdStr ?? (object)DBNull.Value,
                symbol        ?? (object)DBNull.Value,
                signal        ?? (object)DBNull.Value,
                skippedReason ?? (object)DBNull.Value,
                pageSize, offset)
            .ToListAsync(ct);

        var totalRows = await db.Database
            .SqlQueryRaw<CountRow>(CountSql,
                strategyIdStr ?? (object)DBNull.Value,
                symbol        ?? (object)DBNull.Value,
                signal        ?? (object)DBNull.Value,
                skippedReason ?? (object)DBNull.Value)
            .FirstOrDefaultAsync(ct);

        var items = rows.Select(r => new SignalJournalEntryDto(
            r.Id, r.StrategyInstanceId, r.InternalSymbol, r.EvaluatedAt,
            r.Timeframe, r.Signal, r.EntryPrice, r.StopLoss, r.TakeProfit,
            r.Reason, r.DiagnosticsJson, r.ActedOn, r.SkippedReason)).ToList();

        return (items, totalRows?.Value ?? 0);
    }

    private sealed class SignalJournalRow
    {
        public long Id { get; set; }
        public Guid StrategyInstanceId { get; set; }
        public string InternalSymbol { get; set; } = "";
        public DateTimeOffset EvaluatedAt { get; set; }
        public string Timeframe { get; set; } = "";
        public string Signal { get; set; } = "";
        public decimal? EntryPrice { get; set; }
        public decimal? StopLoss { get; set; }
        public decimal? TakeProfit { get; set; }
        public string? Reason { get; set; }
        public string? DiagnosticsJson { get; set; }
        public bool ActedOn { get; set; }
        public string? SkippedReason { get; set; }
    }

    private sealed class CountRow { public int Value { get; set; } }
}

// ── User preferences ──────────────────────────────────────────────────────────

public class EfUserPreferencesRepository(AlgoTraderDbContext db) : IUserPreferencesRepository
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<UserPreferencesDto?> GetByUserAsync(string userId, CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQueryRaw<UserPrefsRow>(
                "SELECT preferences_json FROM user_preferences WHERE user_id = {0}", userId)
            .ToListAsync(ct);

        var row = rows.FirstOrDefault();
        if (row is null) return null;
        return JsonSerializer.Deserialize<UserPreferencesDto>(row.PreferencesJson, _json);
    }

    public async Task UpsertAsync(string userId, UserPreferencesDto prefs, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(prefs, _json);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO user_preferences (user_id, preferences_json, updated_at)
            VALUES ({userId}, {json}, now())
            ON CONFLICT (user_id) DO UPDATE
                SET preferences_json = EXCLUDED.preferences_json,
                    updated_at       = EXCLUDED.updated_at
            """, ct);
    }

    private sealed class UserPrefsRow { public string PreferencesJson { get; set; } = "{}"; }
}

// ── Capital allocation (EF Core — has AlgoTraderDbContext.CapitalAllocations DbSet) ────────────

public class EfCapitalAllocationRepository(AlgoTraderDbContext db) : ICapitalAllocationRepository
{
    public async Task<IReadOnlyList<CapitalAllocation>> GetAllAsync(CancellationToken ct)
        => await db.CapitalAllocations.ToListAsync(ct);

    public async Task<CapitalAllocation?> GetByIdAsync(Guid id, CancellationToken ct)
        => await db.CapitalAllocations.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<CapitalAllocation?> GetByInstanceAsync(Guid instanceId, CancellationToken ct)
        => await db.CapitalAllocations.FirstOrDefaultAsync(a => a.StrategyInstanceId == instanceId, ct);

    public async Task AddAsync(CapitalAllocation alloc, CancellationToken ct)
    {
        await db.CapitalAllocations.AddAsync(alloc, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(CapitalAllocation alloc, CancellationToken ct)
    {
        db.CapitalAllocations.Update(alloc);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteByInstanceAsync(Guid instanceId, CancellationToken ct)
    {
        var alloc = await db.CapitalAllocations.FirstOrDefaultAsync(a => a.StrategyInstanceId == instanceId, ct);
        if (alloc is not null)
        {
            db.CapitalAllocations.Remove(alloc);
            await db.SaveChangesAsync(ct);
        }
    }
}

// ── Watchlist (EF Core — has AlgoTraderDbContext.Watchlists DbSet) ────────────

public class EfWatchlistRepository(AlgoTraderDbContext db) : IWatchlistRepository
{
    public async Task<Watchlist?> GetByIdAsync(Guid id, CancellationToken ct)
        => await db.Watchlists
            .Include(w => w.Symbols)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

    public async Task<IReadOnlyList<Watchlist>> GetByUserAsync(string userId, CancellationToken ct)
        => await db.Watchlists
            .Include(w => w.Symbols)
            .Where(w => w.CreatedBy == userId)
            .OrderBy(w => w.Name)
            .ToListAsync(ct);

    public async Task AddAsync(Watchlist watchlist, CancellationToken ct)
    {
        await db.Watchlists.AddAsync(watchlist, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Watchlist watchlist, CancellationToken ct)
    {
        db.Watchlists.Update(watchlist);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var watchlist = await db.Watchlists.FindAsync([id], ct);
        if (watchlist is not null)
        {
            db.Watchlists.Remove(watchlist);
            await db.SaveChangesAsync(ct);
        }
    }
}

// ── OptionIvHistory ───────────────────────────────────────────────────────────

public class EfOptionIvHistoryRepository(AlgoTraderDbContext db) : IOptionIvHistoryRepository
{
    public Task<IReadOnlyList<OptionIvHistory>> GetRecentAsync(
        string underlyingSymbol, int days, CancellationToken ct)
        => db.OptionIvHistory
             .Where(h => h.UnderlyingSymbol == underlyingSymbol)
             .OrderByDescending(h => h.Date)
             .Take(days)
             .ToListAsync(ct)
             .ContinueWith(t => (IReadOnlyList<OptionIvHistory>)t.Result, ct);

    // FIB-3: Date-range query for backtest pre-fetch (avoids O(n) DB calls per bar).
    public Task<IReadOnlyList<OptionIvHistory>> GetRangeAsync(
        string underlyingSymbol, NodaTime.LocalDate from, NodaTime.LocalDate to, CancellationToken ct)
        => db.OptionIvHistory
             .Where(h => h.UnderlyingSymbol == underlyingSymbol && h.Date >= from && h.Date <= to)
             .OrderByDescending(h => h.Date)
             .ToListAsync(ct)
             .ContinueWith(t => (IReadOnlyList<OptionIvHistory>)t.Result, ct);

    public async Task UpsertAsync(OptionIvHistory record, CancellationToken ct)
    {
        var existing = await db.OptionIvHistory
            .FirstOrDefaultAsync(h => h.UnderlyingSymbol == record.UnderlyingSymbol
                                   && h.Date == record.Date, ct);
        if (existing != null)
        {
            existing.AtmIv      = record.AtmIv;
            existing.RecordedAt = record.RecordedAt;
        }
        else
        {
            db.OptionIvHistory.Add(record);
        }
        await db.SaveChangesAsync(ct);
    }
}

// ── SpreadPosition ────────────────────────────────────────────────────────────

public class EfSpreadPositionRepository(AlgoTraderDbContext db) : ISpreadPositionRepository
{
    public Task<SpreadPosition?> GetByIdAsync(Guid id, CancellationToken ct)
        => db.SpreadPositions
             .Include(s => s.Legs)
             .FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<IReadOnlyList<SpreadPosition>> GetOpenAsync(CancellationToken ct)
        => db.SpreadPositions
             .Include(s => s.Legs)
             .Where(s => s.Status == "Open")
             .ToListAsync(ct)
             .ContinueWith(t => (IReadOnlyList<SpreadPosition>)t.Result, ct);

    public async Task AddAsync(SpreadPosition spread, CancellationToken ct)
    {
        db.SpreadPositions.Add(spread);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(SpreadPosition spread, CancellationToken ct)
    {
        db.SpreadPositions.Update(spread);
        await db.SaveChangesAsync(ct);
    }
}

// ── FxRateProvider ────────────────────────────────────────────────────────────

public class EfFxRateProvider(AlgoTraderDbContext db) : IFxRateProvider
{
    public async Task<decimal> GetRateToInrAsync(
        string baseCurrency, NodaTime.LocalDate date, CancellationToken ct)
    {
        if (string.Equals(baseCurrency, "INR", StringComparison.OrdinalIgnoreCase)) return 1m;

        // Exact date first, then most recent prior date (handles weekends/holidays)
        var rate = await db.FxRates
            .Where(r => r.BaseCurrency == baseCurrency && r.QuoteCurrency == "INR"
                     && r.Date <= date)
            .OrderByDescending(r => r.Date)
            .Select(r => (decimal?)r.Rate)
            .FirstOrDefaultAsync(ct);

        return rate ?? 1m; // fallback to 1:1 if no data — caller should log a warning
    }

    public async Task UpsertAsync(FxRate record, CancellationToken ct)
    {
        var existing = await db.FxRates
            .FirstOrDefaultAsync(r => r.BaseCurrency == record.BaseCurrency
                                   && r.QuoteCurrency == record.QuoteCurrency
                                   && r.Date == record.Date, ct);
        if (existing != null)
        {
            existing.Rate       = record.Rate;
            existing.RecordedAt = record.RecordedAt;
        }
        else
        {
            db.FxRates.Add(record);
        }
        await db.SaveChangesAsync(ct);
    }
}

// ── Trade Journal ─────────────────────────────────────────────────────────────

public class EfTradeJournalRepository(AlgoTraderDbContext db) : ITradeJournalRepository
{
    public async Task AddAsync(TradeJournalEntry entry, CancellationToken ct)
    {
        db.TradeJournalEntries.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task<(IReadOnlyList<TradeJournalEntryDto> Items, int Total)> GetPagedAsync(
        Guid? strategyInstanceId, string? symbol, string? exitReason, string? source,
        int page, int pageSize, CancellationToken ct)
    {
        var q = db.TradeJournalEntries.AsQueryable();
        if (strategyInstanceId.HasValue) q = q.Where(e => e.StrategyInstanceId == strategyInstanceId.Value);
        if (!string.IsNullOrEmpty(symbol))     q = q.Where(e => e.InternalSymbol == symbol);
        if (!string.IsNullOrEmpty(exitReason)) q = q.Where(e => e.ExitReason == exitReason);
        if (!string.IsNullOrEmpty(source))     q = q.Where(e => e.Source == source);

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(e => e.ExitTime)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new TradeJournalEntryDto(
                e.Id, e.StrategyInstanceId, e.InternalSymbol, e.Direction, e.Quantity,
                e.EntryPrice, e.ExitPrice, e.StopLoss, e.TakeProfit,
                e.EntryTime.ToDateTimeOffset(), e.ExitTime.ToDateTimeOffset(),
                e.GrossPnl, e.NetPnl, e.Commission, e.Stt,
                e.RMultiple, e.InitialRisk, e.Mae, e.Mfe,
                e.ExitReason, e.EntryReason, e.Notes, e.Tags,
                e.TaxClassification, e.HoldingDays, e.Source, e.SourceTradeId,
                e.CreatedAt.ToDateTimeOffset()))
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<TradeJournalEntryDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var e = await db.TradeJournalEntries.FindAsync([id], ct);
        if (e == null) return null;
        return new TradeJournalEntryDto(
            e.Id, e.StrategyInstanceId, e.InternalSymbol, e.Direction, e.Quantity,
            e.EntryPrice, e.ExitPrice, e.StopLoss, e.TakeProfit,
            e.EntryTime.ToDateTimeOffset(), e.ExitTime.ToDateTimeOffset(),
            e.GrossPnl, e.NetPnl, e.Commission, e.Stt,
            e.RMultiple, e.InitialRisk, e.Mae, e.Mfe,
            e.ExitReason, e.EntryReason, e.Notes, e.Tags,
            e.TaxClassification, e.HoldingDays, e.Source, e.SourceTradeId,
            e.CreatedAt.ToDateTimeOffset());
    }

    public async Task UpdateNotesAndTagsAsync(Guid id, string? notes, string[] tags, CancellationToken ct)
    {
        var e = await db.TradeJournalEntries.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"TradeJournalEntry {id} not found");
        e.Notes = notes;
        e.Tags  = tags;
        await db.SaveChangesAsync(ct);
    }

    public async Task<PnlAttributionDto> GetAttributionAsync(
        Guid? strategyInstanceId, string? symbol, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct)
    {
        var q = db.TradeJournalEntries.AsQueryable();
        if (strategyInstanceId.HasValue) q = q.Where(e => e.StrategyInstanceId == strategyInstanceId.Value);
        if (!string.IsNullOrEmpty(symbol))  q = q.Where(e => e.InternalSymbol == symbol);
        if (fromDate.HasValue) q = q.Where(e => e.ExitTime >= Instant.FromDateTimeOffset(fromDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
        if (toDate.HasValue)   q = q.Where(e => e.ExitTime <= Instant.FromDateTimeOffset(toDate.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc)));

        var rows = await q.Select(e => new {
            e.StrategyInstanceId, e.InternalSymbol, e.NetPnl, e.ExitReason, e.RMultiple,
            ExitDt = e.ExitTime.ToDateTimeOffset()
        }).ToListAsync(ct);

        static PnlByDimension Agg(string label, IEnumerable<dynamic> group)
        {
            var list = group.ToList();
            var wins  = list.Where(r => r.NetPnl > 0).ToList();
            var losses = list.Where(r => r.NetPnl <= 0).ToList();
            return new PnlByDimension(
                label,
                list.Sum(r => (decimal)r.NetPnl),
                wins.Sum(r => (decimal)r.NetPnl),
                losses.Sum(r => (decimal)r.NetPnl),
                list.Count, wins.Count,
                list.Count > 0 ? (decimal)wins.Count / list.Count : 0,
                list.Any(r => r.RMultiple != null)
                    ? list.Where(r => r.RMultiple != null).Average(r => (decimal)r.RMultiple!) : 0);
        }

        var bySymbol = rows.GroupBy(r => r.InternalSymbol)
            .Select(g => Agg(g.Key, g.Cast<dynamic>())).ToList();

        var byMonth = rows.GroupBy(r => r.ExitDt.ToString("yyyy-MM"))
            .OrderBy(g => g.Key)
            .Select(g => Agg(g.Key, g.Cast<dynamic>())).ToList();

        var byDow = rows.GroupBy(r => r.ExitDt.DayOfWeek.ToString())
            .Select(g => Agg(g.Key, g.Cast<dynamic>())).ToList();

        var byExit = rows.GroupBy(r => r.ExitReason)
            .Select(g => Agg(g.Key, g.Cast<dynamic>())).ToList();

        var bySession = rows.GroupBy(r => r.ExitDt.TimeOfDay.Hours < 12 ? "Morning" : "Afternoon")
            .Select(g => Agg(g.Key, g.Cast<dynamic>())).ToList();

        // Cross-strategy breakdown — only when aggregating all strategies.
        // Join strategy_instances to resolve human-readable strategy name labels.
        List<PnlByDimension>? byStrategy = null;
        if (!strategyInstanceId.HasValue && rows.Count > 0)
        {
            var instanceIds = rows.Select(r => r.StrategyInstanceId).Distinct().ToList();
            var nameMap = await db.StrategyInstances
                .Where(s => instanceIds.Contains(s.Id))
                .Select(s => new { s.Id, s.StrategyType })
                .ToDictionaryAsync(s => s.Id, s => s.StrategyType, ct);

            byStrategy = rows
                .GroupBy(r => nameMap.TryGetValue(r.StrategyInstanceId, out var t) ? t : r.StrategyInstanceId.ToString())
                .Select(g => Agg(g.Key, g.Cast<dynamic>()))
                .OrderByDescending(d => d.TotalPnl)
                .ToList();
        }

        return new PnlAttributionDto(bySymbol, byMonth, byDow, byExit, bySession, byStrategy);
    }
}

// ── Monitoring alert rules ─────────────────────────────────────────────────────

public class EfAlertRulesRepository(AlgoTraderDbContext db) : IAlertRulesRepository
{
    public async Task<IReadOnlyList<AlertRuleDto>> GetAllAsync(string userId, CancellationToken ct)
    {
        var rules = await db.MonitoringAlertRules
            .AsNoTracking()
            .Where(r => r.UserId == null || r.UserId.ToString() == userId)
            .OrderBy(r => r.AlertType)
            .ToListAsync(ct);
        return rules.Select(ToDto).ToList();
    }

    public async Task<Guid> AddAsync(AlertRuleDto rule, string userId, CancellationToken ct)
    {
        var entity = new MonitoringAlertRule
        {
            Id              = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id,
            AlertType       = rule.AlertType,
            MetricName      = rule.MetricName,
            Operator        = rule.Operator,
            ThresholdValue  = rule.ThresholdValue,
            Severity        = rule.Severity,
            Channels        = rule.Channels,
            IsActive        = rule.IsActive,
            MessageTemplate = rule.MessageTemplate,
            UserId          = Guid.TryParse(userId, out var uid) ? uid : null,
        };
        db.MonitoringAlertRules.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.MonitoringAlertRules.FindAsync([id], ct);
        if (entity == null) return false;
        db.MonitoringAlertRules.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static AlertRuleDto ToDto(MonitoringAlertRule r) =>
        new(r.Id, r.AlertType, r.MetricName, r.Operator, r.ThresholdValue,
            r.Severity, r.Channels, r.IsActive, r.MessageTemplate);
}

// ── User repository ────────────────────────────────────────────────────────────

public class EfUserRepository(AlgoTraderDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct)
        => db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct)
        => db.Users.AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.Username != null && u.Username.ToLower() == username.ToLower() && u.IsActive, ct);

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct)
        // OAuth-only users have null Username — sort by DisplayName then Email as fallback (AP-001 compliant)
        => await db.Users.AsNoTracking().Where(u => u.IsActive)
            .OrderBy(u => u.DisplayName ?? u.Email ?? u.Username)
            .ToListAsync(ct);

    public async Task<Guid> CreateAsync(User user, CancellationToken ct)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user.Id;
    }

    public async Task UpdateAsync(User user, CancellationToken ct)
    {
        db.Users.Update(user);
        await db.SaveChangesAsync(ct);
    }
}

// ── UserBrokerAccount repository ──────────────────────────────────────────────

public class EfUserBrokerAccountRepository(AlgoTraderDbContext db) : IUserBrokerAccountRepository
{
    public async Task<IReadOnlyList<UserBrokerAccount>> GetByUserIdAsync(string userId, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out var uid)) return [];
        return await db.UserBrokerAccounts.AsNoTracking()
            .Where(a => a.UserId == uid && a.IsActive)
            .OrderBy(a => a.BrokerName)
            .ToListAsync(ct);
    }

    public Task<UserBrokerAccount?> GetAsync(string userId, string brokerName, string market, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out var uid)) return Task.FromResult<UserBrokerAccount?>(null);
        return db.UserBrokerAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == uid
                && a.BrokerName == brokerName
                && a.Market == market, ct);
    }

    public async Task<Guid> AddAsync(UserBrokerAccount account, CancellationToken ct)
    {
        db.UserBrokerAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account.Id;
    }

    public async Task<bool> SetActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var entity = await db.UserBrokerAccounts.FindAsync([id], ct);
        if (entity == null) return false;
        entity.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.UserBrokerAccounts.FindAsync([id], ct);
        if (entity == null) return false;
        db.UserBrokerAccounts.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

// ── UserExternalLogin repository ──────────────────────────────────────────────

public class EfUserExternalLoginRepository(AlgoTraderDbContext db) : IUserExternalLoginRepository
{
    public Task<UserExternalLogin?> FindAsync(string provider, string providerSub, CancellationToken ct)
        => db.UserExternalLogins
             .Include(l => l.User)
             .AsNoTracking()
             .FirstOrDefaultAsync(l => l.Provider == provider && l.ProviderSub == providerSub, ct);

    public Task<User?> FindUserByEmailAsync(string email, CancellationToken ct)
        => db.Users
             .AsNoTracking()
             .FirstOrDefaultAsync(
                 u => u.Email != null && u.Email.ToLower() == email.ToLower() && u.IsActive, ct);

    public async Task AddAsync(UserExternalLogin login, CancellationToken ct)
    {
        db.UserExternalLogins.Add(login);
        await db.SaveChangesAsync(ct);
    }
}
