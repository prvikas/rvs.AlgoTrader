namespace rvs.AlgoTrader.Application.DTOs.Strategy;

/// <summary>
/// Returned to the frontend for every strategy-definition row.
/// DefinitionJson is the full Strategy object JSON produced by StrategyDefinitionPage
/// so the frontend can hydrate the form directly on edit.
/// </summary>
public record StrategyDefinitionDto(
    Guid           Id,
    string         Name,
    string?        Description,
    string         TradingStyle,
    string         DefinitionJson,     // full UI Strategy JSON (indicators, rules, stops, etc.)
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

/// <summary>
/// Request body for create / update.
/// DefinitionJson is the full Strategy object produced by StrategyDefinitionPage.buildPayload().
/// </summary>
public record UpsertStrategyDefinitionRequest(
    string  Name,
    string? Description,
    string  TradingStyle,
    string  DefinitionJson   // must be valid JSON
);
