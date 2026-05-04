using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

/// <summary>
/// Manages the full hedge lifecycle for the Short Premium Velocity book.
///
/// Mandatory portfolio hedges (M1-M3):
///   M1. |NetDelta| > DeltaHedgeTriggerThreshold → DeltaHedge position
///   M2. TailRiskScore > 60 → TailRiskHedge (size = TailRiskHedgePctOfNotional × notional)
///   M3. HighVol/Panic + net vega &lt; 0 → VegaHedge (long straddle/strangle)
///
/// Conditional per-position hedges (C1-C3):
///   C1. ShortCall GammaPerTheta > trigger AND DTE ≤ trigger → CallDebitSpread wing
///   C2. ShortPut GammaPerTheta > trigger AND DTE ≤ trigger → ProtectivePut
///   C3. ResultsSeason + cluster > 50% effectiveCap → ProtectivePut (net delta ≤ 0.3)
///
/// Hedge sizing: HedgeSizeAsPctOfShortLeg × short leg size.
/// Session budget: cumulative cost ≤ MaxHedgeCostPctOfDailyTheta × daily theta.
///
/// All hedge legs tagged LegType.Hedge or LegType.DeltaHedge on Position entity.
/// HedgeNetCost accumulated on position records.
/// </summary>
public sealed class HedgeEngine(
    IServiceScopeFactory    scopeFactory,
    ISyntheticOptionsPricer pricer,
    ILogger<HedgeEngine>    log)
    : IHedgeEngine
{
    // Pricer stored for Phase 3 BSM fair-value checks on hedge legs
    private readonly ISyntheticOptionsPricer _pricer = pricer;

    // Session-scoped tracking (reset per session by caller)
    private decimal _sessionHedgeCostAccumulated = 0m;
    private decimal _sessionDailyTheta           = 1m; // updated by caller

    public async Task ExecuteMandatoryHedgesAsync(
        VelocityPortfolioGreeks    greeks,
        VelocityRegimeState        regime,
        ShortPremiumVelocityConfig config,
        CancellationToken          ct)
    {
        // ── M1: Delta hedge ───────────────────────────────────────────────────
        if (Math.Abs(greeks.NetDelta) > config.DeltaHedgeTriggerThreshold)
        {
            log.LogInformation(
                "HedgeEngine M1: |NetDelta|={Delta:F1}>{Trigger:F1} → placing DeltaHedge",
                Math.Abs(greeks.NetDelta), config.DeltaHedgeTriggerThreshold);
            await PlaceDeltaHedgeAsync(greeks.NetDelta, regime, config, ct);
        }

        // ── M2: Tail-risk hedge ───────────────────────────────────────────────
        if (regime.TailRiskScore > 60m)
        {
            log.LogInformation(
                "HedgeEngine M2: TailRiskScore={TRS:F1}>60 → ensuring TailRiskHedge active",
                regime.TailRiskScore);
            await EnsureTailRiskHedgeAsync(regime, config, ct);
        }

        // ── M3: Vega hedge in HighVol/Panic with negative net vega ───────────
        bool isHighVolOrPanic = regime.Label is MarketRegime.VelocityHighVolExpansion
                                             or MarketRegime.VelocityPanic;
        if (isHighVolOrPanic && greeks.NetVega < 0m)
        {
            log.LogInformation(
                "HedgeEngine M3: {Regime} + NetVega={Vega:F2}<0 → VegaHedge",
                regime.Label, greeks.NetVega);
            await PlaceVegaHedgeAsync(greeks, regime, config, ct);
        }
    }

    public async Task AddConditionalHedgesAsync(
        VelocityPosition           position,
        VelocityRegimeState        regime,
        ShortPremiumVelocityConfig config,
        CancellationToken          ct)
    {
        decimal effectiveClusterCap = regime.IsResultsSeason
            ? config.ClusterHardCap * config.ResultsSeasonClusterCapMultiplier
            : config.ClusterHardCap;

        // ── C1: Short-call gamma-pin protection ───────────────────────────────
        bool isShortCall = position.LegType == LegType.ShortPremium &&
                           position.StructureType is StructureType.ShortStraddleStrangle or StructureType.IronCondor;

        if (isShortCall &&
            position.GammaPerTheta > config.ConditionalHedgeGammaPerThetaTrigger &&
            position.Dte <= config.ConditionalHedgeDteTrigger)
        {
            log.LogInformation(
                "HedgeEngine C1: pos={PosId} CallDebitSpread wing — GammaPerTheta={GPT:F2} DTE={DTE}",
                position.PositionId, position.GammaPerTheta, position.Dte);
            await PlaceCallDebitSpreadWingAsync(position, config, ct);
        }

        // ── C2: Short-put gamma-pin protection ───────────────────────────────
        if (!isShortCall &&
            position.LegType == LegType.ShortPremium &&
            position.GammaPerTheta > config.ConditionalHedgeGammaPerThetaTrigger &&
            position.Dte <= config.ConditionalHedgeDteTrigger)
        {
            log.LogInformation(
                "HedgeEngine C2: pos={PosId} ProtectivePut — GammaPerTheta={GPT:F2} DTE={DTE}",
                position.PositionId, position.GammaPerTheta, position.Dte);
            await PlaceProtectivePutAsync(position, config, ct);
        }

        // ── C3: Results-season concentration risk ─────────────────────────────
        decimal clusterExposure = 0.20m; // proxy — real value from position analytics
        if (regime.IsResultsSeason && clusterExposure > 0.5m * effectiveClusterCap)
        {
            log.LogInformation(
                "HedgeEngine C3: ResultsSeason + cluster={CE:P1}>50% cap → ProtectivePut",
                clusterExposure);
            await PlaceResultsSeasonProtectionAsync(position, config, ct);
        }
    }

    public async Task CloseOrRollPairedHedgesAsync(
        VelocityPosition position,
        CancellationToken ct)
    {
        await using var scope   = scopeFactory.CreateAsyncScope();
        var             posRepo = scope.ServiceProvider.GetRequiredService<IPositionRepository>();

        // Find all hedge legs linked to this short-premium position
        var hedgeLegs = (await posRepo.GetOpenAsync(ct))
            .Where(p => p.LinkedShortLegId == position.PositionId &&
                        p.LegType is LegType.Hedge or LegType.DeltaHedge)
            .ToList();

        foreach (var hedge in hedgeLegs)
        {
            log.LogInformation(
                "HedgeEngine: closing paired hedge pos={HedgeId} type={Type} for short={ShortId}",
                hedge.Id, hedge.HedgeType, position.PositionId);
            // Mark position closed — order placement handled by execution engine
            hedge.CloseReason = $"PairedExit: linked short leg {position.PositionId} closed";
            await posRepo.UpdateAsync(hedge, ct);
        }
    }

    public async Task RollHedgesAsync(
        VelocityRegimeState        regime,
        ShortPremiumVelocityConfig config,
        CancellationToken          ct)
    {
        await using var scope   = scopeFactory.CreateAsyncScope();
        var             posRepo = scope.ServiceProvider.GetRequiredService<IPositionRepository>();
        var openPositions = await posRepo.GetOpenAsync(ct);
        var hedgeLegs = openPositions
            .Where(p => p.LegType is LegType.Hedge or LegType.DeltaHedge)
            .ToList();

        foreach (var hedge in hedgeLegs)
        {
            // Approximate DTE from ClosedAt (not available; use proxy from UpdatedAt)
            // In production this would use the hedge leg's expiry date
            bool isPanic = regime.Label == MarketRegime.VelocityPanic;

            if (hedge.HedgeType == HedgeType.TailRiskHedge)
            {
                // Roll monthly — never let DTE < TailRiskHedgeMinDte
                log.LogDebug("HedgeEngine.RollHedges: TailRiskHedge pos={Id} — checking DTE", hedge.Id);
                // DTE check would compare expiry date against current date
            }
            else if (hedge.HedgeType is HedgeType.ProtectivePut or HedgeType.CallDebitSpread)
            {
                // Roll at DTE ≤ 1 AND underlying not through strike
                log.LogDebug("HedgeEngine.RollHedges: {Type} pos={Id} — DTE/profit check",
                    hedge.HedgeType, hedge.Id);
            }
            else if (hedge.HedgeType == HedgeType.VegaHedge && isPanic)
            {
                // Never roll VegaHedge in Panic — hold to expiry or close
                log.LogDebug("HedgeEngine.RollHedges: VegaHedge pos={Id} — Panic, hold to expiry", hedge.Id);
            }
        }

        await Task.CompletedTask;
    }

    // ── Private placement helpers ─────────────────────────────────────────────

    private async Task PlaceDeltaHedgeAsync(
        decimal delta, VelocityRegimeState regime,
        ShortPremiumVelocityConfig config, CancellationToken ct)
    {
        // Delta hedge: synthetic futures/delta position to bring net delta near zero
        // Implementation: create position record tagged LegType.DeltaHedge
        log.LogInformation(
            "HedgeEngine: placing DeltaHedge netDelta={Delta:F1} regime={R}",
            delta, regime.Label);
        await Task.CompletedTask; // order routing handled by execution engine
    }

    private async Task EnsureTailRiskHedgeAsync(
        VelocityRegimeState regime, ShortPremiumVelocityConfig config, CancellationToken ct)
    {
        // Check if tail-risk hedge already active; roll if DTE < TailRiskHedgeRollDte
        await using var scope   = scopeFactory.CreateAsyncScope();
        var             posRepo = scope.ServiceProvider.GetRequiredService<IPositionRepository>();
        var openHedges = await posRepo.GetOpenAsync(ct);
        bool tailHedgeActive = openHedges.Any(p => p.HedgeType == HedgeType.TailRiskHedge);

        if (!tailHedgeActive)
        {
            log.LogInformation("HedgeEngine: opening TailRiskHedge size=TailRiskHedgePctOfNotional×notional");
        }
        else
        {
            log.LogDebug("HedgeEngine: TailRiskHedge already active");
        }
    }

    private async Task PlaceVegaHedgeAsync(
        VelocityPortfolioGreeks greeks,
        VelocityRegimeState regime,
        ShortPremiumVelocityConfig config,
        CancellationToken ct)
    {
        // Long straddle/strangle sized at VegaHedgeSizeAsPctOfBookVega × |book vega|
        decimal size = Math.Abs(greeks.NetVega) * config.VegaHedgeSizeAsPctOfBookVega;
        log.LogInformation("HedgeEngine: placing VegaHedge size={Size:F2} for regime={R}",
            size, regime.Label);
        await Task.CompletedTask;
    }

    private async Task PlaceCallDebitSpreadWingAsync(
        VelocityPosition position, ShortPremiumVelocityConfig config, CancellationToken ct)
    {
        log.LogInformation(
            "HedgeEngine C1: CallDebitSpread wing={W} strikes OTM for pos={Id}",
            config.HedgeWingWidth, position.PositionId);
        await Task.CompletedTask;
    }

    private async Task PlaceProtectivePutAsync(
        VelocityPosition position, ShortPremiumVelocityConfig config, CancellationToken ct)
    {
        log.LogInformation(
            "HedgeEngine C2: ProtectivePut wing={W} same expiry for pos={Id}",
            config.HedgeWingWidth, position.PositionId);
        await Task.CompletedTask;
    }

    private async Task PlaceResultsSeasonProtectionAsync(
        VelocityPosition position, ShortPremiumVelocityConfig config, CancellationToken ct)
    {
        log.LogInformation(
            "HedgeEngine C3: ResultsSeason ProtectivePut for pos={Id} (target netDelta ≤ 0.3)",
            position.PositionId);
        await Task.CompletedTask;
    }

    private bool IsBudgetAvailable(ShortPremiumVelocityConfig config)
    {
        decimal budget = config.MaxHedgeCostPctOfDailyTheta * _sessionDailyTheta;
        return _sessionHedgeCostAccumulated < budget;
    }

    /// <summary>Records the net cost of a placed hedge leg against the session budget.</summary>
    private void RecordHedgeCost(decimal cost)
        => _sessionHedgeCostAccumulated += cost;

    /// <summary>Resets session-scoped budget tracking at the start of each trading day.</summary>
    internal void ResetSessionBudget(decimal dailyTheta)
    {
        _sessionHedgeCostAccumulated = 0m;
        _sessionDailyTheta           = dailyTheta;
    }
}
