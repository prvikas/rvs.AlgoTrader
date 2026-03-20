using MediatR;

namespace rvs.AlgoTrader.Application.Commands.Alerts;

public record CreateAlertRuleCommand(
    string AlertType,
    string Severity,
    string Condition,
    string? Message = null,
    string Channel = "TELEGRAM") : IRequest<Guid>;

public record DeleteAlertRuleCommand(Guid Id) : IRequest<bool>;
