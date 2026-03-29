using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Strategies.AlertCandleShort;
using rvs.AlgoTrader.Strategies.EmaVwapMomentum;
using rvs.AlgoTrader.Strategies.PriceActionBreakout;

namespace rvs.AlgoTrader.Strategies;

/// <summary>
/// Creates IStrategy instances by name, passing deserialized parameters_json.
///
/// Registration table — add new strategies here:
/// ┌────────────────────────────────┬───────────────────────────────────────────────────┐
/// │ Strategy Name                  │ Description                                       │
/// ├────────────────────────────────┼───────────────────────────────────────────────────┤
/// │ PriceActionBreakout            │ N-bar range breakout with ATR/volume filter        │
/// │ EmaVwapMomentum                │ EMA crossover + VWAP + BB + Volume + OC (optional)│
/// │ AlertCandleShort               │ 5-EMA alert candle short (BankNifty/Nifty, 5min)  │
/// └────────────────────────────────┴───────────────────────────────────────────────────┘
/// </summary>
public class StrategyFactory : IStrategyFactory
{
    public IStrategy Create(string strategyName, string? parametersJson)
    {
        return strategyName switch
        {
            "PriceActionBreakout" => new PriceActionBreakoutStrategy(
                PriceActionBreakoutConfig.FromJson(parametersJson ?? "{}")),

            "EmaVwapMomentum" => new EmaVwapMomentumStrategy(
                EmaVwapMomentumConfig.FromJson(parametersJson ?? "{}")),

            "AlertCandleShort" => new AlertCandleShortStrategy(
                AlertCandleShortConfig.FromJson(parametersJson ?? "{}")),

            _ => throw new InvalidOperationException(
                $"Unknown strategy: '{strategyName}'. Registered strategies: {string.Join(", ", GetRegisteredNames())}. " +
                $"Add the new strategy to StrategyFactory.cs.")
        };
    }

    public IEnumerable<string> GetRegisteredNames()
        => ["PriceActionBreakout", "EmaVwapMomentum", "AlertCandleShort"];

    public IReadOnlyList<StrategyParamDef> GetParameterSchema(string strategyName)
        => strategyName switch
        {
            "PriceActionBreakout" => PriceActionBreakoutConfig.GetSchema(),
            "EmaVwapMomentum"     => EmaVwapMomentumConfig.GetSchema(),
            "AlertCandleShort"    => AlertCandleShortConfig.GetSchema(),
            _ => throw new InvalidOperationException(
                $"Unknown strategy: '{strategyName}'. Registered: {string.Join(", ", GetRegisteredNames())}")
        };
}
