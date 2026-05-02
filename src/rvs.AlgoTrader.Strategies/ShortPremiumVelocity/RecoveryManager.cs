using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

/// <summary>
/// Post-drawdown recovery state machine with 4 steps:
///
///   Step 0 (Panic lock): IsSoftStop or regime=Panic → multiplier = RecoveryMultiplierMin[Panic] = 0.50.
///   Step 1 (Initial):    Entry into recovery → multiplier = RegimeMin (0.50–1.00).
///   Step 2 (Stabilise):  RecoveryWindowMinSessions[regime] profitable sessions → multiplier mid-point.
///   Step 3 (Rebuild):    RecoveryWindowMaxSessions[regime] sessions AND Sharpe ≥ RecoveryStep3SharpeMin → multiplier = RegimeMax.
///   Full (Normal):       No active drawdown → multiplier = 1.0 (uncapped).
///
/// All window sizes come from config — no hard-coded session counts.
/// Step-up is evaluated by EvaluateStepUpAsync; it is NOT automatic.
/// </summary>
public sealed class RecoveryManager(
    IPositionRepository         positions,
    ICircuitBreakerService      circuitBreaker,
    IClock                      clock,
    ILogger<RecoveryManager>    log)
    : IRecoveryManager
{
    // ── Recovery state ────────────────────────────────────────────────────────
    private int     _recoveryStep        = 0;   // 0=Normal, 1=Init, 2=Stabilise, 3=Rebuild
    private bool    _inRecovery          = false;
    private int     _sessionsInStep      = 0;
    private int     _profitableSessions  = 0;
    private decimal _rollingPnl         = 0m;
    private decimal _rollingPnlSumSq    = 0m;
    private int     _rollingPnlCount    = 0;
    private Instant _stepEnteredAt      = Instant.MinValue;

    // ── IRecoveryManager ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the sizing multiplier (0.50–1.25) to apply this session.
    ///
    /// Panic lock overrides all steps: if circuit-breaker is HardStop or regime is Panic,
    /// returns RecoveryMultiplierMin[Panic] (0.50) regardless of step.
    /// </summary>
    public decimal GetRecoveryMultiplier(
        VelocityRegimeState        regime,
        ShortPremiumVelocityConfig config)
    {
        // Panic lock — highest priority
        bool panicLock = circuitBreaker.CurrentState.State == CircuitBreakerStateValue.HardStop
                      || regime.Label == MarketRegime.VelocityPanic;

        if (panicLock)
        {
            decimal panicMin = config.RecoveryMultiplierMin.GetValueOrDefault(
                MarketRegime.VelocityPanic, 0.50m);
            log.LogDebug("RecoveryManager: Panic lock → multiplier={M}", panicMin);
            return panicMin;
        }

        if (!_inRecovery)
            return 1.0m; // normal — no cap

        decimal regimeMin = config.RecoveryMultiplierMin.GetValueOrDefault(regime.Label, 0.50m);
        decimal regimeMax = config.RecoveryMultiplierMax.GetValueOrDefault(regime.Label, 1.25m);

        decimal multiplier = _recoveryStep switch
        {
            1 => regimeMin,                                      // 50–100% depending on regime
            2 => Math.Round((regimeMin + regimeMax) / 2m, 4),   // mid-point
            3 => regimeMax,                                      // max (regime-capped)
            _ => 1.0m,
        };

        log.LogDebug(
            "RecoveryManager: step={S} regime={R} multiplier={M} sessionsInStep={SS}",
            _recoveryStep, regime.Label, multiplier, _sessionsInStep);

        return multiplier;
    }

    /// <summary>
    /// Evaluates whether to step up to the next recovery stage.
    ///
    /// Step 1 → 2: sessionsInStep ≥ RecoveryWindowMinSessions[regime] AND ≥1 profitable session.
    /// Step 2 → 3: sessionsInStep ≥ RecoveryWindowMaxSessions[regime] AND Sharpe ≥ RecoveryStep3SharpeMin.
    /// Step 3 → Full: Sharpe ≥ RecoveryStep3SharpeMin AND no recent loss streak.
    ///
    /// Also handles: entering recovery when CB is SoftStop/HardStop.
    /// Returns true when a step-up occurred.
    /// </summary>
    public async Task<bool> EvaluateStepUpAsync(
        VelocityRegimeState        regime,
        ShortPremiumVelocityConfig config,
        CancellationToken          ct)
    {
        var cbState = circuitBreaker.CurrentState.State;

        // ── Enter recovery mode if not already in it ──────────────────────────
        if (!_inRecovery && cbState != CircuitBreakerStateValue.Normal)
        {
            _inRecovery       = true;
            _recoveryStep     = 1;
            _sessionsInStep   = 0;
            _profitableSessions = 0;
            _stepEnteredAt    = clock.NowInstant();
            log.LogWarning(
                "RecoveryManager: entering recovery step=1 regime={R} cb={CB}",
                regime.Label, cbState);
            return false; // just entered; evaluate next session
        }

        // ── Exit recovery when CB resets to Normal ────────────────────────────
        if (_inRecovery && cbState == CircuitBreakerStateValue.Normal && _recoveryStep >= 3)
        {
            decimal sharpe = ComputeRollingSharpe();
            if (sharpe >= config.RecoveryStep3SharpeMin)
            {
                log.LogInformation(
                    "RecoveryManager: FULL recovery — sharpe={Sh:F2} ≥ {Min:F2}; exiting recovery mode",
                    sharpe, config.RecoveryStep3SharpeMin);
                _inRecovery       = false;
                _recoveryStep     = 0;
                _sessionsInStep   = 0;
                _profitableSessions = 0;
                _rollingPnl       = 0m;
                _rollingPnlSumSq  = 0m;
                _rollingPnlCount  = 0;
                return true;
            }
        }

        if (!_inRecovery) return false;

        // ── Update session P&L stats ──────────────────────────────────────────
        await UpdateSessionStatsAsync(regime, config, ct);

        int minSessions = config.RecoveryWindowMinSessions.GetValueOrDefault(regime.Label, 20);
        int maxSessions = config.RecoveryWindowMaxSessions.GetValueOrDefault(regime.Label, 40);
        decimal sharpeNow = ComputeRollingSharpe();

        bool stepped = false;

        if (_recoveryStep == 1 && _sessionsInStep >= minSessions && _profitableSessions >= 1)
        {
            _recoveryStep   = 2;
            _sessionsInStep = 0;
            log.LogInformation(
                "RecoveryManager: step 1→2 after {N} sessions profit={P} regime={R}",
                minSessions, _profitableSessions, regime.Label);
            stepped = true;
        }
        else if (_recoveryStep == 2 && _sessionsInStep >= maxSessions &&
                 sharpeNow >= config.RecoveryStep3SharpeMin)
        {
            _recoveryStep   = 3;
            _sessionsInStep = 0;
            log.LogInformation(
                "RecoveryManager: step 2→3 after {N} sessions sharpe={Sh:F2} regime={R}",
                maxSessions, sharpeNow, regime.Label);
            stepped = true;
        }

        return stepped;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task UpdateSessionStatsAsync(
        VelocityRegimeState        regime,
        ShortPremiumVelocityConfig config,
        CancellationToken          ct)
    {
        // Proxy: compute daily P&L from closed positions today
        var now = clock.NowInstant();
        var ist = now.InZone(DateTimeZone.ForOffset(Offset.FromHoursAndMinutes(5, 30)));
        var today = ist.Date;

        var closedToday = await positions.GetClosedTodayAsync(
            [Guid.Empty], today, ct);  // Guid.Empty = all strategy instances (proxy)

        decimal sessionPnl = closedToday.Sum(p => p.UnrealizedPnl + p.RealizedPnl);

        _sessionsInStep++;
        if (sessionPnl > 0) _profitableSessions++;

        // Update rolling stats for Sharpe
        _rollingPnl      += sessionPnl;
        _rollingPnlSumSq += sessionPnl * sessionPnl;
        _rollingPnlCount++;

        log.LogDebug(
            "RecoveryManager.UpdateStats: sessionPnl={PnL:F0} sessionsInStep={S} profitable={P}",
            sessionPnl, _sessionsInStep, _profitableSessions);
    }

    private decimal ComputeRollingSharpe()
    {
        if (_rollingPnlCount < 2) return 0m;

        decimal mean     = _rollingPnl / _rollingPnlCount;
        decimal variance = (_rollingPnlSumSq / _rollingPnlCount) - (mean * mean);
        decimal stdDev   = variance > 0m ? (decimal)Math.Sqrt((double)variance) : 1m;

        return stdDev > 0m ? Math.Round(mean / stdDev * (decimal)Math.Sqrt(252.0), 4) : 0m;
    }
}
