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
    /// <summary>
    /// Authoritative closed-trade P&amp;L (backfilled from Pnl by migration 024; NOT NULL in DB).
    /// Always use this for analytics. Nullable in C# only to handle rows inserted before
    /// migration 024 ran — COALESCE(RealizedPnl, Pnl) in query code for full safety.
    /// </summary>
    public decimal? RealizedPnl { get; set; }
    /// <summary>Legacy P&amp;L column — kept for backward compatibility. Always equals RealizedPnl on new rows.</summary>
    public decimal Pnl { get; set; }
    public string? CloseReason { get; set; }
    public Instant EntryTime { get; set; }
    public Instant? ExitTime { get; set; }
    public Instant? OpenedAt { get; set; }
    public Instant? ClosedAt { get; set; }

    // EF Core requires parameterless constructor
    public ForwardTestTrade() { }
}
