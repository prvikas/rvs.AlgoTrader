using MediatR;
using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Queries.Backtest;

public class GetBacktestResultHandler(IBacktestRunRepository repo) : IRequestHandler<GetBacktestResultQuery, BacktestResultDto?>
{
    public async Task<BacktestResultDto?> Handle(GetBacktestResultQuery request, CancellationToken ct)
    {
        return await repo.GetByIdAsync(request.RunId, ct);
    }
}

public class GetBacktestRunsHandler(IBacktestRunRepository repo) : IRequestHandler<GetBacktestRunsQuery, PagedResult<BacktestResultDto>>
{
    public async Task<PagedResult<BacktestResultDto>> Handle(GetBacktestRunsQuery request, CancellationToken ct)
    {
        var (items, total) = await repo.GetPagedAsync(request.StrategyInstanceId, request.Page, request.PageSize, ct, request.StrategyName);
        return new PagedResult<BacktestResultDto>(items, total, request.Page, request.PageSize);
    }
}

public class GetBacktestCostProfilesHandler(IBacktestCostProfileRepository repo) : IRequestHandler<GetBacktestCostProfilesQuery, IReadOnlyList<BacktestCostProfileDto>>
{
    public async Task<IReadOnlyList<BacktestCostProfileDto>> Handle(GetBacktestCostProfilesQuery request, CancellationToken ct)
    {
        return await repo.GetAllAsync(ct);
    }
}

public class GetBacktestCostProfileByIdHandler(IBacktestCostProfileRepository repo) : IRequestHandler<GetBacktestCostProfileByIdQuery, BacktestCostProfileDto?>
{
    public async Task<BacktestCostProfileDto?> Handle(GetBacktestCostProfileByIdQuery request, CancellationToken ct)
    {
        return await repo.GetByIdAsync(request.Id, ct);
    }
}

public class ReproduceBacktestHandler(IBacktestRunRepository repo, IBacktestReproductionService reproduction) : IRequestHandler<ReproduceBacktestQuery, BacktestResultDto?>
{
    public async Task<BacktestResultDto?> Handle(ReproduceBacktestQuery request, CancellationToken ct)
    {
        var original = await repo.GetByIdAsync(request.RunId, ct);
        if (original is null) return null;
        return await reproduction.ReproduceAsync(original, ct);
    }
}

public class RunBacktestHandler(IBacktestService backtest) : IRequestHandler<RunBacktestQuery, BacktestResultDto>
{
    public Task<BacktestResultDto> Handle(RunBacktestQuery request, CancellationToken ct)
        => backtest.RunAsync(request.Request, ct);
}

public class RunWalkForwardHandler(IBacktestService backtest) : IRequestHandler<RunWalkForwardQuery, object>
{
    public Task<object> Handle(RunWalkForwardQuery request, CancellationToken ct)
        => backtest.RunWalkForwardAsync(request.Request, ct);
}

public class GetBacktestResultsHandler(IBacktestRunRepository repo) : IRequestHandler<GetBacktestResultsQuery, IReadOnlyList<BacktestResultDto>>
{
    public Task<IReadOnlyList<BacktestResultDto>> Handle(GetBacktestResultsQuery request, CancellationToken ct)
        => repo.GetAllAsync(request.StrategyName, ct);
}

public class GetBacktestReportHandler(IBacktestRunRepository repo) : IRequestHandler<GetBacktestReportQuery, byte[]?>
{
    public Task<byte[]?> Handle(GetBacktestReportQuery request, CancellationToken ct)
        => repo.GetReportAsync(request.RunId, ct);
}

public class GetBacktestsByScenarioHandler(IBacktestRunRepository repo) : IRequestHandler<GetBacktestsByScenarioQuery, IReadOnlyList<BacktestResultDto>>
{
    public Task<IReadOnlyList<BacktestResultDto>> Handle(GetBacktestsByScenarioQuery request, CancellationToken ct)
        => repo.GetByScenarioAsync(request.ScenarioId, request.Page, request.PageSize, ct);
}
