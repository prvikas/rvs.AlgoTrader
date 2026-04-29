namespace rvs.AlgoTrader.Domain.Constants;

/// <summary>
/// Canonical broker name strings used throughout the system.
/// Always reference these constants instead of raw string literals to prevent
/// silent routing failures caused by typos.
/// </summary>
public static class BrokerNames
{
    /// <summary>MStock (Mirae Asset) — default broker for Indian equities and derivatives.</summary>
    public const string MStock = "MStock";

    /// <summary>Zerodha — discount broker; requires manual TOTP re-auth daily.</summary>
    public const string Zerodha = "Zerodha";

    /// <summary>Upstox — discount broker; supports OAuth2 token auto-refresh.</summary>
    public const string Upstox = "Upstox";

    /// <summary>Fallback broker name used when no explicit broker is configured.</summary>
    public const string Default = MStock;
}
