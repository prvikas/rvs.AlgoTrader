using MediatR;
using rvs.AlgoTrader.Application.DTOs.Strategy;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Queries.Strategy;

// ── List all scenarios for a strategy instance ────────────────────────────────

public record GetScenariosQuery(Guid StrategyInstanceId) : IRequest<IReadOnlyList<StrategyScenarioDto>>;

public class GetScenariosQueryHandler(
    IStrategyScenarioRepository repo,
    IStrategyInstanceRepository instanceRepo)
    : IRequestHandler<GetScenariosQuery, IReadOnlyList<StrategyScenarioDto>>
{
    public async Task<IReadOnlyList<StrategyScenarioDto>> Handle(GetScenariosQuery req, CancellationToken ct)
    {
        var list = await repo.GetByInstanceAsync(req.StrategyInstanceId, ct);
        if (list.Count == 0) return [];

        var instance   = await instanceRepo.GetByIdAsync(req.StrategyInstanceId, ct);
        var baseParams = instance?.ParametersJson;

        return list
            .Select(s => ToDto(s, ScenarioParamMerger.Merge(baseParams, s.ParametersJsonOverride)))
            .ToList();
    }

    // effectiveParamsJson: pre-merged base + override JSON for this scenario.
    internal static StrategyScenarioDto ToDto(Domain.Entities.StrategyScenario s, string effectiveParamsJson) => new(
        s.Id,
        s.StrategyInstanceId,
        s.Name,
        s.Description,
        s.ParametersJsonOverride,
        effectiveParamsJson,
        s.AllocatedCapital,
        s.Status.ToString(),
        s.Version,
        s.LastBacktestRunId,
        s.CreatedAt.ToDateTimeOffset(),
        s.UpdatedAt.ToDateTimeOffset());
}

// ── Single scenario by ID ─────────────────────────────────────────────────────

public record GetScenarioByIdQuery(Guid Id) : IRequest<StrategyScenarioDto?>;

public class GetScenarioByIdQueryHandler(
    IStrategyScenarioRepository repo,
    IStrategyInstanceRepository instanceRepo)
    : IRequestHandler<GetScenarioByIdQuery, StrategyScenarioDto?>
{
    public async Task<StrategyScenarioDto?> Handle(GetScenarioByIdQuery req, CancellationToken ct)
    {
        var s = await repo.GetByIdAsync(req.Id, ct);
        if (s == null) return null;

        var instance       = await instanceRepo.GetByIdAsync(s.StrategyInstanceId, ct);
        var effectiveParams = ScenarioParamMerger.Merge(instance?.ParametersJson, s.ParametersJsonOverride);
        return GetScenariosQueryHandler.ToDto(s, effectiveParams);
    }
}

// ── Comparison grid — all scenarios for an instance with backtest metrics ─────

public record GetScenarioComparisonQuery(Guid StrategyInstanceId) : IRequest<IReadOnlyList<ScenarioComparisonRow>>;

public class GetScenarioComparisonQueryHandler(
    IStrategyScenarioRepository scenarioRepo,
    IStrategyInstanceRepository instanceRepo,
    IBacktestRunRepository backtestRepo)
    : IRequestHandler<GetScenarioComparisonQuery, IReadOnlyList<ScenarioComparisonRow>>
{
    public async Task<IReadOnlyList<ScenarioComparisonRow>> Handle(GetScenarioComparisonQuery req, CancellationToken ct)
    {
        var scenarios  = await scenarioRepo.GetByInstanceAsync(req.StrategyInstanceId, ct);
        var instance   = await instanceRepo.GetByIdAsync(req.StrategyInstanceId, ct);
        var baseParams = instance?.ParametersJson;
        var rows       = new List<ScenarioComparisonRow>(scenarios.Count);

        foreach (var s in scenarios)
        {
            DTOs.Backtest.BacktestResultDto? bt = null;
            if (s.LastBacktestRunId.HasValue)
                bt = await backtestRepo.GetByIdAsync(s.LastBacktestRunId.Value, ct);

            var effectiveParams = ScenarioParamMerger.Merge(baseParams, s.ParametersJsonOverride);

            rows.Add(new ScenarioComparisonRow(
                ScenarioId:              s.Id,
                ScenarioName:            s.Name,
                EffectiveParametersJson: effectiveParams,
                AllocatedCapital:        s.AllocatedCapital,
                TotalReturn:             bt?.TotalReturn,
                MaxDrawdown:             bt?.MaxDrawdown,
                SharpeRatio:             bt?.SharpeRatio,
                WinRate:                 bt?.WinRate,
                TotalTrades:             bt?.TotalTrades,
                ProfitFactor:            bt?.ProfitFactor,
                ExpectancyPerTrade:      bt?.ExpectancyPerTrade,
                Status:                  s.Status.ToString(),
                LastBacktestRunId:       s.LastBacktestRunId));
        }

        return rows;
    }
}
