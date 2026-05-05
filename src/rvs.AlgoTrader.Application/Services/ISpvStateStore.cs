namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// Persists per-instance state for stateful SPV singleton services across process restarts.
///
/// Currently stores: CircuitBreakerService state (velocity_circuit_breaker_state table).
/// Future: RecoveryManager rolling window (velocity_recovery_state table — Phase 3).
///
/// Implementations use IServiceScopeFactory internally; register as Scoped.
/// </summary>
public interface ISpvStateStore
{
    /// <summary>
    /// Loads the persisted circuit-breaker state for the given strategy instance.
    /// Returns null when no persisted row exists (treat as Normal/fresh start).
    /// </summary>
    Task<PersistedCbState?> LoadCbStateAsync(Guid instanceId, CancellationToken ct);

    /// <summary>
    /// Upserts the circuit-breaker state for the given strategy instance.
    /// Called on every state transition (SoftStop ↔ HardStop ↔ Normal).
    /// </summary>
    Task SaveCbStateAsync(Guid instanceId, PersistedCbState state, CancellationToken ct);
}

/// <summary>DB-serialisable snapshot of CircuitBreakerService state for one strategy instance.</summary>
public sealed class PersistedCbState
{
    public string   State            { get; set; } = "Normal";
    public decimal  DailyLossPercent { get; set; }
    public DateTime? TriggeredAt     { get; set; }        // UTC
    public DateTime? ResetEligibleAt { get; set; }        // UTC
    public DateTime  UpdatedAt       { get; set; } = DateTime.UtcNow;
}
