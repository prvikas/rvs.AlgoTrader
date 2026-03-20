using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Strategies.PriceActionBreakout;

namespace rvs.AlgoTrader.Strategies;

/// <summary>
/// Creates IStrategy instances by name, passing deserialized parameters_json.
/// Add new strategy registrations here as they are implemented.
/// </summary>
public class StrategyFactory : IStrategyFactory
{
    public IStrategy Create(string strategyName, string? parametersJson)
    {
        return strategyName switch
        {
            "PriceActionBreakout" => new PriceActionBreakoutStrategy(
                PriceActionBreakoutConfig.FromJson(parametersJson ?? "{}")),

            _ => throw new InvalidOperationException($"Unknown strategy: '{strategyName}'. Register it in StrategyFactory.")
        };
    }

    public IEnumerable<string> GetRegisteredNames()
        => ["PriceActionBreakout"];
}
