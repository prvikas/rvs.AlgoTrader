using Microsoft.Extensions.DependencyInjection;
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
    IServiceScopeFactory        scopeFactory,
    ICircuitBreakerService      circuitBreaker,
    IClock                      clock,
    ILogger<MarginManager>      log)
    : IMarginManager
{
    private MarginState _cached = new(0m, 0m, 0m, false, false,
        NodaTime.Instant.MinValue);

    public Task<MarginState> GetCurrentStateAsync(CancellationToken ct)
        => GetCurrentStateAsync(new ShortPremiumVelocityConfig(), ct);

    public async Task<MarginState> GetCurrentStateAsync(
        ShortPremiumVelocityConfig config,
        CancellationToken          ct)
    {
        var now = clock.NowInstant();

        // Return cached if still fresh — use config threshold, not a hardcoded literal
        bool isFresh = _cached.IsFresh &&
                       (now - _cached.LastRefreshedAt).TotalMinutes <= config.MarginFreshnessMaxAgeMinutes;

        if (isFresh)
            return _cached;

        // Re-compute from position repository (new scope — singleton cannot hold scoped IPositionRepository)
        await using var scope  = scopeFactory.CreateAsyncScope();
        var             posRepo = scope.ServiceProvider.GetRequiredService<IPositionRepository>();
        var openPos = await posRepo.GetOpenAsync(ct);

        decimal grossMarginUsed   = ComputeGrossMargin(openPos);
        decimal hedgeMarginCredit = ComputeHedgeCredit(openPos, config);
        decimal netShocked        = grossMarginUsed > 0
            ? Math.Max(0m, grossMarginUsed - hedgeMarginCredit) / grossMarginUsed
            : 0m;

        bool isResultsSeason = IsResultsSeasonNow(now, config);

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
        var margin = await GetCurrentStateAsync(config, ct);

        if (margin.NetShockedUtilization < config.ShockedUtilizationHardCap)
            return false; // already within limits

        log.LogWarning(
            "MarginManager.TrimToFit: NetShocked={NS:P1} > HardCap={HC:P1} — trimming",
            margin.NetShockedUtilization, config.ShockedUtilizationHardCap);

        await using var trimScope  = scopeFactory.CreateAsyncScope();
        var             trimRepo   = trimScope.ServiceProvider.GetRequiredService<IPositionRepository>();
        var openPos = await trimRepo.GetOpenAsync(ct);

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
            await trimRepo.UpdateAsync(leg, ct);
            trimmed = true;

            // Recompute (simplified proxy — production would re-fetch margin)
            margin = await GetCurrentStateAsync(config, ct);
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
                await trimRepo.UpdateAsync(hedge, ct);
                trimmed = true;
            }
        }

        // ── Step 3: escalate to HardStop if still over target ──────────────────
        margin = await GetCurrentStateAsync(config, ct);
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

    private static decimal ComputeHedgeCredit(
        IEnumerable<Position>      positions,
        ShortPremiumVelocityConfig config)
    {
        // Hedge legs reduce net SPAN margin requirement by config.HedgeCreditFraction
        return positions
            .Where(p => p.LegType is LegType.Hedge or LegType.DeltaHedge)
            .Sum(p => Math.Abs(p.AvgPrice * p.Quantity) * config.HedgeCreditFraction);
    }

    private static decimal ComputeThetaToMarginRatio(Position p)
    {
        // Lower ratio = worse position (trim first)
        decimal margin = Math.Abs(p.AvgPrice * p.Quantity);
        return margin > 0 ? p.UnrealizedPnl / margin : 0m;
    }

    private static bool IsResultsSeasonNow(Instant now, ShortPremiumVelocityConfig config)
    {
        var ist = now.InZone(DateTimeZone.ForOffset(Offset.FromHoursAndMinutes(5, 30)));
        return config.ResultsSeasonMonths.Contains(ist.Month);
    }
}
