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
    public string BrokerName { get; private set; } = string.Empty;
    public decimal AllocatedCapital { get; private set; }
    public decimal ReservedCapital { get; private set; }
    public decimal AvailableCapital => AllocatedCapital - ReservedCapital;
    public Instant CreatedAt { get; private set; }
    public Instant UpdatedAt { get; private set; }

    private CapitalAllocation() { }

    public static CapitalAllocation Create(
        Guid strategyInstanceId,
        string brokerName,
        decimal allocatedCapital,
        Instant now)
    {
        if (allocatedCapital <= 0)
            throw new ArgumentOutOfRangeException(nameof(allocatedCapital), "Allocated capital must be positive");

        return new CapitalAllocation
        {
            Id = Guid.NewGuid(),
            StrategyInstanceId = strategyInstanceId,
            BrokerName = brokerName,
            AllocatedCapital = allocatedCapital,
            ReservedCapital = 0m,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>Updates allocated capital and/or broker assignment.</summary>
    public void UpdateAllocation(decimal newCapital, string brokerName, Instant now)
    {
        if (newCapital <= 0)
            throw new ArgumentOutOfRangeException(nameof(newCapital), "Allocated capital must be positive");
        AllocatedCapital = newCapital;
        BrokerName = brokerName;
        UpdatedAt = now;
    }

    /// <summary>Legacy overload — preserves existing broker name.</summary>
    public void UpdateAllocation(decimal newCapital, Instant now) =>
        UpdateAllocation(newCapital, BrokerName, now);

    /// <summary>Sync reserved capital from Redis snapshot (reconciliation path).</summary>
    public void SyncReservedCapital(decimal reserved, Instant now)
    {
        ReservedCapital = reserved;
        UpdatedAt = now;
    }
}
