using rvs.AlgoTrader.Application.DTOs.Strategy;

namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// CRUD for scenarios attached to a UI-designed strategy definition.
/// These are test configurations (date range, capital, overrides) stored against
/// the definition rather than a deployed instance.
/// </summary>
public interface IStrategyDefinitionScenarioService
{
    Task<IReadOnlyList<StrategyDefinitionScenarioDto>> ListAsync(Guid definitionId, CancellationToken ct);
    Task<StrategyDefinitionScenarioDto?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<StrategyDefinitionScenarioDto> CreateAsync(Guid definitionId, UpsertDefinitionScenarioRequest request, CancellationToken ct);
    Task<StrategyDefinitionScenarioDto?> UpdateAsync(Guid id, UpsertDefinitionScenarioRequest request, CancellationToken ct);
    Task<StrategyDefinitionScenarioDto?> PatchAsync(Guid id, PatchDefinitionScenarioRequest request, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
}
