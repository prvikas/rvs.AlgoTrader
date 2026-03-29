using NodaTime;
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
    public string ConfigJson { get; set; } = "{}";
    public string? FailureBehaviorJson { get; set; }
    public Guid? RiskProfileId { get; set; }
    public string? ScheduleJson { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public Instant CreatedAt { get; set; }
    public Instant UpdatedAt { get; set; }

    // Operational fields used by Infrastructure services
    public string InternalSymbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public bool AutoResumeOnRestart { get; set; }
    public Guid? CurrentRunId { get; set; }
    public decimal AllocatedCapital { get; set; }
    public string? ParametersJson { get; set; }

    // Intraday P&L — updated by the execution engine on each trade/tick
    public decimal TodayRealizedPnl { get; set; }
    public decimal TodayUnrealizedPnl { get; set; }

    // Order routing fields (used by LiveExecutionEngine)
    public string? BrokerToken { get; set; }
    public Exchange Exchange { get; set; } = Enums.Exchange.NSE;
    public ProductType ProductType { get; set; } = Enums.ProductType.MIS;
    public int LotSize { get; set; } = 1;

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
}
