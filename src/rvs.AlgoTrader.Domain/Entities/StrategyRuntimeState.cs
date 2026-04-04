using NodaTime;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Runtime state of a strategy instance — operational metrics and transient status.
/// Separated from StrategyInstance (definition) to follow SRP.
/// 1:1 relationship with StrategyInstance.
/// </summary>
public class StrategyRuntimeState
{
    public Guid StrategyInstanceId { get; set; }
    public StrategyInstance? StrategyInstance { get; set; }

    /// <summary>Active StrategyRun ID if currently running; null if idle.</summary>
    public Guid? CurrentRunId { get; set; }

    /// <summary>Intraday realized P&amp;L — updated after each fill or position close.</summary>
    public decimal TodayRealizedPnl { get; private set; }

    /// <summary>Intraday unrealized P&amp;L — updated at each mark-to-market tick.</summary>
    public decimal TodayUnrealizedPnl { get; private set; }

    /// <summary>Whether to auto-resume this instance after server restart during active session.</summary>
    public bool AutoResumeOnRestart { get; private set; }

    /// <summary>Audit fields.</summary>
    public Instant CreatedAt { get; set; }
    public Instant UpdatedAt { get; set; }

    // EF Core requires parameterless constructor
    public StrategyRuntimeState() { }

    public static StrategyRuntimeState Create(Guid strategyInstanceId, Instant now)
    {
        return new StrategyRuntimeState
        {
            StrategyInstanceId = strategyInstanceId,
            TodayRealizedPnl = 0m,
            TodayUnrealizedPnl = 0m,
            AutoResumeOnRestart = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Records intraday P&amp;L from the execution engine after a fill or mark-to-market tick.</summary>
    public void UpdatePnl(decimal realizedPnl, decimal unrealizedPnl)
    {
        TodayRealizedPnl = realizedPnl;
        TodayUnrealizedPnl = unrealizedPnl;
    }

    /// <summary>Resets intraday P&amp;L counters at the start of each trading day.</summary>
    public void ResetDailyPnl()
    {
        TodayRealizedPnl = 0m;
        TodayUnrealizedPnl = 0m;
    }

    /// <summary>Controls whether this instance auto-resumes after a server restart.</summary>
    public void SetAutoResume(bool value) => AutoResumeOnRestart = value;
}
