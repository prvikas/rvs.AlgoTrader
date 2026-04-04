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
    public string? ParametersJson { get; set; }
    public decimal AllocatedCapital { get; set; }

    // ── P4 Approval Gate + Execution Mode (moved here from operational) ──────

    // Navigation properties to related entities (separate concerns)
    public StrategyRuntimeState? RuntimeState { get; set; }
    public BrokerCredential? Credential { get; set; }

    // ── P4 Approval Gate ─────────────────────────────────────────────────────

    /// <summary>True once automated checks pass and a manual approval has been recorded.</summary>
    public bool     ApprovalReady { get; set; }
    /// <summary>Timestamp of the most recent manual approval (null if never approved or revoked).</summary>
    public Instant? ApprovedAt    { get; set; }

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
            ParametersJson = parametersJson
        };
    }

    public void UpdateStatus(StrategyStatus newStatus, Instant now)
    {
        Status = newStatus;
        UpdatedAt = now;
    }

    // Domain methods for P&L and order routing moved to StrategyRuntimeState and BrokerCredential
}
