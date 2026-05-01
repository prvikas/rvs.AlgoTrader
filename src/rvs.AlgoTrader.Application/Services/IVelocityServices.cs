using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Application.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Short Premium Velocity — Application-layer service interfaces.
// All implementations live in Infrastructure or Strategies.
// Grouped in one file to keep the Application layer clean;
// one interface per logical concern matches the ISP (Interface Segregation Principle).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Hard-gate + soft-filter screener.
/// Evaluates whether conditions allow a new SPV position to be opened:
/// hard gates (must all pass) and soft violations (advisory caps).
/// </summary>
public interface IVelocityScreener
{
    Task<VelocityScreenResult> ScreenAsync(
        StrategyContext ctx,
        VelocityRegimeState regime,
        ShortPremiumVelocityConfig config,
        CancellationToken ct);
}

/// <summary>
/// Computes the composite Velocity Score and Opportunity Density for the current bar.
/// Drives aggression multiplier and structure hint.
/// </summary>
public interface IVelocityIndicator
{
    Task<VelocityScoreResult> ScoreAsync(
        StrategyContext ctx,
        VelocityRegimeState regime,
        ShortPremiumVelocityConfig config,
        CancellationToken ct);
}

/// <summary>
/// Selects the optimal spread structure (straddle, strangle, iron condor, etc.)
/// based on regime and velocity score. Pure function — no I/O.
/// </summary>
public interface IStructureSelector
{
    StructureChoice SelectStructure(
        VelocityRegimeState regime,
        VelocityScoreResult score,
        ShortPremiumVelocityConfig config);
}

/// <summary>
/// Determines the optimal DTE bucket and validates it against hard gates
/// (min liquidity DTE, results-season restrictions, regime-specific weights).
/// </summary>
public interface IDteOptimizer
{
    DteSelection OptimizeDte(
        VelocityRegimeState regime,
        StructureChoice structure,
        StrategyContext ctx,
        ShortPremiumVelocityConfig config);
}

/// <summary>
/// Evaluates whether an open position should be rolled (new expiry), closed early,
/// or held — using DTE thresholds, profit-target fractions, and blocking gates.
/// </summary>
public interface IRollDecisionEngine
{
    Task<RollDecision> EvaluateAsync(
        VelocityPosition position,
        VelocityRegimeState regime,
        StrategyContext ctx,
        ShortPremiumVelocityConfig config,
        CancellationToken ct);
}

/// <summary>
/// Manages the complete hedge lifecycle: mandatory book-level hedges,
/// conditional per-position hedges, roll/close of paired hedges.
/// Implementations place real or simulated orders via IOrderManager.
/// </summary>
public interface IHedgeEngine
{
    /// <summary>Places mandatory portfolio-level hedges when Greeks breach thresholds.</summary>
    Task ExecuteMandatoryHedgesAsync(
        VelocityPortfolioGreeks greeks,
        VelocityRegimeState regime,
        ShortPremiumVelocityConfig config,
        CancellationToken ct);

    /// <summary>Adds conditional hedges for a specific position (e.g. on gamma/DTE trigger).</summary>
    Task AddConditionalHedgesAsync(
        VelocityPosition position,
        VelocityRegimeState regime,
        ShortPremiumVelocityConfig config,
        CancellationToken ct);

    /// <summary>Closes or rolls hedge legs whose paired short-premium leg was exited.</summary>
    Task CloseOrRollPairedHedgesAsync(
        VelocityPosition position,
        CancellationToken ct);

    /// <summary>Rolls all active hedge positions that are approaching expiry.</summary>
    Task RollHedgesAsync(
        VelocityRegimeState regime,
        ShortPremiumVelocityConfig config,
        CancellationToken ct);
}

/// <summary>
/// Evaluates whether the current portfolio Greeks require additional hedging,
/// publishing <see cref="Commands.ShortPremiumVelocity.HedgeActionRequiredEvent"/> when thresholds are breached.
/// Runs on every tick / bar close; is the entry point for the hedge decision loop.
/// </summary>
public interface IHedgeEvaluator
{
    Task EvaluatePortfolioHedgeNeedAsync(
        VelocityRegimeState regime,
        ShortPremiumVelocityConfig config,
        CancellationToken ct);
}

/// <summary>
/// Manages post-drawdown recovery: computes the recovery position-size multiplier
/// and evaluates step-up eligibility as performance recovers.
/// </summary>
public interface IRecoveryManager
{
    /// <summary>Returns the sizing multiplier (0.5–1.25) to apply during recovery mode.</summary>
    decimal GetRecoveryMultiplier(
        VelocityRegimeState regime,
        ShortPremiumVelocityConfig config);

    /// <summary>Returns true when the strategy is eligible to step up to the next recovery stage.</summary>
    Task<bool> EvaluateStepUpAsync(
        VelocityRegimeState regime,
        ShortPremiumVelocityConfig config,
        CancellationToken ct);
}

/// <summary>
/// Intraday circuit breaker: soft stop (reduce size) and hard stop (no new positions)
/// based on daily realised loss percentage against configured thresholds.
/// </summary>
public interface ICircuitBreakerService
{
    /// <summary>Current circuit-breaker state. Updated by <see cref="EvaluateAsync"/>.</summary>
    CircuitBreakerState CurrentState { get; }

    /// <summary>
    /// Evaluates the daily loss percentage against SoftStop / HardStop thresholds.
    /// Publishes a <see cref="Commands.ShortPremiumVelocity.JumpRiskSoftStopEvent"/> when the soft threshold is crossed.
    /// </summary>
    Task EvaluateAsync(
        decimal dailyLossPct,
        ShortPremiumVelocityConfig config,
        CancellationToken ct);
}

/// <summary>
/// Tracks broker-reported margin usage and can trim the position book to fit
/// within the shocked utilisation hard cap.
/// </summary>
public interface IMarginManager
{
    /// <summary>Fetches and returns the current margin state from the broker (cached for freshness window).</summary>
    Task<MarginState> GetCurrentStateAsync(CancellationToken ct);

    /// <summary>
    /// Trims the lowest-priority positions until net-shocked utilisation falls to
    /// <see cref="ShortPremiumVelocityConfig.TrimToFitTargetPercent"/>.
    /// Returns true when positions were trimmed, false when margin is already within limits.
    /// </summary>
    Task<bool> TrimToFitAsync(
        ShortPremiumVelocityConfig config,
        CancellationToken ct);
}

/// <summary>
/// Monitors intraday realised-vol spikes via the broker WebSocket feed.
/// Event-driven: no polling. Publishes <see cref="Commands.ShortPremiumVelocity.JumpRiskSoftStopEvent"/>
/// when a spike exceeds <see cref="ShortPremiumVelocityConfig.JumpRiskSpikeStdDevMultiplier"/> standard deviations.
/// </summary>
public interface IJumpRiskMonitor
{
    /// <summary>Current jump-risk state (updated on every WebSocket tick).</summary>
    JumpRiskState CurrentState { get; }

    // Event-driven — no polling methods. Wire to IBrokerWebSocket via DI.
}

/// <summary>
/// Compounds net session P&amp;L back into the capital pool (after fees, slippage, and
/// hedge costs) using the configured friction buffer and leakage alert thresholds.
/// </summary>
public interface IReinvestmentEngine
{
    Task ProcessSessionCloseAsync(
        decimal grossPnl,
        decimal fees,
        decimal slippage,
        decimal hedgeNetCost,
        ShortPremiumVelocityConfig config,
        CancellationToken ct);
}

/// <summary>
/// Black-Scholes synthetic option pricer with skew and slippage overlays.
/// Used for backtesting (no live chain available) and pre-trade fair-value estimates.
/// Pure function — no I/O.
/// </summary>
public interface ISyntheticOptionsPricer
{
    SyntheticOptionPrice Price(
        string underlying,
        decimal spot,
        decimal strike,
        int dte,
        Domain.Enums.OptionType optionType,
        decimal iv,
        ShortPremiumVelocityConfig config);
}
