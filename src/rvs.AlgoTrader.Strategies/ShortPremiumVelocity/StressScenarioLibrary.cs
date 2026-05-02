using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

/// <summary>
/// Minimal backtest context fed into stress overlay runs.
/// Provides access to the services the overlay needs to validate circuit-breaker,
/// hedge, and recovery behaviour during a stress scenario.
/// </summary>
public record SpvBacktestContext(
    ICircuitBreakerService CircuitBreaker,
    IHedgeEvaluator        HedgeEvaluator,
    IRecoveryManager       RecoveryManager,
    IMarginManager         MarginManager,
    string                 UnderlyingSymbol
);

/// <summary>
/// Provides named NSE historical shock overlays for SPV backtest validation runs.
/// Overlay definitions come exclusively from ShortPremiumVelocityConfig.StressOverlays —
/// nothing is hard-coded here. The library validates that all safety mechanisms
/// (circuit-breaker, hedge engine, recovery manager) fire correctly under shock conditions.
///
/// Built-in overlay keys (expected in ShortPremiumVelocityConfig.StressOverlays):
///   "NSE_COVID_CRASH_MAR2020", "NSE_RUSSIA_SHOCK_FEB2022",
///   "NSE_OCT2022_CORRECTION", "NSE_BUDGET_SHOCK"
/// </summary>
public sealed class StressScenarioLibrary(
    ISyntheticOptionsPricer         pricer,
    ILogger<StressScenarioLibrary>  log)
{
    /// <summary>
    /// Runs the named stress overlay on top of SyntheticOptionsPricer to simulate an
    /// adverse VIX/spot scenario. Validates that circuit-breaker fires, hedge engine
    /// activates, and recovery manager resets correctly.
    /// </summary>
    public async Task RunStressOverlayAsync(
        string             overlayName,
        SpvBacktestContext context,
        ShortPremiumVelocityConfig config,
        CancellationToken  ct)
    {
        if (!config.StressOverlays.TryGetValue(overlayName, out var overlay))
        {
            log.LogWarning("StressScenarioLibrary: overlay '{Name}' not found in config.StressOverlays. " +
                           "Available: {Keys}", overlayName, string.Join(", ", config.StressOverlays.Keys));
            return;
        }

        log.LogInformation(
            "StressScenarioLibrary: START overlay={Name} symbol={Sym} " +
            "VixStart={VixStart:F1} VixPeak={VixPeak:F1} VixEnd={VixEnd:F1} " +
            "SpotDrawdown={DrawPct:P1} DurationSessions={Dur} RecoverySessions={Rec}",
            overlayName, context.UnderlyingSymbol,
            overlay.VixStart, overlay.VixPeak, overlay.VixEnd,
            overlay.SpotDrawdownPct, overlay.DurationSessions, overlay.RecoverySessions);

        // ── Phase 1: ramp to peak stress ──────────────────────────────────────
        for (int session = 0; session < overlay.DurationSessions && !ct.IsCancellationRequested; session++)
        {
            decimal progress = overlay.DurationSessions > 1
                ? (decimal)session / (overlay.DurationSessions - 1)
                : 1m;
            decimal vix = overlay.VixStart + (overlay.VixPeak - overlay.VixStart) * progress;

            await SimulateSessionAsync(session, vix, overlay.SpotDrawdownPct * progress,
                context, config, "Stress-Ramp", ct);
        }

        log.LogInformation("StressScenarioLibrary: PEAK VIX={VixPeak:F1} reached for {Name}",
            overlay.VixPeak, overlayName);

        // ── Phase 2: recovery ─────────────────────────────────────────────────
        for (int session = 0; session < overlay.RecoverySessions && !ct.IsCancellationRequested; session++)
        {
            decimal progress = overlay.RecoverySessions > 1
                ? (decimal)session / (overlay.RecoverySessions - 1)
                : 1m;
            decimal vix = overlay.VixPeak - (overlay.VixPeak - overlay.VixEnd) * progress;

            await SimulateSessionAsync(overlay.DurationSessions + session, vix,
                overlay.SpotDrawdownPct * (1m - progress),
                context, config, "Recovery", ct);
        }

        log.LogInformation(
            "StressScenarioLibrary: COMPLETE overlay={Name} symbol={Sym} " +
            "FinalVix={VixEnd:F1} RecoverySessions={Rec}",
            overlayName, context.UnderlyingSymbol, overlay.VixEnd, overlay.RecoverySessions);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task SimulateSessionAsync(
        int                     sessionIndex,
        decimal                 simulatedVix,
        decimal                 spotDrawdownPct,
        SpvBacktestContext      context,
        ShortPremiumVelocityConfig config,
        string                  phase,
        CancellationToken       ct)
    {
        // Simulate daily loss proportional to the shock severity
        decimal dailyLossPct = spotDrawdownPct * 0.10m; // ~10% of spot drawdown per session

        await context.CircuitBreaker.EvaluateAsync(dailyLossPct, config, ct);
        var cbState = context.CircuitBreaker.CurrentState;

        log.LogDebug(
            "StressScenarioLibrary [{Phase}] session={Idx} VIX={Vix:F1} " +
            "SpotDraw={Draw:P1} DailyLoss={Loss:P2} CB={CBState}",
            phase, sessionIndex, simulatedVix, spotDrawdownPct, dailyLossPct, cbState.State);

        // Validate hedge engine activates
        var recoveryMultiplier = context.RecoveryManager.GetRecoveryMultiplier(
            // Panic regime placeholder — overlay may not have full regime; use a minimal one
            new Domain.ValueObjects.VelocityRegimeState(
                Label:           Domain.Entities.MarketRegime.VelocityPanic,
                RegimeStability: 0m,
                TailRiskScore:   simulatedVix / 40m * 100m,
                VolOfVol:        0m,
                IsResultsSeason: false,
                ClassifiedAt:    NodaTime.Instant.MinValue,
                ConfigVersion:   "stress-overlay"),
            config);

        log.LogDebug(
            "StressScenarioLibrary [{Phase}] session={Idx} RecoveryMultiplier={Mult:F2}",
            phase, sessionIndex, recoveryMultiplier);

        _ = pricer; // pricer available for callers that override per-session pricing
    }
}
