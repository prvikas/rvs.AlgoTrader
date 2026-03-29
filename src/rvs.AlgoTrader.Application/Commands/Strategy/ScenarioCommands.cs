using MediatR;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.DTOs.Strategy;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Application.Commands.Strategy;

// ── Create ────────────────────────────────────────────────────────────────────

public record CreateScenarioCommand(
    Guid    StrategyInstanceId,
    string  Name,
    string? Description,
    string? ParametersJsonOverride) : IRequest<Guid>;

public class CreateScenarioCommandHandler(
    IStrategyScenarioRepository repo,
    IAuditService audit,
    Domain.Interfaces.IClock clock) : IRequestHandler<CreateScenarioCommand, Guid>
{
    public async Task<Guid> Handle(CreateScenarioCommand req, CancellationToken ct)
    {
        var scenario = Domain.Entities.StrategyScenario.Create(
            req.StrategyInstanceId,
            req.Name,
            req.Description,
            req.ParametersJsonOverride,
            clock.NowInstant());

        await repo.AddAsync(scenario, ct);
        await audit.LogAsync("SCENARIO_CREATED", "User", "StrategyScenario", scenario.Id.ToString(),
            new { scenario.Name, scenario.StrategyInstanceId }, scenario.Id.ToString(), ct);
        return scenario.Id;
    }
}

// ── Update ────────────────────────────────────────────────────────────────────

public record UpdateScenarioCommand(
    Guid    Id,
    string? Name,
    string? Description,
    string? ParametersJsonOverride) : IRequest<bool>;

public class UpdateScenarioCommandHandler(
    IStrategyScenarioRepository repo,
    IAuditService audit,
    Domain.Interfaces.IClock clock) : IRequestHandler<UpdateScenarioCommand, bool>
{
    public async Task<bool> Handle(UpdateScenarioCommand req, CancellationToken ct)
    {
        var scenario = await repo.GetByIdAsync(req.Id, ct)
            ?? throw new KeyNotFoundException($"Scenario {req.Id} not found.");

        if (scenario.Status == ScenarioStatus.Live)
            throw new InvalidOperationException("Cannot edit a Live scenario — archive it first.");

        if (req.Name is not null) scenario.Name = req.Name;
        if (req.Description is not null) scenario.Description = req.Description;
        if (req.ParametersJsonOverride is not null)
        {
            scenario.ParametersJsonOverride = req.ParametersJsonOverride;
            // Reset to Draft when override changes — existing backtest no longer reflects params
            if (scenario.Status == ScenarioStatus.Backtested)
                scenario.Status = ScenarioStatus.Draft;
        }
        scenario.UpdatedAt = clock.NowInstant();

        await repo.UpdateAsync(scenario, ct);
        await audit.LogAsync("SCENARIO_UPDATED", "User", "StrategyScenario", req.Id.ToString(),
            new { req.Name, req.ParametersJsonOverride }, req.Id.ToString(), ct);
        return true;
    }
}

// ── Delete ────────────────────────────────────────────────────────────────────

public record DeleteScenarioCommand(Guid Id) : IRequest<bool>;

public class DeleteScenarioCommandHandler(
    IStrategyScenarioRepository repo,
    IAuditService audit) : IRequestHandler<DeleteScenarioCommand, bool>
{
    public async Task<bool> Handle(DeleteScenarioCommand req, CancellationToken ct)
    {
        var scenario = await repo.GetByIdAsync(req.Id, ct)
            ?? throw new KeyNotFoundException($"Scenario {req.Id} not found.");

        if (scenario.Status == ScenarioStatus.Live)
            throw new InvalidOperationException("Cannot delete a Live scenario — archive it first.");

        await repo.DeleteAsync(req.Id, ct);
        await audit.LogAsync("SCENARIO_DELETED", "User", "StrategyScenario", req.Id.ToString(),
            new { scenario.Name }, req.Id.ToString(), ct);
        return true;
    }
}

// ── Promote ───────────────────────────────────────────────────────────────────

/// <summary>
/// Promote a scenario to the next lifecycle stage.
/// Draft → Backtested: blocked (done automatically after backtest completes).
/// Backtested → ForwardTest: allowed.
/// ForwardTest → Live: allowed.
/// Any → Archived: always allowed.
/// </summary>
public record PromoteScenarioCommand(Guid Id, string TargetStatus) : IRequest<bool>;

public class PromoteScenarioCommandHandler(
    IStrategyScenarioRepository repo,
    IAuditService audit,
    Domain.Interfaces.IClock clock) : IRequestHandler<PromoteScenarioCommand, bool>
{
    public async Task<bool> Handle(PromoteScenarioCommand req, CancellationToken ct)
    {
        var scenario = await repo.GetByIdAsync(req.Id, ct)
            ?? throw new KeyNotFoundException($"Scenario {req.Id} not found.");

        if (!Enum.TryParse<ScenarioStatus>(req.TargetStatus, true, out var target))
            throw new ArgumentException($"Invalid target status: {req.TargetStatus}");

        // Gate: only Backtested scenarios can go to ForwardTest
        if (target == ScenarioStatus.ForwardTest && scenario.Status != ScenarioStatus.Backtested)
            throw new InvalidOperationException(
                "Scenario must be Backtested before promoting to ForwardTest.");

        // Gate: only ForwardTest scenarios can go Live
        if (target == ScenarioStatus.Live && scenario.Status != ScenarioStatus.ForwardTest)
            throw new InvalidOperationException(
                "Scenario must complete ForwardTest before going Live.");

        var previous = scenario.Status.ToString();
        scenario.Status    = target;
        scenario.UpdatedAt = clock.NowInstant();

        await repo.UpdateAsync(scenario, ct);
        await audit.LogAsync("SCENARIO_PROMOTED", "User", "StrategyScenario", req.Id.ToString(),
            new { From = previous, To = req.TargetStatus }, req.Id.ToString(), ct);
        return true;
    }
}

// ── Run all scenarios in parallel ─────────────────────────────────────────────

public record ScenarioJobResult(Guid ScenarioId, string JobId);

public record RunScenariosCommand(
    Guid   StrategyInstanceId,
    IReadOnlyList<Guid>? ScenarioIds,
    string InternalSymbol,
    string Timeframe,
    LocalDate FromDate,
    LocalDate ToDate,
    decimal InitialCapital        = 100_000m,
    decimal RiskPerTradePercent   = 1.0m) : IRequest<IReadOnlyList<ScenarioJobResult>>;

public class RunScenariosCommandHandler(
    IStrategyScenarioRepository scenarioRepo,
    IStrategyInstanceRepository instanceRepo,
    IBacktestJobManager jobManager) : IRequestHandler<RunScenariosCommand, IReadOnlyList<ScenarioJobResult>>
{
    public async Task<IReadOnlyList<ScenarioJobResult>> Handle(RunScenariosCommand req, CancellationToken ct)
    {
        var instance = await instanceRepo.GetByIdAsync(req.StrategyInstanceId, ct)
            ?? throw new KeyNotFoundException($"Strategy instance {req.StrategyInstanceId} not found.");

        var allScenarios = await scenarioRepo.GetByInstanceAsync(req.StrategyInstanceId, ct);

        var targets = req.ScenarioIds?.Count > 0
            ? allScenarios.Where(s => req.ScenarioIds.Contains(s.Id)).ToList()
            : allScenarios.ToList();

        if (targets.Count == 0)
            throw new InvalidOperationException("No scenarios found for the given strategy instance.");

        // Launch all in parallel — each gets an isolated background job
        var tasks = targets.Select(scenario =>
        {
            var effectiveParams = Services.ScenarioParamMerger.Merge(
                instance.ParametersJson,
                scenario.ParametersJsonOverride);

            var backtestRequest = new BacktestRequestDto(
                StrategyName:        instance.StrategyType,
                ParametersJson:      effectiveParams,
                InternalSymbol:      req.InternalSymbol,
                Timeframe:           req.Timeframe,
                FromDate:            req.FromDate,
                ToDate:              req.ToDate,
                InitialCapital:      req.InitialCapital,
                RiskPerTradePercent: req.RiskPerTradePercent);

            return jobManager.EnqueueScenarioAsync(scenario.Id, backtestRequest, ct)
                .ContinueWith(t => new ScenarioJobResult(scenario.Id, t.Result),
                    TaskContinuationOptions.OnlyOnRanToCompletion);
        });

        return await Task.WhenAll(tasks);
    }
}
