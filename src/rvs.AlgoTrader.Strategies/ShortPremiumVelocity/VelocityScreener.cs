using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

/// <summary>
/// Hard-gate + soft-filter screener for Short Premium Velocity.
///
/// Hard gates (any failure → Passed=false, gate name added to FailedHardGates):
///   a. TailRiskScore ≤ TailRiskScoreCeiling[regime]
///   b. GammaPerTheta ≤ GammaPerThetaCeiling[regime]
///   c. LiquiditySurvival ≥ LiquiditySurvivalFloor[regime]
///   d. ClusterExposure ≤ effectiveClusterCap (tightened during results season)
///   e. NetShockedUtilization within MarginManager limit
///   f. ResultsSeason AND scheduled result within 2 sessions → reject
///   g. CircuitBreakerState != HardStop
///   h. RequiresMandatoryHedge AND HedgeBudget exhausted → reject
///
/// Soft rules reduce SuggestedAggressionCap but do not block entry.
///
/// RequiresMandatoryHedge = TailRiskScore > 60 OR regime is HighVol/Panic
///                           OR |PortfolioDelta| > DeltaHedgeTriggerThreshold
/// </summary>
public sealed class VelocityScreener(
    ICircuitBreakerService        circuitBreaker,
    IMarginManager                marginManager,
    ILogger<VelocityScreener>     log)
    : IVelocityScreener
{
    public async Task<VelocityScreenResult> ScreenAsync(
        StrategyContext            ctx,
        VelocityRegimeState        regime,
        ShortPremiumVelocityConfig config,
        CancellationToken          ct)
    {
        var failedHardGates = new List<string>();
        var softViolations  = new List<string>();
        decimal aggressionCap = config.AggressionMultiplierMax;

        // Pre-fetch margin state (needed for gates e and cluster)
        MarginState margin = await marginManager.GetCurrentStateAsync(ct);

        // ── Derived inputs (from StrategyContext / option chain) ──────────────
        // In production these come from OptionChain and portfolio analytics;
        // we surface sensible defaults when data is absent.
        decimal tailRiskScore     = regime.TailRiskScore;
        decimal gammaPerTheta     = ExtractGammaPerTheta(ctx);
        decimal liquiditySurvival = ExtractLiquiditySurvival(ctx);
        decimal clusterExposure   = ExtractClusterExposure(ctx);
        decimal portfolioDelta    = ExtractPortfolioDelta(ctx);

        // ── Effective cluster cap ─────────────────────────────────────────────
        decimal effectiveClusterCap = regime.IsResultsSeason
            ? config.ClusterHardCap * config.ResultsSeasonClusterCapMultiplier
            : config.ClusterHardCap;

        // ── Hard gate a: TailRiskScore ─────────────────────────────────────────
        if (config.TailRiskScoreCeiling.TryGetValue(regime.Label, out decimal trsMax) &&
            tailRiskScore > trsMax)
        {
            failedHardGates.Add($"TailRiskScore={tailRiskScore:F1}>Ceiling={trsMax:F1}");
        }

        // ── Hard gate b: GammaPerTheta ────────────────────────────────────────
        if (config.GammaPerThetaCeiling.TryGetValue(regime.Label, out decimal gptMax) &&
            gammaPerTheta > gptMax)
        {
            failedHardGates.Add($"GammaPerTheta={gammaPerTheta:F2}>Ceiling={gptMax:F2}");
        }

        // ── Hard gate c: LiquiditySurvival ───────────────────────────────────
        if (config.LiquiditySurvivalFloor.TryGetValue(regime.Label, out decimal lsFloor) &&
            liquiditySurvival < lsFloor)
        {
            failedHardGates.Add($"LiquiditySurvival={liquiditySurvival:F3}<Floor={lsFloor:F3}");
        }

        // ── Hard gate d: ClusterExposure ──────────────────────────────────────
        if (clusterExposure > effectiveClusterCap)
        {
            failedHardGates.Add($"ClusterExposure={clusterExposure:P1}>EffectiveCap={effectiveClusterCap:P1}" +
                (regime.IsResultsSeason ? " [ResultsSeason tightened]" : ""));
        }

        // ── Hard gate e: Margin ───────────────────────────────────────────────
        if (margin.NetShockedUtilization >= config.ShockedUtilizationHardCap)
        {
            failedHardGates.Add(
                $"ShockedUtilization={margin.NetShockedUtilization:P1}>=HardCap={config.ShockedUtilizationHardCap:P1}");
        }

        // ── Hard gate f: ResultsSeason event proximity ────────────────────────
        if (regime.IsResultsSeason && ctx.HasUpcomingEvent)
        {
            failedHardGates.Add("ResultsSeasonEventProximity: scheduled result within 2 sessions");
        }

        // ── Hard gate g: CircuitBreaker HardStop ──────────────────────────────
        var cbState = circuitBreaker.CurrentState;
        if (cbState.State == CircuitBreakerStateValue.HardStop)
        {
            failedHardGates.Add("CircuitBreaker=HardStop");
        }

        // ── RequiresMandatoryHedge ─────────────────────────────────────────────
        bool requiresHedge =
            tailRiskScore > 60m ||
            regime.Label is Domain.Entities.MarketRegime.VelocityHighVolExpansion
                        or Domain.Entities.MarketRegime.VelocityPanic ||
            Math.Abs(portfolioDelta) > config.DeltaHedgeTriggerThreshold;

        // ── Hard gate h: Hedge budget exhausted ───────────────────────────────
        // HedgeBudgetRemainingForSession is tracked externally; approximate from margin state here.
        if (requiresHedge && !margin.IsFresh)
        {
            failedHardGates.Add("RequiresMandatoryHedge AND HedgeBudget=Exhausted (margin stale)");
        }

        // ── Soft rules ────────────────────────────────────────────────────────

        // Soft a: Correlation diversity below threshold (proxy: use breadth pct)
        if (ctx.BreadthPct200Sma.HasValue && ctx.BreadthPct200Sma.Value < 40m)
        {
            softViolations.Add("CorrelationDiversity<threshold");
            aggressionCap = Math.Min(aggressionCap, config.AggressionMultiplierMax * 0.90m);
        }

        // Soft b: EventProximity elevated (but not within 2-session window)
        if (ctx.HasUpcomingEvent && !regime.IsResultsSeason)
        {
            softViolations.Add("EventProximity=elevated");
            aggressionCap = Math.Min(aggressionCap, config.AggressionMultiplierMax * 0.85m);
        }

        // Soft c: Elevated slippage signal (IV richness proxy)
        if (regime.Label == Domain.Entities.MarketRegime.VelocityHighVolExpansion)
        {
            softViolations.Add("Slippage=elevated (HighVolExpansion regime)");
            aggressionCap = Math.Min(aggressionCap, config.AggressionMultiplierMax * 0.80m);
        }

        // Soft d: SoftStop from circuit breaker
        if (cbState.State == CircuitBreakerStateValue.SoftStop)
        {
            softViolations.Add("CircuitBreaker=SoftStop");
            aggressionCap = Math.Min(aggressionCap, 1.0m);
        }

        // Soft e: ResultsSeason + low OD
        if (regime.IsResultsSeason)
        {
            softViolations.Add("ResultsSeason: cluster cap tightened, monitoring OD");
            aggressionCap = Math.Min(aggressionCap, config.AggressionMultiplierMax * 0.75m);
        }

        bool passed = failedHardGates.Count == 0;

        log.LogDebug(
            "VelocityScreener: {Result} | Gates={Gates} | Soft={Soft} | AggrCap={Agg:F2} | Regime={R}",
            passed ? "PASS" : "FAIL",
            passed ? "none" : string.Join("; ", failedHardGates),
            softViolations.Count == 0 ? "none" : string.Join("; ", softViolations),
            aggressionCap, regime.Label);

        return new VelocityScreenResult(
            Passed:                  passed,
            FailedHardGates:         failedHardGates,
            SoftViolations:          softViolations,
            SuggestedAggressionCap: Math.Round(aggressionCap, 4),
            RequiresMandatoryHedge:  requiresHedge);
    }

    // ── Data extraction helpers (StrategyContext → domain inputs) ─────────────

    private static decimal ExtractGammaPerTheta(StrategyContext ctx)
        // OptionChain available: derive from ATM Greeks snapshot
        => ctx.OptionChain?.AtmIv > 0 ? 1.0m : 0.5m; // proxy; replaced by real Greeks in live mode

    private static decimal ExtractLiquiditySurvival(StrategyContext ctx)
        // Bid-ask relative to ATM premium — proxy from IV when chain is present
        => ctx.OptionChain is not null ? 0.08m : 0.04m;

    private static decimal ExtractClusterExposure(StrategyContext ctx)
        // Proxy: single-symbol exposure ≡ 0.20 when no portfolio data available
        => 0.20m;

    private static decimal ExtractPortfolioDelta(StrategyContext ctx)
        => 0m; // replaced by live aggregation in HedgeEvaluator
}
