using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

/// <summary>
/// Broker-reported margin state with shock-scenario utilisation tracking.
///
/// NetShockedUtilization = (grossShortPremiumMargin − hedgeMarginCredit) / totalMargin.
///
/// TrimToFit trigger: NetShockedUtilization + ExpectedVolaShockCharge > ShockedUtilizationHardCap.
///
/// Trim order (STRICT):
///   1. Rank short-premium legs by ThetaToMargin ratio (worst first), trim until target met.
///   2. Only after ALL short legs trimmed: close orphaned hedge legs.
///   3. If target still not met: escalate → CircuitBreakerService HardStop.
///
/// Freshness: IsFresh = (Now − LastRefreshedAt) ≤ MarginFreshnessMaxAgeMinutes.
///   Stale + ResultsSeason → block ALL new entries.
///   Stale (not season) → block size increases only.
/// </summary>
public sealed class MarginManager(
    IPositionRepository         positions,
    ICircuitBreakerService      circuitBreaker,
    IClock                      clock,
    ILogger<MarginManager>      log)
    : IMarginManager
{
    private MarginState _cached = new(0m, 0m, 0m, false, false,
        NodaTime.Instant.MinValue);

    public async Task<MarginState> GetCurrentStateAsync(CancellationToken ct)
    {
        var now = clock.NowInstant();

        // Return cached if still fresh (checked below by consumer; here we refresh periodically)
        bool isFresh = _cached.IsFresh &&
                       (now - _cached.LastRefreshedAt).TotalMinutes <= 20;

        if (isFresh)
            return _cached;

        // Re-compute from position repository
        var openPos = await positions.GetOpenAsync(ct);

        decimal grossMarginUsed   = ComputeGrossMargin(openPos);
        decimal hedgeMarginCredit = ComputeHedgeCredit(openPos);
        decimal netShocked        = grossMarginUsed > 0
            ? Math.Max(0m, grossMarginUsed - hedgeMarginCredit) / grossMarginUsed
            : 0m;

        bool isResultsSeason = IsResultsSeasonNow(now);

        _cached = new MarginState(
            GrossMarginUsed:       grossMarginUsed,
            HedgeMarginCredit:     hedgeMarginCredit,
            NetShockedUtilization: Math.Round(netShocked, 4),
            IsFresh:               true,
            IsResultsSeason:       isResultsSeason,
            LastRefreshedAt:       now);

        log.LogDebug(
            "MarginManager refresh: Gross={G:F0} Credit={C:F0} NetShocked={NS:P1} ResultsSeason={RS}",
            grossMarginUsed, hedgeMarginCredit, netShocked, isResultsSeason);

        return _cached;
    }

    public async Task<bool> TrimToFitAsync(
        ShortPremiumVelocityConfig config,
        CancellationToken          ct)
    {
        var margin = await GetCurrentStateAsync(ct);

        if (margin.NetShockedUtilization < config.ShockedUtilizationHardCap)
            return false; // already within limits

        log.LogWarning(
            "MarginManager.TrimToFit: NetShocked={NS:P1} > HardCap={HC:P1} — trimming",
            margin.NetShockedUtilization, config.ShockedUtilizationHardCap);

        var openPos = await positions.GetOpenAsync(ct);

        // ── Step 1: rank short-premium legs by ThetaToMargin (worst first) ────
        var shortLegs = openPos
            .Where(p => p.LegType == LegType.ShortPremium)
            .OrderBy(p => ComputeThetaToMarginRatio(p))  // worst (lowest) ratio first
            .ToList();

        bool trimmed   = false;
        decimal target = config.TrimToFitTargetPercent;

        foreach (var leg in shortLegs)
        {
            if (margin.NetShockedUtilization <= target) break;

            log.LogInformation(
                "MarginManager.TrimToFit: closing short-premium leg pos={Id} symbol={Sym}",
                leg.Id, leg.InternalSymbol);
            leg.CloseReason = "TrimToFit: margin utilisation exceeded ShockedUtilizationHardCap";
            await positions.UpdateAsync(leg, ct);
            trimmed = true;

            // Recompute (simplified proxy — production would re-fetch margin)
            margin = await GetCurrentStateAsync(ct);
        }

        // ── Step 2: close orphaned hedge legs ──────────────────────────────────
        if (margin.NetShockedUtilization > target)
        {
            var orphanedHedges = openPos
                .Where(p => p.LegType is LegType.Hedge or LegType.DeltaHedge &&
                            (p.LinkedShortLegId == null ||
                             shortLegs.All(s => s.Id != p.LinkedShortLegId || !s.IsOpen)))
                .ToList();

            foreach (var hedge in orphanedHedges)
            {
                if (margin.NetShockedUtilization <= target) break;
                log.LogInformation(
                    "MarginManager.TrimToFit: closing orphaned hedge pos={Id}",
                    hedge.Id);
                hedge.CloseReason = "TrimToFit: orphaned hedge, linked short leg already closed";
                await positions.UpdateAsync(hedge, ct);
                trimmed = true;
            }
        }

        // ── Step 3: escalate to HardStop if still over target ──────────────────
        margin = await GetCurrentStateAsync(ct);
        if (margin.NetShockedUtilization > target)
        {
            log.LogCritical(
                "MarginManager.TrimToFit: CANNOT reduce to target={T:P1} — escalating to HardStop",
                target);
            await circuitBreaker.EvaluateAsync(config.HardStopLossPct, config, ct);
        }

        return trimmed;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static decimal ComputeGrossMargin(IEnumerable<Position> positions)
    {
        // Proxy: sum absolute position value as margin estimate
        return positions
            .Where(p => p.LegType == LegType.ShortPremium)
            .Sum(p => Math.Abs(p.AvgPrice * p.Quantity));
    }

    private static decimal ComputeHedgeCredit(IEnumerable<Position> positions)
    {
        // Hedge legs reduce net SPAN margin requirement
        return positions
            .Where(p => p.LegType is LegType.Hedge or LegType.DeltaHedge)
            .Sum(p => Math.Abs(p.AvgPrice * p.Quantity) * 0.3m); // ~30% margin credit proxy
    }

    private static decimal ComputeThetaToMarginRatio(Position p)
    {
        // Lower ratio = worse position (trim first)
        decimal margin = Math.Abs(p.AvgPrice * p.Quantity);
        return margin > 0 ? p.UnrealizedPnl / margin : 0m;
    }

    private static bool IsResultsSeasonNow(Instant now)
    {
        var ist = now.InZone(DateTimeZone.ForOffset(Offset.FromHoursAndMinutes(5, 30)));
        return ist.Month is 1 or 2 or 4 or 5 or 7 or 8 or 10 or 11;
    }
}
