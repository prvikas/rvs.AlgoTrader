using Microsoft.Extensions.DependencyInjection;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Strategies.AlertCandleShort;
using rvs.AlgoTrader.Strategies.CalendarSpread;
using rvs.AlgoTrader.Strategies.EmaVwapMomentum;
using rvs.AlgoTrader.Strategies.FibOptionSpread;
using rvs.AlgoTrader.Strategies.IntradayPcrOptions;
using rvs.AlgoTrader.Strategies.IronCondor;
using rvs.AlgoTrader.Strategies.PriceActionBreakout;
using rvs.AlgoTrader.Strategies.ShortStraddleStrangle;
using rvs.AlgoTrader.Strategies.GenericRules;
using rvs.AlgoTrader.Strategies.ShortPremiumVelocity;
using rvs.AlgoTrader.Strategies.SmaDualThreshold;
using rvs.AlgoTrader.Strategies.VcpSwing;
using rvs.AlgoTrader.Strategies.VerticalSpread;

namespace rvs.AlgoTrader.Strategies;

/// <summary>
/// Creates IStrategy instances by name, passing deserialized parameters_json.
///
/// Registration table:
/// ┌──────────────────────────┬───────────────────────────────────────────────────────────┐
/// │ Strategy Name            │ Description                                               │
/// ├──────────────────────────┼───────────────────────────────────────────────────────────┤
/// │ PriceActionBreakout      │ N-bar range breakout with ATR/volume filter               │
/// │ EmaVwapMomentum          │ EMA crossover + VWAP + BB + Volume                       │
/// │ AlertCandleShort         │ 5-EMA alert candle short (BankNifty/Nifty, 5min)         │
/// │ VcpSwing                 │ STRAT-001: VCP swing (daily equity, SMA200 + contractions)│
/// │ FibOptionSpread          │ STRAT-002: Fibonacci hedged option spread (credit spreads)│
/// │ IntradayPcrOptions       │ STRAT-003: Intraday PCR/OI/VWAP options (index options)  │
/// │ IronCondor               │ Short OTM call spread + short OTM put spread (4 legs)    │
/// │ ShortStraddleStrangle    │ Short ATM straddle or OTM strangle (2 legs, theta decay)  │
/// │ CalendarSpread           │ Sell near-expiry + buy far-expiry ATM (theta + vega)      │
/// │ VerticalSpread           │ Bull/Bear call/put spreads (4 types, debit or credit)     │
/// │ SmaDualThreshold         │ SMA20/50 dual-threshold trend (Nitin Joshi, 1Hr, skip 15:15)│
/// │ GenericRules             │ UI-defined strategy — any indicator + condition tree       │
/// │ ShortPremiumVelocity     │ STRAT-004: Short premium velocity (5-regime options seller)│
/// └──────────────────────────┴───────────────────────────────────────────────────────────┘
///
/// ShortPremiumVelocity uses ActivatorUtilities.CreateInstance so that DI-registered
/// engine services (ICircuitBreakerService, IJumpRiskMonitor, etc.) are injected automatically.
/// All other strategies are plain config-only constructions (no DI services).
/// </summary>
public class StrategyFactory(IServiceProvider serviceProvider) : IStrategyFactory
{
    public IStrategy Create(string strategyName, string? parametersJson)
    {
        return strategyName switch
        {
            "PriceActionBreakout"   => new PriceActionBreakoutStrategy(
                                           PriceActionBreakoutConfig.FromJson(parametersJson ?? "{}")),
            "EmaVwapMomentum"       => new EmaVwapMomentumStrategy(
                                           EmaVwapMomentumConfig.FromJson(parametersJson ?? "{}")),
            "AlertCandleShort"      => new AlertCandleShortStrategy(
                                           AlertCandleShortConfig.FromJson(parametersJson ?? "{}")),
            "VcpSwing"              => new VcpSwingStrategy(
                                           VcpSwingConfig.FromJson(parametersJson ?? "{}")),
            "FibOptionSpread"       => new FibOptionSpreadStrategy(
                                           FibOptionSpreadConfig.FromJson(parametersJson ?? "{}")),
            "IntradayPcrOptions"    => new IntradayPcrOptionsStrategy(
                                           IntradayPcrOptionsConfig.FromJson(parametersJson ?? "{}")),
            "IronCondor"            => new IronCondorStrategy(
                                           IronCondorConfig.FromJson(parametersJson ?? "{}")),
            "ShortStraddleStrangle" => new ShortStraddleStrangleStrategy(
                                           ShortStraddleStrangleConfig.FromJson(parametersJson ?? "{}")),
            "CalendarSpread"        => new CalendarSpreadStrategy(
                                           CalendarSpreadConfig.FromJson(parametersJson ?? "{}")),
            "VerticalSpread"        => new VerticalSpreadStrategy(
                                           VerticalSpreadConfig.FromJson(parametersJson ?? "{}")),
            "SmaDualThreshold"      => new SmaDualThresholdStrategy(
                                           SmaDualThresholdConfig.FromJson(parametersJson ?? "{}")),
            "GenericRules"          => new GenericRulesStrategy(
                                           GenericRulesConfig.FromJson(parametersJson ?? "{}")),

            // STRAT-004: ShortPremiumVelocity requires DI services — use ActivatorUtilities.
            // config is passed explicitly; all other constructor parameters resolved from container.
            "ShortPremiumVelocity"  => ActivatorUtilities.CreateInstance<ShortPremiumVelocityStrategy>(
                                           serviceProvider,
                                           ShortPremiumVelocityConfig.FromJson(parametersJson ?? "{}")),

            _ => throw new InvalidOperationException(
                $"Unknown strategy: '{strategyName}'. Registered: {string.Join(", ", GetRegisteredNames())}. " +
                $"Add the new strategy to StrategyFactory.cs.")
        };
    }

    public IEnumerable<string> GetRegisteredNames() =>
    [
        "PriceActionBreakout", "EmaVwapMomentum", "AlertCandleShort",
        "VcpSwing", "FibOptionSpread", "IntradayPcrOptions",
        "IronCondor", "ShortStraddleStrangle", "CalendarSpread", "VerticalSpread",
        "SmaDualThreshold",
        "GenericRules",
        "ShortPremiumVelocity",
    ];

    public IReadOnlyList<StrategyParamDef> GetParameterSchema(string strategyName)
        => strategyName switch
        {
            "PriceActionBreakout"   => PriceActionBreakoutConfig.GetSchema(),
            "EmaVwapMomentum"       => EmaVwapMomentumConfig.GetSchema(),
            "AlertCandleShort"      => AlertCandleShortConfig.GetSchema(),
            "VcpSwing"              => VcpSwingConfig.GetSchema(),
            "FibOptionSpread"       => FibOptionSpreadConfig.GetSchema(),
            "IntradayPcrOptions"    => IntradayPcrOptionsConfig.GetSchema(),
            "IronCondor"            => IronCondorConfig.GetSchema(),
            "ShortStraddleStrangle" => ShortStraddleStrangleConfig.GetSchema(),
            "CalendarSpread"        => CalendarSpreadConfig.GetSchema(),
            "VerticalSpread"        => VerticalSpreadConfig.GetSchema(),
            "SmaDualThreshold"      => SmaDualThresholdConfig.GetSchema(),
            "GenericRules"          => [],  // schema is free-form JSON (full UI strategy definition)
            "ShortPremiumVelocity"  => ShortPremiumVelocityConfig.GetSchema(),
            _ => throw new InvalidOperationException(
                $"Unknown strategy: '{strategyName}'. Registered: {string.Join(", ", GetRegisteredNames())}")
        };
}
