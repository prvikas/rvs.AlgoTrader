using NodaTime;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Domain.Entities;

public class StrategyRun
{
    public Guid Id { get; set; }
    public Guid StrategyInstanceId { get; set; }
    public string? BrokerName { get; set; }
    public StrategyMode Mode { get; set; }
    public StrategyRunStatus Status { get; set; }
    public Instant StartedAt { get; set; }
    public Instant? EndedAt { get; set; }
    public string? EndReason { get; set; }

    // Metrics accumulated during the run
    public decimal TotalPnl { get; set; }
    public int TradeCount { get; set; }
    public int WinCount { get; set; }
    public int LossCount { get; set; }
    public decimal MaxDrawdown { get; set; }

    // EF Core requires parameterless constructor
    public StrategyRun() { }
}
