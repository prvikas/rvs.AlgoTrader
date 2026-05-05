using MediatR;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Commands.Strategy;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

/// <summary>
/// Intraday circuit-breaker state machine: Normal → SoftStop → HardStop.
/// Evaluated on every tick via EvaluateAsync(dailyLossPct, config, ct).
///
/// SoftStop trigger: dailyLossPct ≥ SoftStopLossPct (default 1.5%).
///   Effects: 50% size cap (RecoveryManager), AggressionMultiplier ≤ 1.0 (VelocityIndicator),
///   no new naked-short entries, continue mandatory hedge rolls, no new VegaHedge.
///
/// HardStop trigger: dailyLossPct ≥ HardStopLossPct (default 2.5%)
///   OR MarginManager forced-liquidation risk.
///   Effects: freeze new entries and discretionary rolls; allow risk-reducing exits,
///   forced Trim-to-Fit, mandatory hedge rolls.
///   PUBLISHES ActivateKillSwitchCommand (existing) via MediatR — no duplicate mechanism.
///
/// Reset: next trading day when MarginState.IsFresh AND JumpRisk.IsSoftStop==false
///   AND regime is NOT Panic/HighVolExpansion. ResetEligibleAt = next trading day open.
/// </summary>
public sealed class CircuitBreakerService(
    IPublisher                      publisher,
    IClock                          clock,
    IJumpRiskMonitor                jumpRisk,
    ILogger<CircuitBreakerService>  log)
    : ICircuitBreakerService
{
    // NSE IST offset
    private static readonly DateTimeZone Ist =
        DateTimeZone.ForOffset(Offset.FromHoursAndMinutes(5, 30));

    // In-memory state — persisted to DB in Phase 3 migration table
    private CircuitBreakerStateValue _state = CircuitBreakerStateValue.Normal;
    private Instant?                 _triggeredAt;
    private Instant?                 _resetEligibleAt;

    public CircuitBreakerState CurrentState => new(
        State:           _state,
        DailyLossPercent: _lastDailyLossPct,
        TriggeredAt:      _triggeredAt,
        ResetEligibleAt: _resetEligibleAt);

    private decimal _lastDailyLossPct;

    public async Task EvaluateAsync(
        decimal                    dailyLossPct,
        ShortPremiumVelocityConfig config,
        CancellationToken          ct)
    {
        _lastDailyLossPct = dailyLossPct;
        var now = clock.NowInstant();

        // ── HardStop trigger ──────────────────────────────────────────────────
        if (dailyLossPct >= config.HardStopLossPct && _state != CircuitBreakerStateValue.HardStop)
        {
            _state           = CircuitBreakerStateValue.HardStop;
            _triggeredAt     = now;
            _resetEligibleAt = NextTradingDayOpen(now);

            log.LogCritical(
                "CircuitBreaker: HARD STOP triggered — dailyLoss={Loss:P2} ≥ HardCap={Cap:P2} at {At}",
                dailyLossPct, config.HardStopLossPct, now);

            // Publish existing kill-switch command — no new mechanism
            await publisher.Publish(
                new ActivateKillSwitchCommand(
                    Actor:         "CircuitBreakerService",
                    Reason:        $"SPV HardStop: dailyLoss={dailyLossPct:P2} ≥ {config.HardStopLossPct:P2}",
                    CorrelationId: Guid.NewGuid().ToString()),
                ct);
            return;
        }

        // ── SoftStop trigger ──────────────────────────────────────────────────
        if (dailyLossPct >= config.SoftStopLossPct && _state == CircuitBreakerStateValue.Normal)
        {
            _state       = CircuitBreakerStateValue.SoftStop;
            _triggeredAt = now;
            _resetEligibleAt = NextTradingDayOpen(now);

            log.LogWarning(
                "CircuitBreaker: SOFT STOP triggered — dailyLoss={Loss:P2} ≥ SoftCap={Cap:P2} at {At}",
                dailyLossPct, config.SoftStopLossPct, now);
            return;
        }

        // Reset is handled by TryResetAsync, called by SPV strategy after margin + regime are known.
    }

    /// <summary>
    /// Resets the circuit breaker to Normal when ALL three conditions hold:
    ///   1. ResetEligibleAt has passed (next trading day).
    ///   2. MarginState.IsFresh — broker margin data is current.
    ///   3. JumpRisk is not in soft-stop.
    ///   4. Regime is not Panic or HighVolExpansion (would re-trigger immediately).
    /// </summary>
    public Task TryResetAsync(
        MarginState         marginState,
        VelocityRegimeState regime,
        CancellationToken   ct)
    {
        if (_state == CircuitBreakerStateValue.Normal) return Task.CompletedTask;
        if (!_resetEligibleAt.HasValue)                return Task.CompletedTask;

        var now = clock.NowInstant();
        if (now < _resetEligibleAt.Value)              return Task.CompletedTask;

        bool jumpRiskClear = !jumpRisk.CurrentState.IsSoftStop;
        bool marginFresh   = marginState.IsFresh;
        bool regimeSafe    = regime.Label is not (MarketRegime.VelocityPanic
                                                or MarketRegime.VelocityHighVolExpansion);

        if (jumpRiskClear && marginFresh && regimeSafe)
        {
            log.LogInformation(
                "CircuitBreaker: resetting to Normal from {PriorState} — " +
                "marginFresh={MF} jumpRiskClear={JR} regime={R} at {At}",
                _state, marginFresh, jumpRiskClear, regime.Label, now);
            _state           = CircuitBreakerStateValue.Normal;
            _triggeredAt     = null;
            _resetEligibleAt = null;
        }
        else
        {
            log.LogDebug(
                "CircuitBreaker: reset deferred — jumpRiskClear={J} marginFresh={M} regimeSafe={R}",
                jumpRiskClear, marginFresh, regimeSafe);
        }

        return Task.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Instant NextTradingDayOpen(Instant now)
    {
        // Next trading day at 09:15 IST
        var nowInIst = now.InZone(Ist);
        var nextDay  = nowInIst.Date.PlusDays(1);
        // Skip weekends (simple; production uses MarketCalendarService)
        if (nextDay.DayOfWeek == IsoDayOfWeek.Saturday)  nextDay = nextDay.PlusDays(2);
        if (nextDay.DayOfWeek == IsoDayOfWeek.Sunday)    nextDay = nextDay.PlusDays(1);
        var openTime = nextDay.At(new LocalTime(9, 15));
        return openTime.InZoneLeniently(Ist).ToInstant();
    }
}
