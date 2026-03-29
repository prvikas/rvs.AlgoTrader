using NodaTime;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// A multi-leg option spread (Iron Condor, Vertical, Straddle, Strangle, Calendar).
/// Contains 2–4 SpreadPositionLeg records, each mapped to one concrete NFO instrument.
/// P&amp;L is the sum of leg P&amp;L. Opened atomically — all legs or none (at the application layer).
/// </summary>
public class SpreadPosition
{
    public Guid                         Id               { get; set; }
    public string                       BrokerName       { get; set; } = string.Empty;
    public string                       UnderlyingSymbol { get; set; } = string.Empty;
    public string                       SpreadType       { get; set; } = string.Empty;
    public string                       Status           { get; set; } = "Open";
    public decimal?                     NetPremium       { get; set; }
    public decimal?                     RealizedPnl      { get; set; }
    public Guid?                        StrategyRunId    { get; set; }
    public string                       CorrelationId    { get; set; } = string.Empty;
    public Instant                      OpenedAt         { get; set; }
    public Instant?                     ClosedAt         { get; set; }

    public ICollection<SpreadPositionLeg> Legs           { get; set; } = new List<SpreadPositionLeg>();
}

public class SpreadPositionLeg
{
    public Guid           Id               { get; set; }
    public Guid           SpreadPositionId { get; set; }
    public string         InternalSymbol   { get; set; } = string.Empty;
    public string         BrokerToken      { get; set; } = string.Empty;
    public OrderDirection Direction        { get; set; }
    public OptionType     OptionType       { get; set; }
    public decimal        StrikePrice      { get; set; }
    public NodaTime.LocalDate Expiry       { get; set; }
    public int            Quantity         { get; set; }
    public decimal?       EntryPrice       { get; set; }
    public decimal?       ExitPrice        { get; set; }
    public string?        BrokerOrderId    { get; set; }
    public string         Status           { get; set; } = "Pending";

    public SpreadPosition? SpreadPosition  { get; set; }
}
