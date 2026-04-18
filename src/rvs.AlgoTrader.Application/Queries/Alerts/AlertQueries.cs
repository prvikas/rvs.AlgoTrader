using MediatR;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Queries.Alerts;

public record GetAlertRulesQuery() : IRequest<IReadOnlyList<AlertRuleDto>>;
public record GetAlertHistoryQuery(int Limit = 50) : IRequest<IReadOnlyList<AlertLogEntry>>;
