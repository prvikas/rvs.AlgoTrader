using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.Commands.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

/// <summary>
/// Per-tick portfolio hedge need assessment.
///
/// On each call: loads all open positions, recomputes VelocityPortfolioGreeks
/// (separating legs by LegType), evaluates M1/M2/M3 and C1/C2/C3 rules.
/// Publishes HedgeActionRequiredEvent via IPublisher (MediatR) for unmet mandatory
/// or expired conditional hedges. Does NOT publish when circuit-breaker is HardStop.
/// </summary>
public sealed class HedgeEvaluator(
    IServiceScopeFactory       scopeFactory,
    ICircuitBreakerService     circuitBreaker,
    IPublisher                 publisher,
    ILogger<HedgeEvaluator>    log)
    : IHedgeEvaluator
{
    public async Task EvaluatePortfolioHedgeNeedAsync(
        VelocityRegimeState        regime,
        ShortPremiumVelocityConfig config,
        CancellationToken          ct)
    {
        // Never publish hedge events when hard-stopped
        if (circuitBreaker.CurrentState.State == CircuitBreakerStateValue.HardStop)
        {
            log.LogDebug("HedgeEvaluator: HardStop active — skipping hedge evaluation");
            return;
        }

        // ── 1. Load all open positions ─────────────────────────────────────────
        await using var scope      = scopeFactory.CreateAsyncScope();
        var             posRepo    = scope.ServiceProvider.GetRequiredService<IPositionRepository>();
        var allOpen = await posRepo.GetOpenAsync(ct);
        var spvPositions = allOpen
            .Where(p => p.LegType.HasValue)
            .ToList();

        // ── 2. Recompute VelocityPortfolioGreeks ──────────────────────────────
        var greeks = AggregateGreeks(spvPositions);

        log.LogDebug(
            "HedgeEvaluator: NetDelta={D:F1} GrossGamma={GG:F4} GammaHedged={GH:F4} " +
            "HedgeCoverage={HC:P1} TailRisk={TR:F1}",
            greeks.NetDelta, greeks.GrossGamma, greeks.GammaHedged,
            greeks.HedgeCoverageRatio, regime.TailRiskScore);

        // ── 3. Evaluate M1: delta ─────────────────────────────────────────────
        if (Math.Abs(greeks.NetDelta) > config.DeltaHedgeTriggerThreshold)
        {
            string reason = $"|NetDelta|={Math.Abs(greeks.NetDelta):F1}>{config.DeltaHedgeTriggerThreshold:F1}";
            log.LogInformation("HedgeEvaluator M1: {Reason}", reason);
            await publisher.Publish(new HedgeActionRequiredEvent(
                UnderlyingSymbol: ExtractPrimarySymbol(spvPositions),
                RequiredHedgeType: HedgeType.DeltaHedge,
                Reason:           reason,
                Regime:           regime.Label,
                OccurredAt:       NodaTime.SystemClock.Instance.GetCurrentInstant()), ct);
        }

        // ── 4. Evaluate M2: tail-risk hedge ───────────────────────────────────
        if (regime.TailRiskScore > 60m)
        {
            bool tailHedgeActive = spvPositions.Any(p => p.HedgeType == HedgeType.TailRiskHedge);
            if (!tailHedgeActive)
            {
                string reason = $"TailRiskScore={regime.TailRiskScore:F1}>60, no active TailRiskHedge";
                log.LogInformation("HedgeEvaluator M2: {Reason}", reason);
                await publisher.Publish(new HedgeActionRequiredEvent(
                    UnderlyingSymbol: ExtractPrimarySymbol(spvPositions),
                    RequiredHedgeType: HedgeType.TailRiskHedge,
                    Reason:           reason,
                    Regime:           regime.Label,
                    OccurredAt:       NodaTime.SystemClock.Instance.GetCurrentInstant()), ct);
            }
        }

        // ── 5. Evaluate M3: vega hedge ────────────────────────────────────────
        bool isHighVolOrPanic = regime.Label is MarketRegime.VelocityHighVolExpansion
                                             or MarketRegime.VelocityPanic;
        if (isHighVolOrPanic && greeks.NetVega < 0m)
        {
            bool vegaHedgeActive = spvPositions.Any(p => p.HedgeType == HedgeType.VegaHedge);
            if (!vegaHedgeActive)
            {
                string reason = $"{regime.Label}: NetVega={greeks.NetVega:F2}<0, no active VegaHedge";
                log.LogInformation("HedgeEvaluator M3: {Reason}", reason);
                await publisher.Publish(new HedgeActionRequiredEvent(
                    UnderlyingSymbol: ExtractPrimarySymbol(spvPositions),
                    RequiredHedgeType: HedgeType.VegaHedge,
                    Reason:           reason,
                    Regime:           regime.Label,
                    OccurredAt:       NodaTime.SystemClock.Instance.GetCurrentInstant()), ct);
            }
        }

        // ── 6. Evaluate per-position conditional hedges ───────────────────────
        var shortLegs = spvPositions.Where(p => p.LegType == LegType.ShortPremium).ToList();
        foreach (var position in shortLegs)
        {
            // We have Position entity but need VelocityPosition — map key fields
            // C1/C2 trigger: high GammaPerTheta AND short DTE
            // Use position's current IV proxy to estimate GammaPerTheta
            decimal estimatedGPT = EstimateGammaPerTheta(position);
            int     estimatedDte = EstimateDte(position);

            if (estimatedGPT > config.ConditionalHedgeGammaPerThetaTrigger &&
                estimatedDte <= config.ConditionalHedgeDteTrigger)
            {
                bool hedgeExists = spvPositions.Any(p =>
                    p.LinkedShortLegId == position.Id &&
                    p.LegType == LegType.Hedge);

                if (!hedgeExists)
                {
                    string reason = $"pos={position.Id}: GammaPerTheta={estimatedGPT:F2}, DTE={estimatedDte}";
                    log.LogInformation("HedgeEvaluator C1/C2: {Reason}", reason);
                    await publisher.Publish(new HedgeActionRequiredEvent(
                        UnderlyingSymbol: position.InternalSymbol,
                        RequiredHedgeType: HedgeType.ProtectivePut,
                        Reason:           reason,
                        Regime:           regime.Label,
                        OccurredAt:       NodaTime.SystemClock.Instance.GetCurrentInstant()), ct);
                }
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static VelocityPortfolioGreeks AggregateGreeks(IReadOnlyList<Position> positions)
    {
        decimal netDelta    = 0m, grossGamma = 0m, gammaHedged = 0m;
        decimal netTheta    = 0m, netVega    = 0m, totalHedgeCost = 0m;

        foreach (var p in positions)
        {
            // In production, delta/gamma/theta/vega come from live re-pricing
            // Here we use position unrealized P&L as a proxy for delta contribution
            decimal sign = p.Quantity >= 0 ? 1m : -1m;

            if (p.LegType == LegType.ShortPremium)
            {
                netDelta   -= sign * 0.1m;  // placeholder delta
                grossGamma += 0.01m;        // absolute gamma
                netTheta   += 0.05m;        // theta collected
                netVega    -= 0.1m;         // short vega
            }
            else if (p.LegType is LegType.Hedge or LegType.DeltaHedge)
            {
                gammaHedged += 0.005m;      // hedge reduces gross gamma
                netVega     += 0.05m;       // hedge adds positive vega
                totalHedgeCost += p.HedgeNetCost ?? 0m;
            }
        }

        decimal grossGammaSafe = grossGamma > 0 ? grossGamma : 1m;
        decimal hcr = gammaHedged / grossGammaSafe;

        return new VelocityPortfolioGreeks(
            NetDelta:           netDelta,
            GrossGamma:         grossGamma,
            GammaHedged:        gammaHedged,
            NetTheta:           netTheta,
            NetVega:            netVega,
            HedgeCoverageRatio: Math.Clamp(hcr, 0m, 1m),
            TotalHedgeNetCost:  totalHedgeCost);
    }

    private static string ExtractPrimarySymbol(IReadOnlyList<Position> positions)
        => positions.FirstOrDefault()?.InternalSymbol ?? "UNKNOWN";

    private static decimal EstimateGammaPerTheta(Position p)
        // Proxy: gamma/theta increases near expiry; use unrealized P&L sign as indicator
        => p.UnrealizedPnl < 0 ? 1.8m : 0.8m;

    private static int EstimateDte(Position p)
    {
        // In production: parse DTE from instrument symbol (e.g. NIFTY24JAN25000CE)
        // Proxy: recent positions opened < 5 days ago have low DTE in weekly expiry
        if (p.OpenedAt.HasValue)
        {
            var age = (NodaTime.SystemClock.Instance.GetCurrentInstant() - p.OpenedAt.Value)
                      .TotalDays;
            return Math.Max(7 - (int)age, 0);
        }
        return 7;
    }
}
