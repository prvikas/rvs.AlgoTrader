using MediatR;
using FluentValidation;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Application.Commands.Strategy;

// ── Existing commands (used internally / by other handlers) ──────────────────

public record StartStrategyInstanceCommand(Guid InstanceId, string Actor, string CorrelationId) : IRequest<bool>;
public record PauseStrategyInstanceCommand(Guid InstanceId, string Actor, string CorrelationId) : IRequest<bool>;
public record StopStrategyInstanceCommand(Guid InstanceId, string Actor, string CorrelationId) : IRequest<bool>;

public class StartStrategyInstanceValidator : AbstractValidator<StartStrategyInstanceCommand>
{
    public StartStrategyInstanceValidator()
    {
        RuleFor(x => x.InstanceId).NotEmpty();
        RuleFor(x => x.Actor).NotEmpty();
    }
}

public class StartStrategyInstanceHandler(IStrategyInstanceManager manager, IAuditService audit) : IRequestHandler<StartStrategyInstanceCommand, bool>
{
    public async Task<bool> Handle(StartStrategyInstanceCommand request, CancellationToken ct)
    {
        await manager.StartAsync(request.InstanceId, ct);
        await audit.LogAsync("STRATEGY_STARTED", request.Actor, "StrategyInstance", request.InstanceId.ToString(), null, request.CorrelationId, ct);
        return true;
    }
}

public class PauseStrategyInstanceHandler(IStrategyInstanceManager manager, IAuditService audit) : IRequestHandler<PauseStrategyInstanceCommand, bool>
{
    public async Task<bool> Handle(PauseStrategyInstanceCommand request, CancellationToken ct)
    {
        await manager.PauseAsync(request.InstanceId, "USER_REQUESTED", ct);
        await audit.LogAsync("STRATEGY_PAUSED", request.Actor, "StrategyInstance", request.InstanceId.ToString(), null, request.CorrelationId, ct);
        return true;
    }
}

public class StopStrategyInstanceHandler(IStrategyInstanceManager manager, IAuditService audit) : IRequestHandler<StopStrategyInstanceCommand, bool>
{
    public async Task<bool> Handle(StopStrategyInstanceCommand request, CancellationToken ct)
    {
        await manager.StopAsync(request.InstanceId, "USER_REQUESTED", ct);
        await audit.LogAsync("STRATEGY_STOPPED", request.Actor, "StrategyInstance", request.InstanceId.ToString(), null, request.CorrelationId, ct);
        return true;
    }
}

// ── Controller-facing commands (used by StrategiesController) ─────────────────

/// <summary>Start a strategy instance; returns the new StrategyRun ID.</summary>
public record StartStrategyCommand(Guid InstanceId) : IRequest<Guid>;

/// <summary>Pause a running strategy instance. InstanceId is overridden from the route.</summary>
public record PauseStrategyCommand(Guid InstanceId = default, string? Reason = null) : IRequest<bool>;

/// <summary>Stop a strategy instance. InstanceId is overridden from the route.</summary>
public record StopStrategyCommand(Guid InstanceId = default, string? Reason = null) : IRequest<bool>;

/// <summary>Permanently delete a strategy instance definition.</summary>
public record DeleteStrategyInstanceCommand(Guid Id) : IRequest<bool>;

/// <summary>Create a new strategy instance; returns its new ID.</summary>
public record CreateStrategyInstanceCommand(
    string Name,
    string StrategyType,
    Guid? WatchlistId,
    string Mode,
    string? BrokerName,
    string? ConfigJson,
    string? FailureBehaviorJson,
    Guid? RiskProfileId,
    string? ScheduleJson,
    string? InternalSymbol = null,
    string? Timeframe = null,
    string? ParametersJson = null,
    string Actor = "User",
    string CorrelationId = "") : IRequest<Guid>;

public class StartStrategyCommandHandler(IStrategyInstanceManager manager) : IRequestHandler<StartStrategyCommand, Guid>
{
    public async Task<Guid> Handle(StartStrategyCommand request, CancellationToken ct)
        => await manager.StartAsync(request.InstanceId, ct);
}

public class PauseStrategyCommandHandler(IStrategyInstanceManager manager) : IRequestHandler<PauseStrategyCommand, bool>
{
    public async Task<bool> Handle(PauseStrategyCommand request, CancellationToken ct)
    {
        await manager.PauseAsync(request.InstanceId, request.Reason ?? "USER_REQUESTED", ct);
        return true;
    }
}

public class StopStrategyCommandHandler(IStrategyInstanceManager manager) : IRequestHandler<StopStrategyCommand, bool>
{
    public async Task<bool> Handle(StopStrategyCommand request, CancellationToken ct)
    {
        await manager.StopAsync(request.InstanceId, request.Reason ?? "USER_REQUESTED", ct);
        return true;
    }
}

public class DeleteStrategyInstanceCommandHandler(IStrategyInstanceRepository repo, IAuditService audit) : IRequestHandler<DeleteStrategyInstanceCommand, bool>
{
    public async Task<bool> Handle(DeleteStrategyInstanceCommand request, CancellationToken ct)
    {
        await repo.DeleteAsync(request.Id, ct);
        await audit.LogAsync("STRATEGY_DELETED", "User", "StrategyInstance", request.Id.ToString(), null, request.Id.ToString(), ct);
        return true;
    }
}

public class CreateStrategyInstanceCommandHandler(
    IStrategyInstanceRepository repo,
    IAuditService audit,
    Domain.Interfaces.IClock clock) : IRequestHandler<CreateStrategyInstanceCommand, Guid>
{
    public async Task<Guid> Handle(CreateStrategyInstanceCommand request, CancellationToken ct)
    {
        if (!Enum.TryParse<StrategyMode>(request.Mode, true, out var mode))
            throw new ArgumentException($"Invalid strategy mode: {request.Mode}");

        var instance = Domain.Entities.StrategyInstance.Create(
            name: request.Name,
            strategyType: request.StrategyType,
            watchlistId: request.WatchlistId,
            mode: mode,
            brokerName: request.BrokerName,
            createdBy: request.Actor,
            createdAt: clock.NowInstant(),
            internalSymbol: request.InternalSymbol,
            timeframe: request.Timeframe,
            configJson: request.ConfigJson,
            failureBehaviorJson: request.FailureBehaviorJson,
            riskProfileId: request.RiskProfileId,
            scheduleJson: request.ScheduleJson,
            parametersJson: request.ParametersJson);

        await repo.AddAsync(instance, ct);
        await audit.LogAsync("STRATEGY_CREATED", request.Actor, "StrategyInstance", instance.Id.ToString(),
            new { instance.Name, instance.StrategyType }, request.CorrelationId, ct);

        return instance.Id;
    }
}
