using rvs.AlgoTrader.Application.DTOs.QuantIntelligence;

namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// CRUD for indicator intelligence cards (P10-A).
/// Cards are seeded via migration 045; users can update any field.
/// </summary>
public interface IIndicatorIntelligenceService
{
    Task<IReadOnlyList<IndicatorIntelligenceDto>> GetAllAsync(CancellationToken ct);
    Task<IndicatorIntelligenceDto?> GetByKeyAsync(string indicatorKey, CancellationToken ct);
    Task<IndicatorIntelligenceDto> UpdateAsync(string indicatorKey, UpdateIndicatorIntelligenceRequest req, CancellationToken ct);
}

/// <summary>
/// CRUD for options metrics intelligence cards (P10-A).
/// Cards are seeded via migration 046; users can update any field.
/// </summary>
public interface IGreeksIntelligenceService
{
    Task<IReadOnlyList<GreeksIntelligenceDto>> GetAllAsync(CancellationToken ct);
    Task<GreeksIntelligenceDto?> GetByKeyAsync(string metricKey, CancellationToken ct);
    Task<GreeksIntelligenceDto> UpdateAsync(string metricKey, UpdateGreeksIntelligenceRequest req, CancellationToken ct);
}
