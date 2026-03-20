using MediatR;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Queries.Alerts;

public class GetAlertRulesHandler : IRequestHandler<GetAlertRulesQuery, IReadOnlyList<object>>
{
    public Task<IReadOnlyList<object>> Handle(GetAlertRulesQuery request, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
}

public class GetAlertHistoryHandler(IAlertLogRepository repo) : IRequestHandler<GetAlertHistoryQuery, IReadOnlyList<AlertLogEntry>>
{
    public async Task<IReadOnlyList<AlertLogEntry>> Handle(GetAlertHistoryQuery request, CancellationToken ct)
        => await repo.GetPagedAsync(1, request.Limit, ct);
}
