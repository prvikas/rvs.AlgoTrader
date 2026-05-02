using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

/// <summary>
/// Short Premium Velocity Strategy (STRAT-004).
///
/// Event-driven options premium-selling strategy across 5 velocity regimes:
///   VelocityLowVolCompression | VelocityChoppyMeanReversion | VelocityPostPanicNormalization
///   VelocityHighVolExpansion  | VelocityPanic
///
/// EvaluateAsync 17-step tick loop:
///   1.  Parse config from StrategyContext.ConfigJson (config injected at construction).
///   2.  Classify velocity regime via IQuantRegimeService.
///   3.  Circuit-breaker gate — HardStop → Hold (no new entries).
///   4.  Jump-risk gate — IsSoftStop blocks new entries.
///   5.  Margin gate — TrimToFit if NetShockedUtilization exceeds hard cap.
///   6.  VelocityScreener hard gates.
///   7.  Roll/close decisions on all open short-premium positions.
///   8.  MaxConcurrentPositions gate.
///   9.  VelocityIndicator — compute VS + OD, get AggressionMultiplier.
///   10. VelocityScore gate — VS &lt; MinVelocityScoreForEntry → Hold.
///   11. StructureSelector — select spread type for regime + VS.
///   12. DteOptimizer — select DTE bucket.
///   13. RecoveryManager — get sizing multiplier.
///   14. KellyHat sizing — half-Kelly × aggression × recovery.
///   15. Soft-stop size cap — CB=SoftStop → 50% cap.
///   16. HedgeEvaluator — fire-and-forget portfolio hedge assessment.
///   17. Build and return SpreadSignalResult.
///
/// Constructed via ActivatorUtilities in StrategyFactory; config is the explicitly-passed
/// parameter, all other constructor parameters are resolved from the DI container.
/// </summary>
public sealed class ShortPremiumVelocityStrategy(
    ShortPremiumVelocityConfig            config,
    IQuantRegimeService                   regimeService,
    IVelocityScreener                     screener,
    IVelocityIndicator                    indicator,
    IStructureSelector                    structure,
    IDteOptimizer                         dteOptimizer,
    IRollDecisionEngine                   rollEngine,
    IHedgeEvaluator                       hedgeEvaluator,
    IRecoveryManager                      recovery,
    ICircuitBreakerService                circuitBreaker,
    IJumpRiskMonitor                      jumpRisk,
    IMarginManager                        margin,
    IPositionRepository                   positions,
    ILogger<ShortPremiumVelocityStrategy> log)
    : IStrategy
{
    public string Name => "ShortPremiumVelocity";

    // Up to 4 concurrent short-premium legs (paired hedge legs are not tracked separately here)
    public int MaxConcurrentPositions => 4;

    // Regime classification needs ADX window + stability window
    public int MinWarmupBars => 60;

    public async Task<SignalResult> EvaluateAsync(StrategyContext ctx, CancellationToken ct)
    {
        // ── Step 2: Classify velocity regime ─────────────────────────────────
        VelocityRegimeState regime;
        try
        {
            regime = await regimeService.ClassifyVelocityRegimeAsync(ctx.InternalSymbol, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "SPV: regime classification failed for {Sym} — returning Hold",
                ctx.InternalSymbol);
            return SignalResult.Hold("Regime classification unavailable");
        }

        log.LogDebug(
            "SPV: sym={Sym} regime={R} stability={S:F2} tailRisk={TR:F1}",
            ctx.InternalSymbol, regime.Label, regime.RegimeStability, regime.TailRiskScore);

        // ── Step 3: Circuit-breaker gate ─────────────────────────────────────
        var cbState = circuitBreaker.CurrentState;
        if (cbState.State == CircuitBreakerStateValue.HardStop)
        {
            log.LogInformation(
                "SPV: HARD STOP active (loss={L:P2}) — no new entries", cbState.DailyLossPercent);
            return SignalResult.Hold(
                $"CircuitBreaker HardStop: dailyLoss={cbState.DailyLossPercent:P2}");
        }

        // ── Step 4: Jump-risk gate ────────────────────────────────────────────
        var jumpState = jumpRisk.CurrentState;
        if (jumpState.IsSoftStop)
        {
            log.LogInformation(
                "SPV: JumpRisk soft-stop active (ratio={R:F2}) — blocking new entries",
                jumpState.CurrentVolRatio);
            return SignalResult.Hold($"JumpRisk soft-stop: volRatio={jumpState.CurrentVolRatio:F2}");
        }

        // ── Step 5: Margin gate ───────────────────────────────────────────────
        var marginState = await margin.GetCurrentStateAsync(ct);
        if (marginState.NetShockedUtilization > config.ShockedUtilizationHardCap)
        {
            log.LogWarning(
                "SPV: Margin hard cap breached ({NS:P1} > {HC:P1}) — TrimToFit",
                marginState.NetShockedUtilization, config.ShockedUtilizationHardCap);
            await margin.TrimToFitAsync(config, ct);
            return SignalResult.Hold("MarginHardCap: TrimToFit triggered");
        }

        // ── Step 6: Velocity screener hard gates ──────────────────────────────
        var screen = await screener.ScreenAsync(ctx, regime, config, ct);
        if (!screen.Passed)
        {
            log.LogInformation(
                "SPV: hard gate blocked — {Reason}",
                string.Join("; ", screen.FailedHardGates));
            return SignalResult.Hold($"HardGate: {string.Join("; ", screen.FailedHardGates)}");
        }

        // ── Step 7: Roll/close decisions on existing open short-premium positions ──
        var openPositions = await positions.GetOpenAsync(ct);
        var shortLegs     = openPositions
            .Where(p => p.LegType == LegType.ShortPremium && p.IsOpen)
            .ToList();

        foreach (var pos in shortLegs)
        {
            var velocityPos  = MapToVelocityPosition(pos);
            var rollDecision = await rollEngine.EvaluateAsync(velocityPos, regime, ctx, config, ct);

            if (rollDecision.Action != RollAction.Hold)
            {
                log.LogInformation(
                    "SPV: pos={Id} action={A} reason={R}",
                    pos.Id, rollDecision.Action, rollDecision.Reason);
                // Phase 3: execute via IOrderManager; mark position reason for downstream handler
                pos.CloseReason = rollDecision.Reason;
                await positions.UpdateAsync(pos, ct);
            }
        }

        // ── Step 8: MaxConcurrentPositions gate ───────────────────────────────
        if (shortLegs.Count >= MaxConcurrentPositions)
        {
            log.LogDebug(
                "SPV: max concurrent positions reached ({N}/{Max}) — Hold",
                shortLegs.Count, MaxConcurrentPositions);
            return SignalResult.Hold(
                $"MaxConcurrentPositions={MaxConcurrentPositions} reached");
        }

        // ── Step 9: Velocity score ────────────────────────────────────────────
        VelocityScoreResult score;
        try
        {
            score = await indicator.ScoreAsync(ctx, regime, config, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "SPV: velocity score failed — returning Hold");
            return SignalResult.Hold("VelocityIndicator unavailable");
        }

        // ── Step 10: Velocity score gate ──────────────────────────────────────
        if (score.VelocityScore < config.MinVelocityScoreForEntry)
        {
            log.LogDebug(
                "SPV: VelocityScore={VS:F1} < min={Min:F1} — Hold",
                score.VelocityScore, config.MinVelocityScoreForEntry);
            return SignalResult.Hold(
                $"VelocityScore={score.VelocityScore:F1} < MinVelocityScoreForEntry={config.MinVelocityScoreForEntry:F1}");
        }

        // ── Step 11: Structure selection ──────────────────────────────────────
        var structureChoice = structure.SelectStructure(regime, score, config);
        if (structureChoice.Type == StructureType.None)
        {
            log.LogInformation(
                "SPV: no structure available for regime={R} — Hold", regime.Label);
            return SignalResult.Hold(
                $"StructureSelector: no valid structure for regime={regime.Label}");
        }

        // ── Step 12: DTE optimisation ─────────────────────────────────────────
        var dteSelection = dteOptimizer.OptimizeDte(regime, structureChoice, ctx, config);
        if (!dteSelection.IsEligible)
        {
            log.LogInformation(
                "SPV: DTE blocked — {Reason}", dteSelection.BlockingGate);
            return SignalResult.Hold($"DteOptimizer: {dteSelection.BlockingGate}");
        }

        // ── Step 13: Recovery multiplier ──────────────────────────────────────
        decimal recoveryMultiplier = recovery.GetRecoveryMultiplier(regime, config);

        // ── Step 14: KellyHat position sizing ────────────────────────────────
        // Half-Kelly: f = (winRate × avgWin − lossRate × avgLoss) / avgWin / 2
        decimal winRate      = WinRateProxy(regime);
        decimal avgWinRatio  = config.ProfitTargetFraction.GetValueOrDefault(regime.Label, 0.50m);
        decimal avgLossRatio = config.StopLossMultiplier;
        decimal lossRate     = 1m - winRate;

        decimal kelly     = avgWinRatio > 0m
            ? (winRate * avgWinRatio - lossRate * avgLossRatio) / avgWinRatio
            : 0m;
        decimal halfKelly = Math.Max(0m, kelly / 2m);

        decimal baseSize = Math.Round(
            halfKelly * score.AggressionMultiplier * recoveryMultiplier, 4);

        // ── Step 15: Soft-stop size cap ───────────────────────────────────────
        if (cbState.State == CircuitBreakerStateValue.SoftStop)
        {
            baseSize = Math.Round(baseSize * 0.50m, 4);
            log.LogInformation(
                "SPV: SoftStop — size capped 50%: baseSize={S:F4}", baseSize);
        }

        baseSize = Math.Clamp(baseSize, 0.10m, 1.0m);

        // ── Step 16: Hedge evaluation (fire-and-forget) ───────────────────────
        _ = hedgeEvaluator.EvaluatePortfolioHedgeNeedAsync(regime, config, ct);

        // ── Step 17: Build and return spread signal ───────────────────────────
        var legs = BuildSpreadLegs(structureChoice.Type, config);
        if (legs.Count == 0)
        {
            log.LogWarning(
                "SPV: no legs built for structure={S}", structureChoice.Type);
            return SignalResult.Hold("No spread legs built");
        }

        decimal spot   = ctx.OptionChain?.SpotPrice ?? 0m;
        LocalDate? exp = ctx.OptionChain?.Expiry;

        string reason =
            $"SPV: regime={regime.Label} VS={score.VelocityScore:F1} " +
            $"OD={score.OpportunityDensity:F1} struct={structureChoice.Type} " +
            $"dte={dteSelection.Dte}d size={baseSize:P1} " +
            $"agg={score.AggressionMultiplier:F2} recovery={recoveryMultiplier:F2} " +
            $"halfKelly={halfKelly:F4}";

        log.LogInformation("SPV: signal — {Reason}", reason);

        var spread = new SpreadSignalResult(
            SpreadType:      structureChoice.Type.ToString(),
            Legs:            legs,
            Reason:          reason,
            DiagnosticsJson: new
            {
                regime         = regime.Label.ToString(),
                stability      = regime.RegimeStability,
                tailRisk       = regime.TailRiskScore,
                velocityScore  = score.VelocityScore,
                opportunityDensity = score.OpportunityDensity,
                aggressionMultiplier = score.AggressionMultiplier,
                targetDte      = dteSelection.Dte,
                halfKelly,
                baseSize,
                recoveryMultiplier,
            },
            SpotPrice:       spot > 0m ? spot : null,
            NearExpiryDate:  exp);

        return SignalResult.SpreadEntry(spread);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static VelocityPosition MapToVelocityPosition(Domain.Entities.Position pos)
    {
        // Proxy Greeks — Phase 3 replaces with live re-pricing from ISyntheticOptionsPricer
        decimal qty        = Math.Abs(pos.Quantity);
        decimal gammaProxy = 0.01m;
        decimal thetaProxy = 0.05m;
        decimal gpTheta    = thetaProxy > 0m ? gammaProxy / thetaProxy : 0m;
        int     dte        = EstimateDteProxy(pos);

        return new VelocityPosition(
            PositionId:       pos.Id,
            UnderlyingSymbol: pos.InternalSymbol,
            StructureType:    StructureType.ShortStraddleStrangle, // proxy; Phase 3 from DB tag
            LegType:          pos.LegType ?? LegType.ShortPremium,
            HedgeType:        pos.HedgeType,
            EntryPremium:     pos.EntryPrice,
            CurrentPremium:   pos.CurrentPrice,
            Delta:            pos.Quantity < 0 ? -0.50m : 0.50m,
            Gamma:            gammaProxy,
            Theta:            thetaProxy,
            Vega:             -0.10m,
            GammaPerTheta:    gpTheta,
            Dte:              dte,
            LinkedShortLegId: pos.LinkedShortLegId,
            EnteredAt:        pos.OpenedAt ?? NodaTime.SystemClock.Instance.GetCurrentInstant());
    }

    private static int EstimateDteProxy(Domain.Entities.Position pos)
    {
        if (!pos.OpenedAt.HasValue) return 7;
        double ageDays = (NodaTime.SystemClock.Instance.GetCurrentInstant() - pos.OpenedAt.Value)
                          .TotalDays;
        return Math.Max(7 - (int)ageDays, 0);
    }

    private static decimal WinRateProxy(VelocityRegimeState regime)
        => regime.Label switch
        {
            MarketRegime.VelocityLowVolCompression      => 0.72m,
            MarketRegime.VelocityChoppyMeanReversion    => 0.65m,
            MarketRegime.VelocityPostPanicNormalization => 0.60m,
            MarketRegime.VelocityHighVolExpansion        => 0.55m,
            _                                            => 0.50m,
        };

    private static IReadOnlyList<SpreadLeg> BuildSpreadLegs(
        StructureType              type,
        ShortPremiumVelocityConfig config)
        => type switch
        {
            StructureType.ShortStraddleStrangle =>
            [
                new SpreadLeg(OptionType.Call, OrderDirection.Sell, StrikeSelectionMode.Atm,
                              TargetDelta: 0.50m, Quantity: 1),
                new SpreadLeg(OptionType.Put,  OrderDirection.Sell, StrikeSelectionMode.Atm,
                              TargetDelta: 0.50m, Quantity: 1),
            ],

            StructureType.IronCondor =>
            [
                new SpreadLeg(OptionType.Call, OrderDirection.Sell, StrikeSelectionMode.ByDelta,
                              TargetDelta: 0.20m, Quantity: 1),
                new SpreadLeg(OptionType.Call, OrderDirection.Buy,  StrikeSelectionMode.OtmByStrike,
                              OtmStrikes: config.HedgeWingWidth, Quantity: 1),
                new SpreadLeg(OptionType.Put,  OrderDirection.Sell, StrikeSelectionMode.ByDelta,
                              TargetDelta: 0.20m, Quantity: 1),
                new SpreadLeg(OptionType.Put,  OrderDirection.Buy,  StrikeSelectionMode.OtmByStrike,
                              OtmStrikes: config.HedgeWingWidth, Quantity: 1),
            ],

            StructureType.VerticalCreditSpread =>
            [
                new SpreadLeg(OptionType.Put,  OrderDirection.Sell, StrikeSelectionMode.ByDelta,
                              TargetDelta: 0.30m, Quantity: 1),
                new SpreadLeg(OptionType.Put,  OrderDirection.Buy,  StrikeSelectionMode.OtmByStrike,
                              OtmStrikes: config.HedgeWingWidth, Quantity: 1),
            ],

            StructureType.CalendarSpread =>
            [
                new SpreadLeg(OptionType.Call, OrderDirection.Sell, StrikeSelectionMode.Atm,
                              TargetDelta: 0.50m, NearestWeekly: true,  Quantity: 1),
                new SpreadLeg(OptionType.Call, OrderDirection.Buy,  StrikeSelectionMode.Atm,
                              TargetDelta: 0.50m, NearestWeekly: false, Quantity: 1),
            ],

            _ => [],
        };
}
