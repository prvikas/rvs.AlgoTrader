using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Domain.ValueObjects;

/// <summary>
/// IV Rank and IV Percentile for an underlying symbol, relative to its 52-week history.
///
/// IvRank = (CurrentIv - 52wLow) / (52wHigh - 52wLow) × 100
///   Ranges 0–100. High IvRank (>50) = IV elevated vs. recent range → favour short premium.
///
/// IvPercentile = % of trading days in the past year where IV was BELOW current IV.
///   More robust than IvRank for skewed distributions.
/// </summary>
public record IvRankSnapshot(
    string   UnderlyingSymbol,
    decimal  CurrentIv,
    decimal  IvRank,           // 0–100
    decimal  IvPercentile,     // 0–100
    decimal  WeekHigh52,
    decimal  WeekLow52,
    IvRegime Regime,
    int      DataPointsUsed
)
{
    public static IvRegime ClassifyRegime(decimal ivRank) => ivRank switch
    {
        < 25m  => IvRegime.Low,
        < 50m  => IvRegime.Normal,
        < 75m  => IvRegime.Elevated,
        _      => IvRegime.Extreme
    };
}
