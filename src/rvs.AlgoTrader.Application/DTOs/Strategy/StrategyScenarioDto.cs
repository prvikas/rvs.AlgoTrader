using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Application.DTOs.Strategy;

/// <summary>
/// Full scenario DTO returned by GET endpoints.
/// ParametersJsonOverride: partial diff — only keys that differ from the strategy's base ParametersJson.
/// EffectiveParametersJson: merged result (base + override) — what the engine actually receives.
/// </summary>
public record StrategyScenarioDto(
    Guid     Id,
    Guid     StrategyInstanceId,
    string   Name,
    string?  Description,
    string?  ParametersJsonOverride,
    string   EffectiveParametersJson,
    decimal? AllocatedCapital,
    string   Status,
    Guid?    LastBacktestRunId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Request to create a new scenario.</summary>
public record CreateScenarioRequest(
    string   Name,
    string?  Description,
    // Optional partial JSON — only the keys to override. null = use strategy base params verbatim.
    string?  ParametersJsonOverride,
    decimal? AllocatedCapital = null);

/// <summary>Request to update a scenario's name/description/override (blocked when Live).</summary>
public record UpdateScenarioRequest(
    string?  Name,
    string?  Description,
    string?  ParametersJsonOverride,
    decimal? AllocatedCapital = null);

/// <summary>
/// Lightweight comparison row — one per scenario, used in the comparison grid.
/// EffectiveParametersJson is the fully-merged params (base + override) for this scenario.
/// </summary>
public record ScenarioComparisonRow(
    Guid     ScenarioId,
    string   ScenarioName,
    string   EffectiveParametersJson,
    decimal? AllocatedCapital,
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

/// <summary>API-layer request body for POST {instanceId}/scenarios/{scenarioId}/promote.</summary>
public record PromoteScenarioRequest(ScenarioStatus TargetStatus);

/// <summary>API-layer request body for POST {instanceId}/scenarios/run.</summary>
public record RunScenariosApiRequest(
    string InternalSymbol,
    string Timeframe,
    string FromDate,
    string ToDate,
    decimal InitialCapital        = 100_000m,
    decimal RiskPerTradePercent   = 1.0m,
    IReadOnlyList<Guid>? ScenarioIds = null);
