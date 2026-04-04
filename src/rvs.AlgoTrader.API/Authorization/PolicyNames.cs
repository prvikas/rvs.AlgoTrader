namespace rvs.AlgoTrader.API.Authorization;

/// <summary>
/// Authorization policy names used with [Authorize(Policy = "...")] on controllers.
///
/// Role hierarchy (each tier implies all lower tiers):
///   SuperAdmin > Admin > RiskManager > Trader > Analyst > Viewer
///
/// JWT claim: "role" (single value, e.g. "Trader")
/// </summary>
public static class PolicyNames
{
    /// <summary>Any authenticated user. Read-only market data, instrument universe, health checks.</summary>
    public const string Viewer = "Viewer";

    /// <summary>Can run backtests, view analytics, export reports.</summary>
    public const string Analyst = "Analyst";

    /// <summary>Can place/cancel orders, start/stop strategies, manage paper trades.</summary>
    public const string Trader = "Trader";

    /// <summary>Can modify risk profiles, approve strategies, activate kill switch.</summary>
    public const string RiskManager = "RiskManager";

    /// <summary>Can manage broker credentials, strategy configurations, system settings.</summary>
    public const string Admin = "Admin";

    /// <summary>Full access including user management and infrastructure changes.</summary>
    public const string SuperAdmin = "SuperAdmin";
}
