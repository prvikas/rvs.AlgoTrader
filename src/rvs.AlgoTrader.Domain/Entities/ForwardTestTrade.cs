using NodaTime;

namespace rvs.AlgoTrader.Domain.Entities;

public class ForwardTestTrade
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string InternalSymbol { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal? SimulatedFillPrice { get; set; }
    public decimal? Slippage { get; set; }
    public decimal? RealizedPnl { get; set; }
    public decimal Pnl { get; set; }
    public string? CloseReason { get; set; }
    public Instant EntryTime { get; set; }
    public Instant? ExitTime { get; set; }
    public Instant? OpenedAt { get; set; }
    public Instant? ClosedAt { get; set; }

    // EF Core requires parameterless constructor
    public ForwardTestTrade() { }
}
