using Microsoft.Extensions.Configuration;
using Npgsql;
using rvs.AlgoTrader.Application.DTOs.Strategy;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services;

public class StrategyDefinitionScenarioService(IConfiguration config) : IStrategyDefinitionScenarioService
{
    private string Cs => config.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Database=algotrader;Username=postgres;Password=postgres";

    public async Task<IReadOnlyList<StrategyDefinitionScenarioDto>> ListAsync(Guid definitionId, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("""
            SELECT id, strategy_definition_id, name, description,
                   capital, broker_account, backtest_from, backtest_to,
                   parameter_overrides, status, last_run_at, last_metrics,
                   created_at, updated_at
            FROM strategy_definition_scenarios
            WHERE strategy_definition_id = @defId
            ORDER BY created_at ASC
            """, conn);
        cmd.Parameters.AddWithValue("@defId", definitionId);

        var list = new List<StrategyDefinitionScenarioDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapRow(reader));

        return list;
    }

    public async Task<StrategyDefinitionScenarioDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("""
            SELECT id, strategy_definition_id, name, description,
                   capital, broker_account, backtest_from, backtest_to,
                   parameter_overrides, status, last_run_at, last_metrics,
                   created_at, updated_at
            FROM strategy_definition_scenarios
            WHERE id = @id
            """, conn);
        cmd.Parameters.AddWithValue("@id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRow(reader) : null;
    }

    public async Task<StrategyDefinitionScenarioDto> CreateAsync(
        Guid definitionId, UpsertDefinitionScenarioRequest request, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO strategy_definition_scenarios
                (strategy_definition_id, name, description, capital, broker_account,
                 backtest_from, backtest_to, parameter_overrides, status)
            VALUES
                (@defId, @name, @desc, @capital, @broker,
                 @from::date, @to::date, @overrides::jsonb, @status)
            RETURNING id, strategy_definition_id, name, description,
                      capital, broker_account, backtest_from, backtest_to,
                      parameter_overrides, status, last_run_at, last_metrics,
                      created_at, updated_at
            """, conn);

        cmd.Parameters.AddWithValue("@defId",    definitionId);
        cmd.Parameters.AddWithValue("@name",     request.Name);
        cmd.Parameters.AddWithValue("@desc",     (object?)request.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@capital",  request.Capital);
        cmd.Parameters.AddWithValue("@broker",   request.BrokerAccount);
        cmd.Parameters.AddWithValue("@from",     request.BacktestFrom);
        cmd.Parameters.AddWithValue("@to",       request.BacktestTo);
        cmd.Parameters.AddWithValue("@overrides",request.ParameterOverridesJson);
        cmd.Parameters.AddWithValue("@status",   request.Status ?? "Draft");

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return MapRow(reader);
    }

    public async Task<StrategyDefinitionScenarioDto?> UpdateAsync(
        Guid id, UpsertDefinitionScenarioRequest request, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("""
            UPDATE strategy_definition_scenarios
            SET name              = @name,
                description       = @desc,
                capital           = @capital,
                broker_account    = @broker,
                backtest_from     = @from::date,
                backtest_to       = @to::date,
                parameter_overrides = @overrides::jsonb,
                status            = COALESCE(@status, status),
                updated_at        = NOW()
            WHERE id = @id
            RETURNING id, strategy_definition_id, name, description,
                      capital, broker_account, backtest_from, backtest_to,
                      parameter_overrides, status, last_run_at, last_metrics,
                      created_at, updated_at
            """, conn);

        cmd.Parameters.AddWithValue("@id",       id);
        cmd.Parameters.AddWithValue("@name",     request.Name);
        cmd.Parameters.AddWithValue("@desc",     (object?)request.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@capital",  request.Capital);
        cmd.Parameters.AddWithValue("@broker",   request.BrokerAccount);
        cmd.Parameters.AddWithValue("@from",     request.BacktestFrom);
        cmd.Parameters.AddWithValue("@to",       request.BacktestTo);
        cmd.Parameters.AddWithValue("@overrides",request.ParameterOverridesJson);
        cmd.Parameters.AddWithValue("@status",   (object?)request.Status ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRow(reader) : null;
    }

    public async Task<StrategyDefinitionScenarioDto?> PatchAsync(
        Guid id, PatchDefinitionScenarioRequest request, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("""
            UPDATE strategy_definition_scenarios
            SET status       = COALESCE(@status, status),
                last_run_at  = COALESCE(@lastRunAt, last_run_at),
                last_metrics = COALESCE(@metrics::jsonb, last_metrics),
                updated_at   = NOW()
            WHERE id = @id
            RETURNING id, strategy_definition_id, name, description,
                      capital, broker_account, backtest_from, backtest_to,
                      parameter_overrides, status, last_run_at, last_metrics,
                      created_at, updated_at
            """, conn);

        cmd.Parameters.AddWithValue("@id",        id);
        cmd.Parameters.AddWithValue("@status",    (object?)request.Status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lastRunAt", (object?)request.LastRunAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@metrics",   (object?)request.LastMetricsJson ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRow(reader) : null;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM strategy_definition_scenarios WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private static StrategyDefinitionScenarioDto MapRow(NpgsqlDataReader r) => new(
        Id:                    r.GetGuid(0),
        StrategyDefinitionId:  r.GetGuid(1),
        Name:                  r.GetString(2),
        Description:           r.IsDBNull(3)  ? null : r.GetString(3),
        Capital:               r.GetDecimal(4),
        BrokerAccount:         r.GetString(5),
        // Npgsql 9 returns `date` as DateOnly on raw NpgsqlConnection — safe to use GetFieldValue<DateOnly>.
        BacktestFrom:          r.GetFieldValue<DateOnly>(6).ToString("yyyy-MM-dd"),
        BacktestTo:            r.GetFieldValue<DateOnly>(7).ToString("yyyy-MM-dd"),
        ParameterOverridesJson:r.GetString(8),
        Status:                r.GetString(9),
        // SCN-1: Npgsql 9 returns timestamptz as DateTime (UTC kind) on raw NpgsqlConnection, not DateTimeOffset.
        // Wrap via DateTimeOffset(utc, offset:0) to satisfy the DTO's DateTimeOffset type.
        LastRunAt:             r.IsDBNull(10) ? null
                                              : new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(10), DateTimeKind.Utc)),
        LastMetricsJson:       r.IsDBNull(11) ? null : r.GetString(11),
        CreatedAt:             new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(12), DateTimeKind.Utc)),
        UpdatedAt:             new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(13), DateTimeKind.Utc))
    );
}
