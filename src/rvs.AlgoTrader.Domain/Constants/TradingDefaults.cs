namespace rvs.AlgoTrader.Domain.Constants;

/// <summary>
/// Centralised default values for trading configuration.
/// Reference these constants instead of scattering magic numbers across services.
/// </summary>
public static class TradingDefaults
{
    /// <summary>Default initial capital when none is specified for a strategy session (₹).</summary>
    public const decimal DefaultCapital = 100_000m;

    /// <summary>Default risk per trade as a percentage of capital.</summary>
    public const decimal RiskPerTradePct = 1.0m;

    /// <summary>Maximum number of closed candles kept in the Redis candle cache per symbol/timeframe.</summary>
    public const int CandleCacheSize = 500;

    /// <summary>Default lot size (number of shares/contracts) when the instrument lot size is not configured.</summary>
    public const int DefaultLotSize = 1;

    /// <summary>Length of a generated backtest job ID (hex characters from a GUID).</summary>
    public const int JobIdLength = 12;

    /// <summary>TTL for per-instance capital reservation keys in Redis (hours, aligned to IST trading session).</summary>
    public const int CapitalTtlHours = 12;

    /// <summary>Default slippage in basis points for paper/forward-test fill simulation.</summary>
    public const int DefaultSlippageBps = 5;

    /// <summary>Default brokerage flat fee per side for backtest cost simulation (₹).</summary>
    public const decimal DefaultBrokerageFlatPerSide = 20m;

    /// <summary>Empty JSON object literal — use instead of the raw string "{}" to avoid typos.</summary>
    public const string EmptyJson = "{}";
}
