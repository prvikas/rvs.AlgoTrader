namespace rvs.AlgoTrader.Application.DTOs.Strategy;

/// <summary>
/// One scenario owned by a strategy_definition row (GenericRules UI-designed strategy).
/// parameter_overrides_json holds the ParameterOverride[] array from the frontend.
/// last_metrics_json holds RunMetrics JSON from the latest completed backtest.
/// </summary>
public record StrategyDefinitionScenarioDto(
    Guid            Id,
    Guid            StrategyDefinitionId,
    string          Name,
    string?         Description,
    decimal         Capital,
    string          BrokerAccount,
    string          BacktestFrom,         // ISO date "YYYY-MM-DD"
    string          BacktestTo,           // ISO date "YYYY-MM-DD"
    string          ParameterOverridesJson,
    string          Status,
    DateTimeOffset? LastRunAt,
    string?         LastMetricsJson,
    DateTimeOffset  CreatedAt,
    DateTimeOffset  UpdatedAt
);

/// <summary>Request body for create / update.</summary>
public record UpsertDefinitionScenarioRequest(
    string   Name,
    string?  Description,
    decimal  Capital,
    string   BrokerAccount,
    string   BacktestFrom,
    string   BacktestTo,
    string   ParameterOverridesJson,
    string?  Status = null
);

/// <summary>Patch to update status and/or metrics after a backtest completes.</summary>
public record PatchDefinitionScenarioRequest(
    string?  Status,
    string?  LastMetricsJson,
    DateTimeOffset? LastRunAt
);
