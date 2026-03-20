using NodaTime;

namespace rvs.AlgoTrader.Domain.Entities;

public class RiskProfile
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public decimal MaxCapitalPerTradePct { get; private set; }
    public int MaxOpenTradesPerSymbol { get; private set; }
    public decimal MaxDailyDrawdownPct { get; private set; }
    public decimal MaxTotalCapitalDeployed { get; private set; }
    public int MaxTradesPerDay { get; private set; }
    public Instant CreatedAt { get; private set; }
    public Instant UpdatedAt { get; private set; }

    private RiskProfile() { }
}
