using NodaTime;
using rvs.AlgoTrader.Domain.Constants;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Domain.Entities;

public class StrategyInstance
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string StrategyType { get; set; } = string.Empty;
    public Guid? WatchlistId { get; set; }
    public StrategyMode Mode { get; set; }
    public string? BrokerName { get; set; }
    public bool IsActive { get; set; }
    public StrategyStatus Status { get; set; }
    public string ConfigJson { get; set; } = TradingDefaults.EmptyJson;
    public string? FailureBehaviorJson { get; set; }
    public Guid? RiskProfileId { get; set; }
    public string? ScheduleJson { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public Instant CreatedAt { get; set; }
    public Instant UpdatedAt { get; set; }

    // Operational fields used by Infrastructure services
    public string InternalSymbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public bool AutoResumeOnRestart { get; private set; }
    public Guid? CurrentRunId { get; set; }
    public decimal AllocatedCapital { get; set; }
    public string? ParametersJson { get; set; }

    // Intraday P&L — mutated only via UpdatePnl() / ResetDailyPnl() domain methods
    public decimal TodayRealizedPnl { get; private set; }
    public decimal TodayUnrealizedPnl { get; private set; }

    // Order routing fields — mutated only via UpdateOrderRouting() domain method
    public string? BrokerToken { get; set; }
    public Exchange Exchange { get; private set; } = Enums.Exchange.NSE;
    public ProductType ProductType { get; private set; } = Enums.ProductType.MIS;
    public int LotSize { get; private set; } = 1;

    /// <summary>
    /// Controls whether this strategy instance places real orders (Live),
    /// simulates fills on live data without real orders (Paper),
    /// or runs historical simulation (Backtest).
    /// Defaults to Live to preserve existing behaviour for current instances.
    /// </summary>
    public ExecutionMode ExecutionMode { get; set; } = ExecutionMode.Live;

    /// <summary>Alias for Name; used by domain events and query handlers.</summary>
    public string StrategyName => Name;

    // EF Core requires parameterless constructor
    public StrategyInstance() { }

    public static StrategyInstance Create(
        string name, string strategyType, Guid? watchlistId,
        StrategyMode mode, string? brokerName, string createdBy,
        Instant createdAt, string? internalSymbol = null,
        string? timeframe = null, string? configJson = null,
        string? failureBehaviorJson = null, Guid? riskProfileId = null,
        string? scheduleJson = null, string? parametersJson = null)
    {
        return new StrategyInstance
        {
            Id = Guid.NewGuid(),
            Name = name,
            StrategyType = strategyType,
            WatchlistId = watchlistId,
            Mode = mode,
            BrokerName = brokerName,
            IsActive = true,
            Status = StrategyStatus.Draft,
            ConfigJson = configJson ?? "{}",
            FailureBehaviorJson = failureBehaviorJson,
            RiskProfileId = riskProfileId,
            ScheduleJson = scheduleJson,
            CreatedBy = createdBy,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            InternalSymbol = internalSymbol ?? string.Empty,
            Timeframe = timeframe ?? string.Empty,
            AutoResumeOnRestart = false,
            ParametersJson = parametersJson
        };
    }

    public void UpdateStatus(StrategyStatus newStatus, Instant now)
    {
        Status = newStatus;
        UpdatedAt = now;
    }

    // ── Domain methods for operational fields (#25) ───────────────────────────

    /// <summary>Records intraday P and L from the execution engine after a fill or mark-to-market tick.</summary>
    public void UpdatePnl(decimal realizedPnl, decimal unrealizedPnl)
    {
        TodayRealizedPnl   = realizedPnl;
        TodayUnrealizedPnl = unrealizedPnl;
    }

    /// <summary>Resets intraday P and L counters at the start of each trading day.</summary>
    public void ResetDailyPnl()
    {
        TodayRealizedPnl   = 0m;
        TodayUnrealizedPnl = 0m;
    }

    /// <summary>Controls whether this instance auto-resumes after a server restart.</summary>
    public void SetAutoResume(bool value) => AutoResumeOnRestart = value;

    /// <summary>
    /// Updates order-routing configuration.
    /// Called from command handlers when the user edits the instance, not from the execution engine.
    /// </summary>
    public void UpdateOrderRouting(Exchange exchange, ProductType productType, int lotSize)
    {
        Exchange    = exchange;
        ProductType = productType;
        LotSize     = lotSize > 0 ? lotSize : 1;
    }
}
