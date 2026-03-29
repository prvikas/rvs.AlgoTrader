using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;
using rvs.AlgoTrader.Application.Commands.Strategy;
using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.Strategy;
using rvs.AlgoTrader.Application.Queries.Backtest;
using rvs.AlgoTrader.Application.Queries.Strategy;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StrategiesController(IMediator mediator, IStrategyFactory strategyFactory) : ControllerBase
{

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StrategyInstanceDto>>>> GetAll(CancellationToken ct)
    {
        var result = await mediator.Send(new GetAllStrategyInstancesQuery(), ct);
        return Ok(ApiResponse<IReadOnlyList<StrategyInstanceDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<StrategyInstanceDto>>> GetById(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetStrategyInstanceByIdQuery(id), ct);
        if (result == null) return NotFound(ApiResponse<object>.Fail("Strategy instance not found"));
        return Ok(ApiResponse<StrategyInstanceDto>.Ok(result));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<Guid>>> Create(
        [FromBody] CreateStrategyInstanceCommand command, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = result },
            ApiResponse<Guid>.Ok(result));
    }

    [HttpPost("{id:guid}/start")]
    public async Task<ActionResult<ApiResponse<Guid>>> Start(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new StartStrategyCommand(id), ct);
        return Ok(ApiResponse<Guid>.Ok(result));
    }

    [HttpPost("{id:guid}/pause")]
    public async Task<ActionResult<ApiResponse<bool>>> Pause(
        Guid id, [FromBody] PauseStrategyCommand command, CancellationToken ct)
    {
        await mediator.Send(command with { InstanceId = id }, ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpPost("{id:guid}/stop")]
    public async Task<ActionResult<ApiResponse<bool>>> Stop(
        Guid id, [FromBody] StopStrategyCommand command, CancellationToken ct)
    {
        await mediator.Send(command with { InstanceId = id }, ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("{id:guid}/signals")]
    public async Task<ActionResult<ApiResponse<PagedResult<SignalJournalEntryDto>>>> GetSignals(
        Guid id, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetSignalJournalQuery(id, null, null, null, 1, limit), ct);
        return Ok(ApiResponse<PagedResult<SignalJournalEntryDto>>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Update(
        Guid id, [FromBody] UpdateStrategyInstanceCommand command, CancellationToken ct)
    {
        await mediator.Send(command with { Id = id }, ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id, CancellationToken ct)
    {
        await mediator.Send(new DeleteStrategyInstanceCommand(id), ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>
    /// Returns the full parameter schema for a strategy — keys, labels, types, defaults, constraints.
    /// The frontend uses this to render the parameter editor dynamically.
    /// This is the single source of truth: schema lives in the strategy's Config class.
    /// </summary>
    [HttpGet("schema/{strategyName}")]
    public ActionResult<ApiResponse<IReadOnlyList<StrategyParamDef>>> GetSchema(string strategyName)
    {
        try
        {
            var schema = strategyFactory.GetParameterSchema(strategyName);
            return Ok(ApiResponse<IReadOnlyList<StrategyParamDef>>.Ok(schema));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<object>.Fail(ex.Message));
        }
    }

    /// <summary>Returns the list of registered strategy names.</summary>
    [HttpGet("registered")]
    public ActionResult<ApiResponse<IEnumerable<string>>> GetRegisteredNames()
        => Ok(ApiResponse<IEnumerable<string>>.Ok(strategyFactory.GetRegisteredNames()));

    // ── Scenarios ─────────────────────────────────────────────────────────────

    /// <summary>List all scenarios for a strategy instance.</summary>
    [HttpGet("{instanceId:guid}/scenarios")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StrategyScenarioDto>>>> GetScenarios(
        Guid instanceId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetScenariosQuery(instanceId), ct);
        return Ok(ApiResponse<IReadOnlyList<StrategyScenarioDto>>.Ok(result));
    }

    /// <summary>Get a single scenario by ID.</summary>
    [HttpGet("{instanceId:guid}/scenarios/{scenarioId:guid}")]
    public async Task<ActionResult<ApiResponse<StrategyScenarioDto>>> GetScenario(
        Guid instanceId, Guid scenarioId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetScenarioByIdQuery(scenarioId), ct);
        if (result == null || result.StrategyInstanceId != instanceId)
            return NotFound(ApiResponse<object>.Fail("Scenario not found"));
        return Ok(ApiResponse<StrategyScenarioDto>.Ok(result));
    }

    /// <summary>Get comparison grid — all scenarios with their latest backtest metrics.</summary>
    [HttpGet("{instanceId:guid}/scenarios/compare")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ScenarioComparisonRow>>>> CompareScenarios(
        Guid instanceId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetScenarioComparisonQuery(instanceId), ct);
        return Ok(ApiResponse<IReadOnlyList<ScenarioComparisonRow>>.Ok(result));
    }

    /// <summary>Create a new scenario for a strategy instance.</summary>
    [HttpPost("{instanceId:guid}/scenarios")]
    public async Task<ActionResult<ApiResponse<Guid>>> CreateScenario(
        Guid instanceId, [FromBody] CreateScenarioRequest request, CancellationToken ct)
    {
        var id = await mediator.Send(new CreateScenarioCommand(
            instanceId, request.Name, request.Description, request.ParametersJsonOverride, request.AllocatedCapital), ct);
        return CreatedAtAction(nameof(GetScenario), new { instanceId, scenarioId = id },
            ApiResponse<Guid>.Ok(id));
    }

    /// <summary>Update scenario name/description/override. Resets to Draft if override changes.</summary>
    [HttpPut("{instanceId:guid}/scenarios/{scenarioId:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateScenario(
        Guid instanceId, Guid scenarioId, [FromBody] UpdateScenarioRequest request, CancellationToken ct)
    {
        await mediator.Send(new UpdateScenarioCommand(
            scenarioId, request.Name, request.Description, request.ParametersJsonOverride, request.AllocatedCapital), ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>Delete a scenario. Blocked when status is Live.</summary>
    [HttpDelete("{instanceId:guid}/scenarios/{scenarioId:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteScenario(
        Guid instanceId, Guid scenarioId, CancellationToken ct)
    {
        await mediator.Send(new DeleteScenarioCommand(scenarioId), ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>Promote a scenario to the next lifecycle stage (ForwardTest / Live / Archived).</summary>
    [HttpPost("{instanceId:guid}/scenarios/{scenarioId:guid}/promote")]
    public async Task<ActionResult<ApiResponse<bool>>> PromoteScenario(
        Guid instanceId, Guid scenarioId,
        [FromBody] PromoteScenarioRequest request, CancellationToken ct)
    {
        await mediator.Send(new PromoteScenarioCommand(scenarioId, request.TargetStatus), ct);
        // TargetStatus is now ScenarioStatus enum — invalid values return 400 at deserialization
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>List all backtest runs for a specific scenario (paginated).</summary>
    [HttpGet("{instanceId:guid}/scenarios/{scenarioId:guid}/backtests")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BacktestResultDto>>>> GetScenarioBacktests(
        Guid instanceId, Guid scenarioId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new GetBacktestsByScenarioQuery(scenarioId, page, Math.Clamp(pageSize, 1, 200)), ct);
        return Ok(ApiResponse<IReadOnlyList<BacktestResultDto>>.Ok(result));
    }

    /// <summary>
    /// Run all (or selected) scenarios for a strategy instance in parallel.
    /// Returns a list of {scenarioId, jobId} — subscribe to each jobId via BacktestHub.
    /// </summary>
    [HttpPost("{instanceId:guid}/scenarios/run")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<object>>>> RunScenarios(
        Guid instanceId, [FromBody] RunScenariosApiRequest request, CancellationToken ct)
    {
        var fromDate = LocalDatePattern.Iso.Parse(request.FromDate).Value;
        var toDate   = LocalDatePattern.Iso.Parse(request.ToDate).Value;

        var results = await mediator.Send(new RunScenariosCommand(
            StrategyInstanceId: instanceId,
            ScenarioIds:        request.ScenarioIds,
            InternalSymbol:     request.InternalSymbol,
            Timeframe:          request.Timeframe,
            FromDate:           fromDate,
            ToDate:             toDate,
            InitialCapital:     request.InitialCapital,
            RiskPerTradePercent: request.RiskPerTradePercent), ct);

        var response = results
            .Select(r => (object)new { scenarioId = r.ScenarioId, jobId = r.JobId })
            .ToList();
        return Accepted(ApiResponse<IReadOnlyList<object>>.Ok(response));
    }
}

// PromoteScenarioRequest and RunScenariosApiRequest are defined in
// rvs.AlgoTrader.Application.DTOs.Strategy.StrategyScenarioDto.cs
