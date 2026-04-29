using Microsoft.Extensions.Configuration;
using Npgsql;
using rvs.AlgoTrader.Application.DTOs.Strategy;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services;

// StrategyDefinitionService — persists UI-designed strategies to the
// strategy_definitions table (migration 036).
//
// All queries are scoped to the calling user (userId).  Rows with user_id = NULL
// (created before migration 050) are treated as owned by the system user.

public class StrategyDefinitionService(IConfiguration config, ICurrentUser currentUser) : IStrategyDefinitionService
{
    private string Cs => config.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Database=algotrader;Username=postgres;Password=postgres";

    private string UserId => currentUser.UserId;

    public async Task<IReadOnlyList<StrategyDefinitionDto>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "SELECT id, name, description, trading_style, definition_json, created_at, updated_at " +
            "FROM strategy_definitions " +
            "WHERE user_id = @userId OR user_id IS NULL " +
            "ORDER BY created_at DESC", conn);
        cmd.Parameters.AddWithValue("@userId", Guid.Parse(UserId));

        var list = new List<StrategyDefinitionDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapRow(reader));

        return list;
    }

    public async Task<StrategyDefinitionDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "SELECT id, name, description, trading_style, definition_json, created_at, updated_at " +
            "FROM strategy_definitions WHERE id = @id AND (user_id = @userId OR user_id IS NULL)", conn);
        cmd.Parameters.AddWithValue("@id",     id);
        cmd.Parameters.AddWithValue("@userId", Guid.Parse(UserId));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRow(reader) : null;
    }

    public async Task<StrategyDefinitionDto> CreateAsync(
        UpsertStrategyDefinitionRequest request, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO strategy_definitions (name, description, trading_style, definition_json, user_id)
            VALUES (@name, @description, @tradingStyle, @json::jsonb, @userId)
            RETURNING id, name, description, trading_style, definition_json, created_at, updated_at
            """, conn);

        cmd.Parameters.AddWithValue("@name",         request.Name);
        cmd.Parameters.AddWithValue("@description",  (object?)request.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tradingStyle", request.TradingStyle);
        cmd.Parameters.AddWithValue("@json",         request.DefinitionJson);
        cmd.Parameters.AddWithValue("@userId",       Guid.Parse(UserId));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return MapRow(reader);
    }

    public async Task<StrategyDefinitionDto?> UpdateAsync(
        Guid id, UpsertStrategyDefinitionRequest request, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("""
            UPDATE strategy_definitions
            SET name            = @name,
                description     = @description,
                trading_style   = @tradingStyle,
                definition_json = @json::jsonb,
                updated_at      = NOW()
            WHERE id = @id AND (user_id = @userId OR user_id IS NULL)
            RETURNING id, name, description, trading_style, definition_json, created_at, updated_at
            """, conn);

        cmd.Parameters.AddWithValue("@id",           id);
        cmd.Parameters.AddWithValue("@name",         request.Name);
        cmd.Parameters.AddWithValue("@description",  (object?)request.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tradingStyle", request.TradingStyle);
        cmd.Parameters.AddWithValue("@json",         request.DefinitionJson);
        cmd.Parameters.AddWithValue("@userId",       Guid.Parse(UserId));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRow(reader) : null;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM strategy_definitions WHERE id = @id AND (user_id = @userId OR user_id IS NULL)", conn);
        cmd.Parameters.AddWithValue("@id",     id);
        cmd.Parameters.AddWithValue("@userId", Guid.Parse(UserId));

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static StrategyDefinitionDto MapRow(NpgsqlDataReader r) => new(
        Id:             r.GetGuid(0),
        Name:           r.GetString(1),
        Description:    r.IsDBNull(2) ? null : r.GetString(2),
        TradingStyle:   r.GetString(3),
        DefinitionJson: r.GetString(4),
        CreatedAt:      new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc)),
        UpdatedAt:      new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(6), DateTimeKind.Utc))
    );
}
