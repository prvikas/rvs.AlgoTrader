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

    public ScenarioStatus Status         { get; set; } = ScenarioStatus.Draft;

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
        Instant now)
        => new()
        {
            Id                     = Guid.NewGuid(),
            StrategyInstanceId     = strategyInstanceId,
            Name                   = name,
            Description            = description,
            ParametersJsonOverride = parametersJsonOverride,
            Status                 = ScenarioStatus.Draft,
            CreatedAt              = now,
            UpdatedAt              = now,
        };
}
