using NodaTime;

namespace rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;

/// <summary>
/// Current broker-reported margin state, including the shocked utilisation metric
/// that accounts for a 2σ adverse move on all open positions.
///
/// GrossMarginUsed:       Raw margin used by all open positions (SPAN + exposure).
/// HedgeMarginCredit:     Margin credit provided by hedging positions (reduces net requirement).
/// NetShockedUtilization: Net margin utilisation after applying a 2σ shock scenario (0–1+).
///                        Compared against ShortPremiumVelocityConfig.ShockedUtilizationHardCap.
/// IsFresh:               True when margin data was fetched within the freshness window.
/// IsResultsSeason:       True when current date is in a results-season month.
/// </summary>
public record MarginState(
    decimal GrossMarginUsed,
    decimal HedgeMarginCredit,
    decimal NetShockedUtilization,
    bool    IsFresh,
    bool    IsResultsSeason,
    Instant LastRefreshedAt
);
