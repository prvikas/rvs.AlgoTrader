namespace rvs.AlgoTrader.Domain.Constants;

/// <summary>
/// ISO-style market codes.  Add new markets here as support is added.
/// Brokers declare which markets they support via IFullBrokerClient.SupportedMarkets.
/// </summary>
public static class MarketCodes
{
    /// <summary>Indian equity + derivatives markets (NSE / BSE).</summary>
    public const string India = "IN";

    /// <summary>United States equity + options markets (NYSE / NASDAQ).</summary>
    public const string UnitedStates = "US";

    /// <summary>United Kingdom equity markets (LSE).</summary>
    public const string UnitedKingdom = "UK";

    /// <summary>Singapore Exchange.</summary>
    public const string Singapore = "SG";
}
