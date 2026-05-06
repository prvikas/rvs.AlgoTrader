using MediatR;
using FluentValidation;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.Strategy;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Application.Commands.Strategy;

// ── Consolidated lifecycle commands (unified internal and external handling) ──

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

public class StartStrategyCommandHandler(
    IStrategyInstanceManager manager,
    IStrategyScenarioRepository scenarioRepo,
    IAuditService audit,
    ICurrentUser currentUser) : IRequestHandler<StartStrategyCommand, Guid>
{
    public async Task<Guid> Handle(StartStrategyCommand request, CancellationToken ct)
    {
        var scenarios = await scenarioRepo.GetByInstanceAsync(request.InstanceId, ct);
        if (scenarios.Count == 0)
            throw new InvalidOperationException(
                "Cannot start a strategy with no scenarios. Create at least one scenario first.");

        var runId = await manager.StartAsync(request.InstanceId, ct);

        var correlationId = Guid.NewGuid().ToString();
        await audit.LogAsync("STRATEGY_STARTED", currentUser.Actor, "StrategyInstance", request.InstanceId.ToString(),
            new { RunId = runId }, correlationId, ct);

        return runId;
    }
}

public class PauseStrategyCommandHandler(
    IStrategyInstanceManager manager,
    IAuditService audit,
    ICurrentUser currentUser) : IRequestHandler<PauseStrategyCommand, bool>
{
    public async Task<bool> Handle(PauseStrategyCommand request, CancellationToken ct)
    {
        await manager.PauseAsync(request.InstanceId, request.Reason ?? "USER_REQUESTED", ct);

        var correlationId = Guid.NewGuid().ToString();
        await audit.LogAsync("STRATEGY_PAUSED", currentUser.Actor, "StrategyInstance", request.InstanceId.ToString(),
            new { Reason = request.Reason }, correlationId, ct);

        return true;
    }
}

public class StopStrategyCommandHandler(
    IStrategyInstanceManager manager,
    IAuditService audit,
    ICurrentUser currentUser) : IRequestHandler<StopStrategyCommand, bool>
{
    public async Task<bool> Handle(StopStrategyCommand request, CancellationToken ct)
    {
        await manager.StopAsync(request.InstanceId, request.Reason ?? "USER_REQUESTED", ct);

        var correlationId = Guid.NewGuid().ToString();
        await audit.LogAsync("STRATEGY_STOPPED", currentUser.Actor, "StrategyInstance", request.InstanceId.ToString(),
            new { Reason = request.Reason }, correlationId, ct);

        return true;
    }
}

public class DeleteStrategyInstanceCommandHandler(IStrategyInstanceRepository repo, IAuditService audit, ICurrentUser currentUser) : IRequestHandler<DeleteStrategyInstanceCommand, bool>
{
    public async Task<bool> Handle(DeleteStrategyInstanceCommand request, CancellationToken ct)
    {
        await repo.DeleteAsync(request.Id, ct);
        await audit.LogAsync("STRATEGY_DELETED", currentUser.Actor, "StrategyInstance", request.Id.ToString(), null, request.Id.ToString(), ct);
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

        if (request.ScheduleJson is not null)
        {
            var schedule = ScheduleConfigDto.Parse(request.ScheduleJson);
            var errors = schedule?.Validate() ?? [];
            if (errors.Count > 0)
                throw new ArgumentException($"Invalid ScheduleJson: {string.Join("; ", errors)}");
        }

        var instance = Domain.Entities.StrategyInstance.Create(
            name: request.Name,
            strategyType: request.StrategyType,
            watchlistId: request.WatchlistId,
            mode: mode,
            brokerAccountId: null,
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

// ── Update command — only allowed when the strategy is NOT running ──────────

/// <summary>
/// Patch editable fields on a strategy instance.
/// All fields are nullable — only non-null fields are applied.
/// Blocked when Status == Running (user must stop/pause first).
/// </summary>
public record UpdateStrategyInstanceCommand(
    Guid Id,
    string? Name = null,
    string? ParametersJson = null,
    string? ConfigJson = null,
    string? BrokerName = null,
    string? InternalSymbol = null,
    string? Timeframe = null,
    string? ScheduleJson = null,
    decimal? AllocatedCapital = null,
    string Actor = "User") : IRequest<bool>;

public class UpdateStrategyInstanceCommandHandler(
    IStrategyInstanceRepository repo,
    IAuditService audit,
    Domain.Interfaces.IClock clock) : IRequestHandler<UpdateStrategyInstanceCommand, bool>
{
    public async Task<bool> Handle(UpdateStrategyInstanceCommand request, CancellationToken ct)
    {
        var instance = await repo.GetByIdAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Strategy instance {request.Id} not found.");

        if (instance.Status == StrategyStatus.Running)
            throw new InvalidOperationException(
                "Cannot edit a running strategy — stop or pause it first.");

        if (request.Name is not null) instance.Name = request.Name;
        if (request.ParametersJson is not null) instance.ParametersJson = request.ParametersJson;
        if (request.ConfigJson is not null) instance.ConfigJson = request.ConfigJson;
        // BrokerName is no longer a property; it's derived from BrokerAccount.Broker
        if (request.InternalSymbol is not null) instance.InternalSymbol = request.InternalSymbol;
        if (request.Timeframe is not null) instance.Timeframe = request.Timeframe;
        if (request.ScheduleJson is not null)
        {
            var schedule = ScheduleConfigDto.Parse(request.ScheduleJson);
            var errors = schedule?.Validate() ?? [];
            if (errors.Count > 0)
                throw new ArgumentException($"Invalid ScheduleJson: {string.Join("; ", errors)}");
            instance.ScheduleJson = request.ScheduleJson;
        }
        if (request.AllocatedCapital.HasValue) instance.AllocatedCapital = request.AllocatedCapital.Value;
        instance.UpdatedAt = clock.NowInstant();

        await repo.UpdateAsync(instance, ct);
        await audit.LogAsync("STRATEGY_UPDATED", request.Actor, "StrategyInstance",
            request.Id.ToString(),
            new { request.Name, request.ParametersJson, request.Timeframe, request.InternalSymbol },
            request.Id.ToString(), ct);
        return true;
    }
}
