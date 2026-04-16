namespace rvs.AlgoTrader.Infrastructure.Options;

/// <summary>
/// Feature-flag options bound from the "Features" config section.
/// When BrokerRequired is false the system runs in backtest-only (offline) mode:
///   • broker login endpoints return HTTP 423
///   • Forward and Live strategy modes are blocked at startup
///   • StartupOrchestrator Step 7 (broker re-auth) is skipped
/// </summary>
public sealed class FeaturesOptions
{
    public const string SectionName = "Features";

    /// <summary>
    /// True (default): normal operation — broker login required for Forward/Live modes.
    /// False: backtest-only mode — no broker connection needed; only Backtest mode allowed.
    /// </summary>
    public bool BrokerRequired { get; init; } = true;
}
