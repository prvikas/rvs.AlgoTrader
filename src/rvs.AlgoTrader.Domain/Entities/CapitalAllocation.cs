using NodaTime;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Tracks capital allocated per strategy instance.
/// ReservedCapital: atomically reserved in Redis before order placement (Lua CAS).
/// AvailableCapital: AllocatedCapital − ReservedCapital (DB snapshot; source of truth is Redis).
/// </summary>
public class CapitalAllocation
{
    public Guid Id { get; private set; }
    public Guid StrategyInstanceId { get; private set; }
    public short BrokerId { get; private set; }
    public decimal AllocatedCapital { get; private set; }
    public decimal ReservedCapital { get; private set; }
    public decimal AvailableCapital => AllocatedCapital - ReservedCapital;
    public Instant CreatedAt { get; private set; }
    public Instant UpdatedAt { get; private set; }

    // Navigation
    public virtual Broker? Broker { get; set; }

    private CapitalAllocation() { }

    public static CapitalAllocation Create(
        Guid strategyInstanceId,
        short brokerId,
        decimal allocatedCapital,
        Instant now)
    {
        if (allocatedCapital <= 0)
            throw new ArgumentOutOfRangeException(nameof(allocatedCapital), "Allocated capital must be positive");

        return new CapitalAllocation
        {
            Id = Guid.NewGuid(),
            StrategyInstanceId = strategyInstanceId,
            BrokerId = brokerId,
            AllocatedCapital = allocatedCapital,
            ReservedCapital = 0m,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>Updates allocated capital and/or broker assignment.</summary>
    public void UpdateAllocation(decimal newCapital, short brokerId, Instant now)
    {
        if (newCapital <= 0)
            throw new ArgumentOutOfRangeException(nameof(newCapital), "Allocated capital must be positive");
        AllocatedCapital = newCapital;
        BrokerId = brokerId;
        UpdatedAt = now;
    }

    /// <summary>Legacy overload — preserves existing broker id.</summary>
    public void UpdateAllocation(decimal newCapital, Instant now) =>
        UpdateAllocation(newCapital, BrokerId, now);

    /// <summary>Sync reserved capital from Redis snapshot (reconciliation path).</summary>
    public void SyncReservedCapital(decimal reserved, Instant now)
    {
        ReservedCapital = reserved;
        UpdatedAt = now;
    }
}
