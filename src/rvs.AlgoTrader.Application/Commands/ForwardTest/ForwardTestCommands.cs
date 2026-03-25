using MediatR;
using rvs.AlgoTrader.Application.DTOs.ForwardTest;

namespace rvs.AlgoTrader.Application.Commands.ForwardTest;

/// <summary>
/// Promote a completed backtest result to a new ForwardTest-mode strategy instance.
/// Carries strategy type, symbol, timeframe, and parameters from the source backtest.
/// Returns the new strategy instance ID.
/// </summary>
public record PromoteBacktestToForwardTestCommand(
    string BacktestId,
    string InstanceName,
    string BrokerName,
    decimal InitialCapital,
    string? ScheduleJson,
    string Actor = "User",
    string CorrelationId = "") : IRequest<string>;

/// <summary>
/// Promote a stopped/completed forward test to a Live mode strategy instance.
/// Runs 7 pre-flight checks; returns the new instance ID + check results.
/// </summary>
public record PromoteForwardTestToLiveCommand(
    string ForwardTestInstanceId,
    string BrokerName,
    decimal AllocatedCapital,
    string? ScheduleJson,
    string Actor = "User",
    string CorrelationId = "") : IRequest<PromoteToLiveResultDto>;
