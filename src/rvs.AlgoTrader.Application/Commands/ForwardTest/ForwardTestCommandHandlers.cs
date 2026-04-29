using MediatR;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.ForwardTest;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Application.Commands.ForwardTest;

// ─────────────────────────────────────────────────────────────────────────────
// Promote Backtest → ForwardTest
// Creates a new Forward-mode strategy instance pre-filled from a backtest result
// ─────────────────────────────────────────────────────────────────────────────

public class PromoteBacktestToForwardTestHandler(
    IBacktestRunRepository backtests,
    IStrategyInstanceRepository strategies,
    IForwardTestSessionRepository sessions,
    IAuditService audit,
    Domain.Interfaces.IClock clock)
    : IRequestHandler<PromoteBacktestToForwardTestCommand, string>
{
    public async Task<string> Handle(PromoteBacktestToForwardTestCommand request, CancellationToken ct)
    {
        // Resolve the source backtest
        if (!Guid.TryParse(request.BacktestId, out var backtestGuid))
            throw new ArgumentException($"Invalid backtest ID: {request.BacktestId}");

        var bt = await backtests.GetByIdAsync(backtestGuid, ct)
            ?? throw new InvalidOperationException($"Backtest {request.BacktestId} not found.");

        var now = clock.NowInstant();

        // Create a new Forward-mode strategy instance carrying over strategy/symbol/params
        var instance = Domain.Entities.StrategyInstance.Create(
            name: request.InstanceName,
            strategyType: bt.StrategyName,
            watchlistId: null,
            mode: StrategyMode.Forward,
            brokerName: request.BrokerName,
            createdBy: request.Actor,
            createdAt: now,
            internalSymbol: bt.Symbol,
            timeframe: bt.Timeframe,
            configJson: null,
            failureBehaviorJson: null,
            riskProfileId: null,
            scheduleJson: request.ScheduleJson,
            parametersJson: null); // Backtest params not stored on BacktestResultDto; user may override

        instance.AllocatedCapital     = request.InitialCapital;
        // Link back to the UI scenario so ScenariosTab can stop/promote via scenario ID.
        instance.DefinitionScenarioId = bt.ScenarioId;

        await strategies.AddAsync(instance, ct);

        // Create a linked forward test session (will be populated by the ForwardTestEngine on start)
        var session = new Domain.Entities.ForwardTestSession
        {
            Id = Guid.NewGuid(),
            StrategyInstanceId = instance.Id,
            StartedAt = now,
            InitialCapital = request.InitialCapital,
            FinalCapital = request.InitialCapital,
            FinalPnl = 0m,
            TradeCount = 0,
            WinRate = 0m,
            MaxDrawdown = 0m,
            SharpeRatio = 0m,
            SourceBacktestId = backtestGuid,
            Status = "Pending",
        };
        await sessions.AddAsync(session, ct);

        await audit.LogAsync("FORWARD_TEST_PROMOTED_FROM_BACKTEST", request.Actor,
            "StrategyInstance", instance.Id.ToString(),
            new { SourceBacktestId = request.BacktestId, instance.Name },
            request.CorrelationId, ct);

        return instance.Id.ToString();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Promote ForwardTest → Live
// Runs 7 pre-flight checks, then creates a Live-mode strategy instance
// ─────────────────────────────────────────────────────────────────────────────

public class PromoteForwardTestToLiveHandler(
    IForwardTestSessionRepository sessions,
    IStrategyInstanceRepository strategies,
    IKillSwitchService killSwitch,
    IAppBrokerSessionManager brokerSessions,
    IAuditService audit,
    Domain.Interfaces.IClock clock)
    : IRequestHandler<PromoteForwardTestToLiveCommand, PromoteToLiveResultDto>
{
    public async Task<PromoteToLiveResultDto> Handle(
        PromoteForwardTestToLiveCommand request, CancellationToken ct)
    {
        if (!Guid.TryParse(request.ForwardTestInstanceId, out var instanceId))
            return Fail("Invalid instance ID format.", []);

        // Load the forward test strategy instance
        var instance = await strategies.GetByIdAsync(instanceId, ct);
        if (instance is null)
            return Fail($"Strategy instance {request.ForwardTestInstanceId} not found.", []);

        // Load the most recent session for this instance
        var instanceSessions = await sessions.GetByInstanceAsync(instanceId, ct);
        var session = instanceSessions.OrderByDescending(s => s.StartedAt).FirstOrDefault();

        // ── Pre-flight checks ─────────────────────────────────────────────────
        var checks = new List<PreFlightCheckDto>();

        // 1. Kill switch must NOT be active
        var ksActive = await killSwitch.IsActiveAsync(ct);
        checks.Add(new PreFlightCheckDto(
            "Kill switch is inactive",
            !ksActive,
            ksActive ? "Kill switch is currently active — deactivate it before going live." : null));

        // 2. Broker must be authenticated
        var brokerOk = await brokerSessions.IsAuthenticatedAsync(request.BrokerName, ct);
        checks.Add(new PreFlightCheckDto(
            $"Broker '{request.BrokerName}' is authenticated",
            brokerOk,
            brokerOk ? null : $"Broker '{request.BrokerName}' is not connected. Please log in first."));

        // 3. Forward test session must be Stopped or Completed (not still Running)
        var sessionStopped = session is not null &&
            (string.Equals(session.Status, "Stopped", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(session.Status, "Completed", StringComparison.OrdinalIgnoreCase));
        checks.Add(new PreFlightCheckDto(
            "Forward test is stopped or completed",
            sessionStopped,
            sessionStopped ? null : "Forward test must be stopped or completed before promoting to live."));

        // 4. Must have run at least 5 trades
        var minTrades = (session?.TradeCount ?? 0) >= 5;
        checks.Add(new PreFlightCheckDto(
            "Minimum 5 trades completed",
            minTrades,
            minTrades ? null : $"Only {session?.TradeCount ?? 0} trades recorded. Need at least 5 to assess strategy quality."));

        // 5. Win rate >= 40%
        var winRateOk = (session?.WinRate ?? 0m) >= 0.40m;
        checks.Add(new PreFlightCheckDto(
            "Win rate ≥ 40%",
            winRateOk,
            winRateOk ? null : $"Win rate is {(session?.WinRate ?? 0m) * 100:F1}% — below the 40% minimum threshold."));

        // 6. Max drawdown < 25%
        var ddOk = (session?.MaxDrawdown ?? 1m) < 0.25m;
        checks.Add(new PreFlightCheckDto(
            "Max drawdown < 25%",
            ddOk,
            ddOk ? null : $"Max drawdown is {(session?.MaxDrawdown ?? 0m) * 100:F1}% — exceeds 25% threshold."));

        // 7. Sufficient allocated capital (basic check: > 0)
        var capitalOk = request.AllocatedCapital > 0;
        checks.Add(new PreFlightCheckDto(
            "Allocated capital > 0",
            capitalOk,
            capitalOk ? null : "Allocated capital must be greater than zero."));

        if (checks.Any(c => !c.Passed))
            return new PromoteToLiveResultDto(false, null, checks, "One or more pre-flight checks failed.");

        // ── All checks passed — create Live strategy instance ─────────────────
        var now = clock.NowInstant();
        var liveInstance = Domain.Entities.StrategyInstance.Create(
            name: $"{instance.Name} [Live]",
            strategyType: instance.StrategyType,
            watchlistId: null,
            mode: StrategyMode.Live,
            brokerName: request.BrokerName,
            createdBy: request.Actor,
            createdAt: now,
            internalSymbol: instance.InternalSymbol,
            timeframe: instance.Timeframe,
            configJson: null,
            failureBehaviorJson: instance.FailureBehaviorJson,
            riskProfileId: null,
            scheduleJson: request.ScheduleJson ?? instance.ScheduleJson,
            parametersJson: instance.ParametersJson);

        liveInstance.AllocatedCapital     = request.AllocatedCapital;
        // Propagate scenario link so the ScenariosTab can track the live instance too.
        liveInstance.DefinitionScenarioId = instance.DefinitionScenarioId;

        await strategies.AddAsync(liveInstance, ct);

        await audit.LogAsync("FORWARD_TEST_PROMOTED_TO_LIVE", request.Actor,
            "StrategyInstance", liveInstance.Id.ToString(),
            new { SourceForwardTestInstanceId = request.ForwardTestInstanceId, liveInstance.Name },
            request.CorrelationId, ct);

        return new PromoteToLiveResultDto(true, liveInstance.Id.ToString(), checks, null);
    }

    private static PromoteToLiveResultDto Fail(string error, IReadOnlyList<PreFlightCheckDto> checks)
        => new(false, null, checks, error);
}
