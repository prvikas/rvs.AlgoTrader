namespace rvs.AlgoTrader.Domain.ValueObjects;

/// <summary>
/// Six-label technical regime classification used by the P10-B Quant Regime Engine.
/// Distinct from MarketRegime (breadth-based Bull/Neutral/Bear) — this is a
/// volatility + momentum regime classifier that drives strategy selection and sizing.
/// </summary>
public enum QuantRegimeLabel
{
    /// <summary>ADX > 25 with bullish DI alignment. Trend-following strategies have positive EV.</summary>
    TrendingUp,

    /// <summary>ADX > 25 with bearish DI alignment. Short/put strategies have positive EV.</summary>
    TrendingDown,

    /// <summary>ADX &lt; 20, stable ATR. Mean-reversion and range-bound strategies preferred.</summary>
    Choppy,

    /// <summary>VIX > 20 or IVP > 70 or ATR expanding. Premium-selling structures have positive EV.</summary>
    ElevatedVolatility,

    /// <summary>VIX > 28 or VIX spike > 20% in 5 days. Reduce all size; avoid new short-gamma.</summary>
    PanicShock,

    /// <summary>ADX &lt; 15, ATR contracting, IVP cheap. Breakout watch; long-gamma positions are cheapest.</summary>
    CompressionWatch,
}

/// <summary>
/// Traffic light for signal quality — applied to the current regime.
/// Frontend renders as a colored dot beside strategy names and in the Quant Lab.
/// </summary>
public enum TrafficLight
{
    /// <summary>Conditions aligned — proceed with standard sizing.</summary>
    Green,

    /// <summary>Partial alignment or mixed signals — reduce size or wait for confirmation.</summary>
    Amber,

    /// <summary>Avoid or significantly reduce — conditions unfavourable for most strategies.</summary>
    Red,
}

/// <summary>
/// A single factor that contributed to the regime classification — shown in the
/// explanation panel so traders understand why the engine landed on a regime.
/// </summary>
// Impact values: "supporting" | "contradicting" | "neutral"
public record QuantRegimeFactor(
    string Name,
    string Value,
    string Interpretation,
    string Impact
);

/// <summary>
/// Full result returned by the regime engine.
/// </summary>
// Confidence: 0–100 score derived from how many factors align with the primary regime.
public record QuantRegimeResult(
    QuantRegimeLabel  Regime,
    TrafficLight      TrafficLight,
    int               Confidence,
    string            Summary,
    string            StrategyGuidance,
    IReadOnlyList<QuantRegimeFactor> ContributingFactors,
    string?           DataWarning = null
);
