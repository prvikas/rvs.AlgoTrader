using MediatR;
using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.DTOs.Common;

namespace rvs.AlgoTrader.Application.Queries.Backtest;

// Execution queries (handled via IBacktestService in Infrastructure)
public record RunBacktestQuery(BacktestRequestDto Request) : IRequest<BacktestResultDto>;
public record RunWalkForwardQuery(BacktestRequestDto Request) : IRequest<object>;

// Report / listing queries
public record GetBacktestResultsQuery(string? StrategyName) : IRequest<IReadOnlyList<BacktestResultDto>>;
public record GetBacktestReportQuery(Guid RunId) : IRequest<byte[]?>;
public record GetBacktestResultQuery(Guid RunId) : IRequest<BacktestResultDto?>;
public record GetBacktestRunsQuery(Guid? StrategyInstanceId, int Page = 1, int PageSize = 20, string? StrategyName = null) : IRequest<PagedResult<BacktestResultDto>>;
public record GetBacktestCostProfilesQuery() : IRequest<IReadOnlyList<BacktestCostProfileDto>>;
public record GetBacktestCostProfileByIdQuery(Guid Id) : IRequest<BacktestCostProfileDto?>;
public record ReproduceBacktestQuery(Guid RunId) : IRequest<BacktestResultDto?>;
public record GetBacktestsByScenarioQuery(Guid ScenarioId, int Page = 1, int PageSize = 50) : IRequest<IReadOnlyList<BacktestResultDto>>;
public record GetBacktestsByDefinitionQuery(Guid DefinitionId, int Page = 1, int PageSize = 100) : IRequest<IReadOnlyList<BacktestResultDto>>;
