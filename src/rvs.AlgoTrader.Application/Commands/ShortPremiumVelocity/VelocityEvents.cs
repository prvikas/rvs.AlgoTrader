using MediatR;
using NodaTime;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Application.Commands.ShortPremiumVelocity;

/// <summary>
/// Fired when the hedge engine determines that a mandatory or conditional hedge
/// must be placed immediately. Handlers in Infrastructure place the orders.
/// </summary>
public record HedgeActionRequiredEvent(
    string       UnderlyingSymbol,
    HedgeType    RequiredHedgeType,
    string       Reason,
    MarketRegime Regime,
    Instant      OccurredAt) : INotification;

/// <summary>
/// Fired when the jump-risk monitor detects an intraday vol spike that exceeds
/// the soft-stop threshold. Strategy engine reduces new-position size on receipt.
/// </summary>
public record JumpRiskSoftStopEvent(
    string  UnderlyingSymbol,
    decimal CurrentVolRatio,
    Instant OccurredAt) : INotification;

/// <summary>
/// Fired when an open position's unrealised loss exceeds the configured
/// stop-loss multiplier of collected premium. Handler closes or rolls the position.
/// </summary>
public record StopLossTriggerEvent(
    Guid    PositionId,
    decimal UnrealisedLossPct,
    Instant OccurredAt) : INotification;

/// <summary>
/// Periodic telemetry snapshot published at session end (or on every bar in forward/live mode).
/// Consumed by monitoring handlers that persist to the signal journal and push to the
/// real-time dashboard SignalR hub.
///
/// OdScore:             Opportunity density score (0–100) from IVelocityIndicator.
/// HedgeCoverageRatio:  GammaHedged / GrossGamma ratio. 1.0 = fully hedged; 0 = unhedged.
/// CircuitBreakerState: String representation of CircuitBreakerStateValue.
/// MarginUtilization:   Net-shocked margin utilisation as a fraction (0–1).
/// </summary>
public record StrategySnapshotEvent(
    Guid         StrategyInstanceId,
    Guid         ScenarioId,
    MarketRegime Regime,
    decimal      RegimeStability,
    decimal      OdScore,
    decimal      DailyGrossPnl,
    decimal      HedgeNetCost,
    decimal      HedgeCoverageRatio,
    string       CircuitBreakerState,
    decimal      MarginUtilization,
    Instant      OccurredAt) : INotification;
