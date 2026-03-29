namespace rvs.AlgoTrader.Application.DTOs.Strategy;

/// <summary>Full scenario DTO returned by GET endpoints.</summary>
public record StrategyScenarioDto(
    Guid   Id,
    Guid   StrategyInstanceId,
    string Name,
    string? Description,
    // Partial override JSON — only keys that differ from the base.
    string? ParametersJsonOverride,
    string Status,
    Guid? LastBacktestRunId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Request to create a new scenario.</summary>
public record CreateScenarioRequest(
    string  Name,
    string? Description,
    // Optional partial JSON. null = scenario uses instance base params verbatim.
    string? ParametersJsonOverride);

/// <summary>Request to update a scenario's name/description/override (blocked when Live).</summary>
public record UpdateScenarioRequest(
    string?  Name,
    string?  Description,
    string?  ParametersJsonOverride);

/// <summary>Lightweight comparison row — one per scenario, used in the comparison grid.</summary>
public record ScenarioComparisonRow(
    Guid    ScenarioId,
    string  ScenarioName,
    string? ParametersJsonOverride,
    // Backtest metrics (null if not yet backtested)
    decimal? TotalReturn,
    decimal? MaxDrawdown,
    decimal? SharpeRatio,
    decimal? WinRate,
    int?     TotalTrades,
    decimal? ProfitFactor,
    decimal? ExpectancyPerTrade,
    string   Status,
    Guid?    LastBacktestRunId);

/// <summary>Request to run all (or a subset of) scenarios for a strategy instance in parallel.</summary>
public record RunScenariosRequest(
    Guid   StrategyInstanceId,
    // Run only these scenario IDs. Empty/null = run all scenarios for the instance.
    IReadOnlyList<Guid>? ScenarioIds,
    string InternalSymbol,
    string Timeframe,
    string FromDate,
    string ToDate,
    decimal InitialCapital = 100_000m,
    decimal RiskPerTradePercent = 1.0m);
