using NodaTime;

namespace rvs.AlgoTrader.Domain.Entities;

public class ForwardTestSession
{
    public Guid Id { get; set; }
    public Guid StrategyInstanceId { get; set; }
    public Instant StartedAt { get; set; }
    public Instant? EndedAt { get; set; }
    public decimal InitialCapital { get; set; }
    public decimal FinalCapital { get; set; }
    public decimal FinalPnl { get; set; }
    public int TradeCount { get; set; }
    public decimal WinRate { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal SharpeRatio { get; set; }
    /// <summary>Optional link to the backtest run that seeded this session via "Promote to Forward Test".</summary>
    public Guid? SourceBacktestId { get; set; }
    public string Status { get; set; } = "Running";

    // EF Core requires parameterless constructor
    public ForwardTestSession() { }
}
