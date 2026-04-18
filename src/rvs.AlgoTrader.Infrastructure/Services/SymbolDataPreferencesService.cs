using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NodaTime;
using Npgsql;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Persists per-symbol data ingestion preferences to the <c>symbol_data_preferences</c>
/// table (migration 040). Replaces the in-memory singleton stub (SDP-1).
/// Uses IClock for the default from-date (AP-001 — no DateTime.UtcNow).
/// </summary>
public sealed class SymbolDataPreferencesService(IConfiguration config, IClock clock) : ISymbolDataPreferencesService
{
    private string Cs => config.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Database=algotrader;Username=postgres;Password=postgres";

    public async Task<SymbolDataPreferences?> GetPreferencesAsync(string symbol, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT id, internal_symbol, timeframes, from_date, priority, is_active
            FROM symbol_data_preferences
            WHERE internal_symbol = @sym
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("sym", symbol);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return BuildDefault(symbol);

        return MapRow(r);
    }

    public async Task UpsertAsync(SymbolDataPreferences preferences, CancellationToken ct)
    {
        var timeframesJson = JsonSerializer.Serialize(preferences.Timeframes);
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO symbol_data_preferences
                (id, internal_symbol, timeframes, from_date, priority, is_active, created_at, updated_at)
            VALUES (@id, @sym, @tf, @fromDate, @priority, @active, now(), now())
            ON CONFLICT (internal_symbol) DO UPDATE
                SET timeframes = EXCLUDED.timeframes,
                    from_date  = EXCLUDED.from_date,
                    priority   = EXCLUDED.priority,
                    is_active  = EXCLUDED.is_active,
                    updated_at = now()
            """, conn);
        cmd.Parameters.AddWithValue("id",       preferences.Id);
        cmd.Parameters.AddWithValue("sym",      preferences.InternalSymbol);
        cmd.Parameters.AddWithValue("tf",       timeframesJson);
        cmd.Parameters.AddWithValue("fromDate", preferences.FromDate);
        cmd.Parameters.AddWithValue("priority", (short)preferences.Priority);
        cmd.Parameters.AddWithValue("active",   preferences.IsActive);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<SymbolDataPreferences>> GetAllActiveAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT id, internal_symbol, timeframes, from_date, priority, is_active
            FROM symbol_data_preferences
            WHERE is_active = TRUE
            ORDER BY priority, internal_symbol
            """, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var results = new List<SymbolDataPreferences>();
        while (await r.ReadAsync(ct))
            results.Add(MapRow(r));
        return results;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SymbolDataPreferences MapRow(Npgsql.NpgsqlDataReader r)
    {
        var id       = r.GetGuid(0);
        var symbol   = r.GetString(1);
        var tfJson   = r.GetString(2);
        var fromDate = r.GetFieldValue<DateOnly>(3);
        var priority = r.GetInt16(4);
        var isActive = r.GetBoolean(5);

        var timeframes = JsonSerializer.Deserialize<string[]>(tfJson) ?? ["1m", "5m", "15m", "1h", "1d"];
        return new SymbolDataPreferences(id, symbol, timeframes, fromDate, priority, isActive);
    }

    private SymbolDataPreferences BuildDefault(string symbol)
    {
        // AP-001: use IClock, not DateTime.UtcNow
        var utcNow      = clock.NowInstant().ToDateTimeUtc();
        var defaultFrom = DateOnly.FromDateTime(utcNow.AddYears(-1));
        return new SymbolDataPreferences(
            Guid.NewGuid(), symbol,
            ["1m", "5m", "15m", "1h", "1d"],
            defaultFrom, Priority: 5, IsActive: true);
    }
}
