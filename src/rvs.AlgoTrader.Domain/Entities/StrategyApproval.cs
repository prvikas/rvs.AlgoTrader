using NodaTime;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Immutable record of a manual live-trading approval for a strategy instance.
/// One active (non-invalidated) approval is required before the LiveExecutionEngine
/// will act on any signal from that instance.
/// INSERT-only: never mutated after creation — only new rows or invalidation columns updated.
/// </summary>
public class StrategyApproval
{
    public Guid   Id                      { get; set; }
    public Guid   StrategyInstanceId      { get; set; }
    public string ApprovedBy              { get; set; } = string.Empty;
    public string? ApprovalNotes          { get; set; }
    public Guid?  BacktestResultId        { get; set; }
    public Guid?  ForwardTestSessionId    { get; set; }

    // Metrics captured at approval time (snapshot — not recalculated)
    public decimal? CagrAtApproval        { get; set; }
    public decimal? DrawdownAtApproval    { get; set; }
    public decimal? SharpeAtApproval      { get; set; }
    public int?     ForwardTestDays       { get; set; }
    public decimal? ForwardWinRate        { get; set; }

    public bool     AutomatedChecksPassed { get; set; }

    // Populated only when the approval is subsequently revoked
    public Instant? InvalidatedAt         { get; set; }
    public string?  InvalidationReason    { get; set; }

    public Instant  CreatedAt             { get; set; }

    public bool IsActive => InvalidatedAt == null;
}
