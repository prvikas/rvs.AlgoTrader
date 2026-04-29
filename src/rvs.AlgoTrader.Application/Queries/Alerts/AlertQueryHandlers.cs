using MediatR;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Queries.Alerts;

public class GetAlertRulesHandler(IAlertRulesRepository repo, ICurrentUser currentUser)
    : IRequestHandler<GetAlertRulesQuery, IReadOnlyList<AlertRuleDto>>
{
    public Task<IReadOnlyList<AlertRuleDto>> Handle(GetAlertRulesQuery request, CancellationToken ct)
        => repo.GetAllAsync(currentUser.UserId, ct);
}

public class GetAlertHistoryHandler(IAlertLogRepository repo) : IRequestHandler<GetAlertHistoryQuery, IReadOnlyList<AlertLogEntry>>
{
    public async Task<IReadOnlyList<AlertLogEntry>> Handle(GetAlertHistoryQuery request, CancellationToken ct)
        => await repo.GetPagedAsync(1, request.Limit, ct);
}
