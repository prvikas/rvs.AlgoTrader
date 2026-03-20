using MediatR;

namespace rvs.AlgoTrader.Application.Commands.Alerts;

public class CreateAlertRuleHandler : IRequestHandler<CreateAlertRuleCommand, Guid>
{
    public Task<Guid> Handle(CreateAlertRuleCommand request, CancellationToken ct)
    {
        // Alert rule persistence not yet implemented — returns a placeholder ID
        return Task.FromResult(Guid.NewGuid());
    }
}

public class DeleteAlertRuleHandler : IRequestHandler<DeleteAlertRuleCommand, bool>
{
    public Task<bool> Handle(DeleteAlertRuleCommand request, CancellationToken ct)
    {
        // Alert rule deletion not yet implemented
        return Task.FromResult(true);
    }
}
