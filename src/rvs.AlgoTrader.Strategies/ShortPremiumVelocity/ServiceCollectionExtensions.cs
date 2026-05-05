using Microsoft.Extensions.DependencyInjection;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

/// <summary>
/// DI registrations for all Short Premium Velocity engine components.
///
/// Call services.AddShortPremiumVelocity() from the API or host startup
/// (src/rvs.AlgoTrader.API/Extensions/ServiceCollectionExtensions.cs).
///
/// Lifetime: all services are Singleton because:
///   - They hold in-memory rolling state (JumpRiskMonitor, CircuitBreakerService, RecoveryManager).
///   - IStrategy instances are long-lived (one per configured strategy run).
///   - No request-scoped state; cancellation is propagated via CancellationToken per call.
///
/// Multi-instance isolation: CircuitBreakerService and RecoveryManager maintain per-Guid
/// state buckets (ConcurrentDictionary keyed by StrategyInstanceId) so that two concurrent
/// forward-test instances do not bleed state into each other.
///
/// ISpvStateStore (Scoped) must be registered in the host BEFORE calling AddShortPremiumVelocity().
/// It is registered in rvs.AlgoTrader.API/Extensions/ServiceCollectionExtensions.cs.
///
/// StrategyFactory is registered separately in the API layer as IStrategyFactory → StrategyFactory.
/// This extension only registers the SPV sub-services so ActivatorUtilities can resolve
/// ShortPremiumVelocityStrategy when StrategyFactory.Create("ShortPremiumVelocity", ...) is called.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShortPremiumVelocity(this IServiceCollection services)
    {
        // ── Core engine components ────────────────────────────────────────────
        services.AddSingleton<ISyntheticOptionsPricer, SyntheticOptionsPricer>();
        services.AddSingleton<IVelocityScreener,      VelocityScreener>();
        services.AddSingleton<IVelocityIndicator,     VelocityIndicator>();
        services.AddSingleton<IStructureSelector,     StructureSelector>();
        services.AddSingleton<IDteOptimizer,          DteOptimizer>();
        services.AddSingleton<IRollDecisionEngine,    RollDecisionEngine>();
        services.AddSingleton<IHedgeEngine,           HedgeEngine>();
        services.AddSingleton<IHedgeEvaluator,        HedgeEvaluator>();
        services.AddSingleton<IRecoveryManager,       RecoveryManager>();
        services.AddSingleton<ICircuitBreakerService, CircuitBreakerService>();
        services.AddSingleton<IJumpRiskMonitor,       JumpRiskMonitor>();
        services.AddSingleton<IMarginManager,         MarginManager>();
        services.AddSingleton<IReinvestmentEngine,    ReinvestmentEngine>();
        services.AddSingleton<StressScenarioLibrary>();

        return services;
    }
}
