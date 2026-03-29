using MediatR;
using rvs.AlgoTrader.Application.DTOs.Strategy;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Queries.Strategy;

// ── List all scenarios for a strategy instance ────────────────────────────────

public record GetScenariosQuery(Guid StrategyInstanceId) : IRequest<IReadOnlyList<StrategyScenarioDto>>;

public class GetScenariosQueryHandler(IStrategyScenarioRepository repo)
    : IRequestHandler<GetScenariosQuery, IReadOnlyList<StrategyScenarioDto>>
{
    public async Task<IReadOnlyList<StrategyScenarioDto>> Handle(GetScenariosQuery req, CancellationToken ct)
    {
        var list = await repo.GetByInstanceAsync(req.StrategyInstanceId, ct);
        return list.Select(ToDto).ToList();
    }

    internal static StrategyScenarioDto ToDto(Domain.Entities.StrategyScenario s) => new(
        s.Id,
        s.StrategyInstanceId,
        s.Name,
        s.Description,
        s.ParametersJsonOverride,
        s.Status.ToString(),
        s.LastBacktestRunId,
        s.CreatedAt.ToDateTimeOffset(),
        s.UpdatedAt.ToDateTimeOffset());
}

// ── Single scenario by ID ─────────────────────────────────────────────────────

public record GetScenarioByIdQuery(Guid Id) : IRequest<StrategyScenarioDto?>;

public class GetScenarioByIdQueryHandler(IStrategyScenarioRepository repo)
    : IRequestHandler<GetScenarioByIdQuery, StrategyScenarioDto?>
{
    public async Task<StrategyScenarioDto?> Handle(GetScenarioByIdQuery req, CancellationToken ct)
    {
        var s = await repo.GetByIdAsync(req.Id, ct);
        return s == null ? null : GetScenariosQueryHandler.ToDto(s);
    }
}

// ── Comparison grid — all scenarios for an instance with backtest metrics ─────

public record GetScenarioComparisonQuery(Guid StrategyInstanceId) : IRequest<IReadOnlyList<ScenarioComparisonRow>>;

public class GetScenarioComparisonQueryHandler(
    IStrategyScenarioRepository scenarioRepo,
    IBacktestRunRepository backtestRepo)
    : IRequestHandler<GetScenarioComparisonQuery, IReadOnlyList<ScenarioComparisonRow>>
{
    public async Task<IReadOnlyList<ScenarioComparisonRow>> Handle(GetScenarioComparisonQuery req, CancellationToken ct)
    {
        var scenarios = await scenarioRepo.GetByInstanceAsync(req.StrategyInstanceId, ct);
        var rows      = new List<ScenarioComparisonRow>(scenarios.Count);

        foreach (var s in scenarios)
        {
            DTOs.Backtest.BacktestResultDto? bt = null;
            if (s.LastBacktestRunId.HasValue)
            {
                bt = await backtestRepo.GetByIdAsync(s.LastBacktestRunId.Value, ct);
            }

            rows.Add(new ScenarioComparisonRow(
                ScenarioId:             s.Id,
                ScenarioName:           s.Name,
                ParametersJsonOverride: s.ParametersJsonOverride,
                TotalReturn:            bt?.TotalReturn,
                MaxDrawdown:            bt?.MaxDrawdown,
                SharpeRatio:            bt?.SharpeRatio,
                WinRate:                bt?.WinRate,
                TotalTrades:            bt?.TotalTrades,
                ProfitFactor:           bt?.ProfitFactor,
                ExpectancyPerTrade:     bt?.ExpectancyPerTrade,
                Status:                 s.Status.ToString(),
                LastBacktestRunId:      s.LastBacktestRunId));
        }

        return rows;
    }
}
