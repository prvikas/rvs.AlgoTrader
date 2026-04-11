using rvs.AlgoTrader.Application.DTOs.Strategy;

namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// CRUD for UI-designed strategy definitions (GenericRules engine).
/// Each definition stores the full Strategy JSON from StrategyDefinitionPage
/// and is executed by GenericRulesStrategy during backtesting and forward testing.
/// </summary>
public interface IStrategyDefinitionService
{
    Task<IReadOnlyList<StrategyDefinitionDto>> GetAllAsync(CancellationToken ct);
    Task<StrategyDefinitionDto?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<StrategyDefinitionDto> CreateAsync(UpsertStrategyDefinitionRequest request, CancellationToken ct);
    Task<StrategyDefinitionDto?> UpdateAsync(Guid id, UpsertStrategyDefinitionRequest request, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
}
