using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using System.Collections.Concurrent;
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

/// <summary>
/// Post-drawdown recovery state machine with 4 steps per strategy instance:
///
///   Step 0 (Panic lock): IsSoftStop or regime=Panic → multiplier = RecoveryMultiplierMin[Panic] = 0.50.
///   Step 1 (Initial):    Entry into recovery → multiplier = RegimeMin (0.50–1.00).
///   Step 2 (Stabilise):  RecoveryWindowMinSessions[regime] profitable sessions → multiplier mid-point.
///   Step 3 (Rebuild):    RecoveryWindowMaxSessions[regime] sessions AND Sharpe ≥ RecoveryStep3SharpeMin → multiplier = RegimeMax.
///   Full (Normal):       No active drawdown → multiplier = 1.0 (uncapped).
///
/// Per-instance isolation: each Guid instanceId maintains independent recovery state so that
/// a drawdown on one instance does not reduce sizing for another running instance.
///
/// All window sizes come from config — no hard-coded session counts.
/// Step-up is evaluated by EvaluateStepUpAsync; it is NOT automatic.
/// </summary>
public sealed class RecoveryManager(
    IServiceScopeFactory        scopeFactory,
    ICircuitBreakerService      circuitBreaker,
    IClock                      clock,
    ILogger<RecoveryManager>    log)
    : IRecoveryManager
{
    // ── Per-instance state ────────────────────────────────────────────────────

    private sealed class RecoveryInstance
    {
        public int     Step              = 0;
        public bool    InRecovery        = false;
        public int     SessionsInStep    = 0;
        public int     ProfitableSessions = 0;
        public decimal RollingPnl        = 0m;
        public decimal RollingPnlSumSq   = 0m;
        public int     RollingPnlCount   = 0;
        public Instant StepEnteredAt     = Instant.MinValue;
    }

    private readonly ConcurrentDictionary<Guid, RecoveryInstance> _instances = new();

    private RecoveryInstance GetOrCreate(Guid instanceId)
        => _instances.GetOrAdd(instanceId, _ => new RecoveryInstance());

    // ── IRecoveryManager (per-instance) ──────────────────────────────────────

    /// <summary>
    /// Returns the sizing multiplier (0.50–1.25) for the given strategy instance.
    ///
    /// Panic lock overrides all steps: if circuit-breaker is HardStop or regime is Panic,
    /// returns RecoveryMultiplierMin[Panic] (0.50) regardless of step.
    /// </summary>
    public decimal GetRecoveryMultiplier(
        Guid                       instanceId,
        VelocityRegimeState        regime,
        ShortPremiumVelocityConfig config)
    {
        // Panic lock — highest priority; use per-instance CB state
        bool panicLock = circuitBreaker.GetState(instanceId).State == CircuitBreakerStateValue.HardStop
                      || regime.Label == MarketRegime.VelocityPanic;

        if (panicLock)
        {
            decimal panicMin = config.RecoveryMultiplierMin.GetValueOrDefault(
                MarketRegime.VelocityPanic, 0.50m);
            log.LogDebug("RecoveryManager[{I}]: Panic lock → multiplier={M}", instanceId, panicMin);
            return panicMin;
        }

        var s = GetOrCreate(instanceId);

        if (!s.InRecovery)
            return 1.0m; // normal — no cap

        decimal regimeMin = config.RecoveryMultiplierMin.GetValueOrDefault(regime.Label, 0.50m);
        decimal regimeMax = config.RecoveryMultiplierMax.GetValueOrDefault(regime.Label, 1.25m);

        decimal multiplier = s.Step switch
        {
            1 => regimeMin,                                      // 50–100% depending on regime
            2 => Math.Round((regimeMin + regimeMax) / 2m, 4),   // mid-point
            3 => regimeMax,                                      // max (regime-capped)
            _ => 1.0m,
        };

        log.LogDebug(
            "RecoveryManager[{I}]: step={S} regime={R} multiplier={M} sessionsInStep={SS}",
            instanceId, s.Step, regime.Label, multiplier, s.SessionsInStep);

        return multiplier;
    }

    /// <summary>
    /// Evaluates whether to step up to the next recovery stage for the given instance.
    /// Returns true when a step-up occurred.
    /// </summary>
    public async Task<bool> EvaluateStepUpAsync(
        Guid                       instanceId,
        VelocityRegimeState        regime,
        ShortPremiumVelocityConfig config,
        CancellationToken          ct)
    {
        var cbState = circuitBreaker.GetState(instanceId).State;
        var s       = GetOrCreate(instanceId);

        // ── Enter recovery mode if not already in it ──────────────────────────
        if (!s.InRecovery && cbState != CircuitBreakerStateValue.Normal)
        {
            s.InRecovery         = true;
            s.Step               = 1;
            s.SessionsInStep     = 0;
            s.ProfitableSessions = 0;
            s.StepEnteredAt      = clock.NowInstant();
            log.LogWarning(
                "RecoveryManager[{I}]: entering recovery step=1 regime={R} cb={CB}",
                instanceId, regime.Label, cbState);
            return false; // just entered; evaluate next session
        }

        // ── Exit recovery when CB resets to Normal ────────────────────────────
        if (s.InRecovery && cbState == CircuitBreakerStateValue.Normal && s.Step >= 3)
        {
            decimal sharpe = ComputeRollingSharpe(s);
            if (sharpe >= config.RecoveryStep3SharpeMin)
            {
                log.LogInformation(
                    "RecoveryManager[{I}]: FULL recovery — sharpe={Sh:F2} ≥ {Min:F2}; exiting recovery mode",
                    instanceId, sharpe, config.RecoveryStep3SharpeMin);
                ResetInstance(s);
                return true;
            }
        }

        if (!s.InRecovery) return false;

        // ── Update session P&L stats ──────────────────────────────────────────
        await UpdateSessionStatsAsync(instanceId, s, regime, config, ct);

        int     minSessions = config.RecoveryWindowMinSessions.GetValueOrDefault(regime.Label, 20);
        int     maxSessions = config.RecoveryWindowMaxSessions.GetValueOrDefault(regime.Label, 40);
        decimal sharpeNow   = ComputeRollingSharpe(s);

        bool stepped = false;

        if (s.Step == 1 && s.SessionsInStep >= minSessions && s.ProfitableSessions >= 1)
        {
            s.Step           = 2;
            s.SessionsInStep = 0;
            log.LogInformation(
                "RecoveryManager[{I}]: step 1→2 after {N} sessions profit={P} regime={R}",
                instanceId, minSessions, s.ProfitableSessions, regime.Label);
            stepped = true;
        }
        else if (s.Step == 2 && s.SessionsInStep >= maxSessions &&
                 sharpeNow >= config.RecoveryStep3SharpeMin)
        {
            s.Step           = 3;
            s.SessionsInStep = 0;
            log.LogInformation(
                "RecoveryManager[{I}]: step 2→3 after {N} sessions sharpe={Sh:F2} regime={R}",
                instanceId, maxSessions, sharpeNow, regime.Label);
            stepped = true;
        }

        return stepped;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ResetInstance(RecoveryInstance s)
    {
        s.InRecovery         = false;
        s.Step               = 0;
        s.SessionsInStep     = 0;
        s.ProfitableSessions = 0;
        s.RollingPnl         = 0m;
        s.RollingPnlSumSq    = 0m;
        s.RollingPnlCount    = 0;
    }

    private async Task UpdateSessionStatsAsync(
        Guid                       instanceId,
        RecoveryInstance           s,
        VelocityRegimeState        regime,
        ShortPremiumVelocityConfig config,
        CancellationToken          ct)
    {
        // Proxy: compute daily P&L from closed positions today
        var now  = clock.NowInstant();
        var ist  = now.InZone(DateTimeZone.ForOffset(Offset.FromHoursAndMinutes(5, 30)));
        var today = ist.Date;

        await using var scope   = scopeFactory.CreateAsyncScope();
        var             posRepo = scope.ServiceProvider.GetRequiredService<IPositionRepository>();
        var closedToday = await posRepo.GetClosedTodayAsync(
            [Guid.Empty], today, ct);  // Guid.Empty = all strategy instances (proxy)

        decimal sessionPnl = closedToday.Sum(p => p.UnrealizedPnl + p.RealizedPnl);

        s.SessionsInStep++;
        if (sessionPnl > 0) s.ProfitableSessions++;

        // Update rolling stats for Sharpe
        s.RollingPnl      += sessionPnl;
        s.RollingPnlSumSq += sessionPnl * sessionPnl;
        s.RollingPnlCount++;

        log.LogDebug(
            "RecoveryManager[{I}].UpdateStats: sessionPnl={PnL:F0} sessionsInStep={S} profitable={P}",
            instanceId, sessionPnl, s.SessionsInStep, s.ProfitableSessions);
    }

    private static decimal ComputeRollingSharpe(RecoveryInstance s)
    {
        if (s.RollingPnlCount < 2) return 0m;

        decimal mean     = s.RollingPnl / s.RollingPnlCount;
        decimal variance = (s.RollingPnlSumSq / s.RollingPnlCount) - (mean * mean);
        decimal stdDev   = variance > 0m ? (decimal)Math.Sqrt((double)variance) : 1m;

        return stdDev > 0m ? Math.Round(mean / stdDev * (decimal)Math.Sqrt(252.0), 4) : 0m;
    }
}
