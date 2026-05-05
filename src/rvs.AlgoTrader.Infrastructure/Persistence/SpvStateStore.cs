using Microsoft.EntityFrameworkCore;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of ISpvStateStore.
/// Reads/writes to velocity_circuit_breaker_state via raw SQL so that no EF model
/// migration is needed — the table already exists (migration 053).
/// Registered as Scoped; singleton callers access it via IServiceScopeFactory.
/// </summary>
public sealed class SpvStateStore(AlgoTraderDbContext db) : ISpvStateStore
{
    public async Task<PersistedCbState?> LoadCbStateAsync(Guid instanceId, CancellationToken ct)
    {
        // EF Core 8+ Database.SqlQuery<T> maps raw results to an arbitrary class
        var rows = await db.Database
            .SqlQuery<PersistedCbState>(
                $"""
                 SELECT
                     state             AS "State",
                     daily_loss_pct    AS "DailyLossPercent",
                     triggered_at      AS "TriggeredAt",
                     reset_eligible_at AS "ResetEligibleAt",
                     updated_at        AS "UpdatedAt"
                 FROM velocity_circuit_breaker_state
                 WHERE strategy_instance_id = {instanceId}
                 LIMIT 1
                 """)
            .ToListAsync(ct);

        return rows.FirstOrDefault();
    }

    public async Task SaveCbStateAsync(Guid instanceId, PersistedCbState state, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO velocity_circuit_breaker_state
                 (strategy_instance_id, state, daily_loss_pct, triggered_at, reset_eligible_at, updated_at)
             VALUES
                 ({instanceId}, {state.State}, {state.DailyLossPercent},
                  {state.TriggeredAt}, {state.ResetEligibleAt}, {state.UpdatedAt})
             ON CONFLICT (strategy_instance_id) DO UPDATE SET
                 state             = EXCLUDED.state,
                 daily_loss_pct    = EXCLUDED.daily_loss_pct,
                 triggered_at      = EXCLUDED.triggered_at,
                 reset_eligible_at = EXCLUDED.reset_eligible_at,
                 updated_at        = EXCLUDED.updated_at
             """,
            ct);
    }
}
