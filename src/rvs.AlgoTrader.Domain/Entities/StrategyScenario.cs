using NodaTime;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// A named parameter variant attached to a strategy instance.
/// Stores only the fields that differ from the instance's base ParametersJson.
/// Effective parameters = merge(instance.ParametersJson, ParametersJsonOverride).
/// Status gates promotion: only Backtested scenarios may go to ForwardTest → Live.
/// </summary>
public class StrategyScenario
{
    public Guid   Id                     { get; set; }
    public Guid   StrategyInstanceId     { get; set; }
    public string Name                   { get; set; } = string.Empty;
    public string? Description           { get; set; }

    /// <summary>
    /// Partial JSON object of overridden parameters, e.g. {"LookbackBars":30,"AtrStopMultiple":2.5}.
    /// null = no override; effective params equal the instance base exactly.
    /// </summary>
    public string? ParametersJsonOverride { get; set; }

    /// <summary>
    /// Capital allocated to this scenario when running forward or live.
    /// If null, the strategy-level AllocatedCapital is used.
    /// </summary>
    public decimal? AllocatedCapital      { get; set; }

    public ScenarioStatus Status         { get; set; } = ScenarioStatus.Draft;

    /// <summary>
    /// Monotonically incremented each time ParametersJsonOverride is changed.
    /// Allows consumers to detect whether a stored backtest result is still current.
    /// </summary>
    public int Version                   { get; set; } = 1;

    /// <summary>ID of the most recent backtest run produced for this scenario.</summary>
    public Guid? LastBacktestRunId       { get; set; }

    public Instant CreatedAt             { get; set; }
    public Instant UpdatedAt             { get; set; }

    // Navigation (not loaded by default — explicit Include when needed)
    public StrategyInstance? StrategyInstance { get; set; }

    public StrategyScenario() { }

    public static StrategyScenario Create(
        Guid strategyInstanceId,
        string name,
        string? description,
        string? parametersJsonOverride,
        Instant now,
        decimal? allocatedCapital = null)
        => new()
        {
            Id                     = Guid.NewGuid(),
            StrategyInstanceId     = strategyInstanceId,
            Name                   = name,
            Description            = description,
            ParametersJsonOverride = parametersJsonOverride,
            AllocatedCapital       = allocatedCapital,
            Status                 = ScenarioStatus.Draft,
            CreatedAt              = now,
            UpdatedAt              = now,
        };
}
