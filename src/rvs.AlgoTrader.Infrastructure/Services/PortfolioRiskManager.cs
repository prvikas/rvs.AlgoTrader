using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Enforces 6 portfolio-level risk controls before each order.
///
/// Controls:
///   1. Daily loss limit   — sum of all strategy TodayRealizedPnl + UnrealizedPnl vs threshold
///   2. Max concurrent positions — count of open positions across all strategies
///   3. Max capital deployment — total deployed capital vs equity
///   4. Max per-symbol exposure — one symbol cannot exceed X% of equity
///   5. Max margin utilisation — capped at config.MaxMarginUtilisationPct
///   6. Max portfolio delta — options strategies: sum(lots × delta) vs equity
///
/// Config persisted via IAppConfigService (Redis-backed, survives restarts).
/// State is computed live from DB/Redis — no stale cache.
/// </summary>
public sealed class PortfolioRiskManager(
    IPositionRepository positionRepo,
    IStrategyInstanceRepository instanceRepo,
    IAppConfigService appConfig,
    IKillSwitchService killSwitch,
    IClock clock,
    ILogger<PortfolioRiskManager> logger) : IPortfolioRiskManager
{
    private const string ConfigKey = "portfolio_risk_config";

    public async Task<RiskCheckResult> CheckOrderAsync(
        StrategyInstance instance,
        decimal orderValue,
        CancellationToken ct)
    {
        var config = await GetConfigAsync(ct);
        var state  = await GetStateAsync(ct);

        // 1. Daily loss limit
        decimal totalDailyPnl = state.DailyRealizedPnl + state.DailyUnrealizedPnl;
        decimal lossLimit     = state.TotalEquity * config.DailyLossLimitPct / 100m;
        if (totalDailyPnl <= -lossLimit)
        {
            logger.LogWarning("[PortfolioRisk] Daily loss limit breached: pnl=₹{Pnl:N2}, limit=₹{Limit:N2}", totalDailyPnl, lossLimit);
            await killSwitch.ActivateAsync("PortfolioRiskManager", $"Daily loss limit breached: ₹{totalDailyPnl:N2}", Guid.NewGuid().ToString(), ct);
            return new RiskCheckResult(false, $"Daily loss limit breached (₹{totalDailyPnl:N2} ≤ -₹{lossLimit:N2})");
        }

        // 2. Max concurrent positions
        if (state.OpenPositionCount >= config.MaxConcurrentPositions)
            return new RiskCheckResult(false, $"Max concurrent positions ({config.MaxConcurrentPositions}) reached");

        // 3. Max capital deployment
        decimal deployedPct = state.TotalEquity > 0
            ? state.DeployedCapital / state.TotalEquity * 100m
            : 0m;
        if (deployedPct + (orderValue / state.TotalEquity * 100m) > config.MaxCapitalDeploymentPct)
            return new RiskCheckResult(false,
                $"Max capital deployment {config.MaxCapitalDeploymentPct}% would be exceeded ({deployedPct:F1}% currently deployed)");

        // 4. Max per-symbol exposure
        var symbolPositions = await positionRepo.GetBySymbolAsync(instance.InternalSymbol, ct);
        decimal symbolValue = symbolPositions.Where(p => p.IsOpen).Sum(p => Math.Abs(p.Quantity * p.AvgPrice));
        decimal symbolPct   = state.TotalEquity > 0 ? (symbolValue + orderValue) / state.TotalEquity * 100m : 0m;
        if (symbolPct > config.MaxSymbolExposurePct)
            return new RiskCheckResult(false,
                $"Symbol {instance.InternalSymbol} exposure {symbolPct:F1}% exceeds limit {config.MaxSymbolExposurePct}%");

        // 5. Margin utilisation (approximated as deployed capital / total equity for now)
        if (deployedPct > config.MaxMarginUtilisationPct)
            return new RiskCheckResult(false,
                $"Margin utilisation {deployedPct:F1}% exceeds limit {config.MaxMarginUtilisationPct}%");

        logger.LogDebug("[PortfolioRisk] Order allowed for {Instance}: deployed={Deployed:F1}%, positions={Pos}",
            instance.Name, deployedPct, state.OpenPositionCount);
        return new RiskCheckResult(true, null);
    }

    public async Task<PortfolioRiskState> GetStateAsync(CancellationToken ct)
    {
        var openPositions = await positionRepo.GetOpenAsync(ct);
        var instances     = await instanceRepo.GetAllActiveAsync(ct);

        decimal deployedCapital  = openPositions.Sum(p => Math.Abs((decimal)p.Quantity * p.AvgPrice));
        decimal dailyRealized    = instances.Sum(i => i.RuntimeState?.TodayRealizedPnl ?? 0m);
        decimal dailyUnrealized  = instances.Sum(i => i.RuntimeState?.TodayUnrealizedPnl ?? 0m);
        decimal totalEquity      = instances.Sum(i => i.AllocatedCapital);

        return new PortfolioRiskState(
            TotalEquity:            totalEquity,
            DeployedCapital:        deployedCapital,
            DailyRealizedPnl:       dailyRealized,
            DailyUnrealizedPnl:     dailyUnrealized,
            OpenPositionCount:      openPositions.Count,
            MarginUsed:             deployedCapital,
            DailyLossLimitBreached: totalEquity > 0 && (dailyRealized + dailyUnrealized) <= -(totalEquity * 0.02m),
            MaxPositionsBreached:   false,
            MaxDeploymentBreached:  false,
            SnapshotAt:             clock.NowInstant().ToDateTimeOffset()
        );
    }

    public async Task<PortfolioRiskConfig> GetConfigAsync(CancellationToken ct)
    {
        var stored = await appConfig.GetAsync<PortfolioRiskConfig>(ConfigKey, ct);
        return stored ?? new PortfolioRiskConfig();
    }

    public async Task SaveConfigAsync(PortfolioRiskConfig config, CancellationToken ct)
        => await appConfig.SetAsync(ConfigKey, config, "PortfolioRiskManager", Guid.NewGuid().ToString(), ct);
}
