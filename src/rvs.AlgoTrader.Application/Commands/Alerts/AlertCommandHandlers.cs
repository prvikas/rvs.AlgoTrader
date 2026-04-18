using MediatR;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Commands.Alerts;

public class CreateAlertRuleHandler(IAlertRulesRepository repo) : IRequestHandler<CreateAlertRuleCommand, Guid>
{
    public Task<Guid> Handle(CreateAlertRuleCommand request, CancellationToken ct)
    {
        // Map command fields to AlertRuleDto.
        // Condition is stored as MetricName; threshold defaults to 0 when not separately provided.
        var dto = new AlertRuleDto(
            Id:             Guid.Empty,   // repo assigns a new UUID on insert
            AlertType:      request.AlertType,
            MetricName:     request.Condition,
            Operator:       ">",
            ThresholdValue: 0.0,
            Severity:       request.Severity,
            Channels:       [request.Channel],
            IsActive:       true,
            MessageTemplate: request.Message ?? request.AlertType);

        return repo.AddAsync(dto, ct);
    }
}

public class DeleteAlertRuleHandler(IAlertRulesRepository repo) : IRequestHandler<DeleteAlertRuleCommand, bool>
{
    public Task<bool> Handle(DeleteAlertRuleCommand request, CancellationToken ct)
        => repo.DeleteAsync(request.Id, ct);
}
