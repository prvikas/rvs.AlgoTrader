namespace rvs.AlgoTrader.Application.DTOs.Options;

/// <summary>One row in the option chain table — CE and PE for a single strike.</summary>
public record OptionStrikeDto(
    decimal Strike,
    long    CeOI,
    long    CeDeltaOI,
    decimal CeLTP,
    decimal CeIV,
    long    PeOI,
    long    PeDeltaOI,
    decimal PeLTP,
    decimal PeIV,
    bool    IsAtm);

/// <summary>Full option chain with derived analytics for one symbol + expiry.</summary>
public record OptionChainDto(
    string                        Symbol,
    string                        Expiry,
    decimal                       SpotPrice,
    decimal                       Pcr,
    decimal                       PcrMax,
    decimal                       PinIV,
    decimal                       TotalIV,
    long                          TotalCeOI,
    long                          TotalPeOI,
    decimal                       MaxPainStrike,
    decimal                       AtmStrike,
    IReadOnlyList<OptionStrikeDto> Strikes,
    string                        FetchedAt);

/// <summary>One signal from the rule engine — triggered or not, with reason and context.</summary>
public record OptionsRuleSignalDto(
    string   RuleId,
    string   RuleName,
    bool     Triggered,
    string   Severity,   // "info" | "warn" | "alert"
    string   Reason,
    decimal? Value,
    decimal? Threshold);

/// <summary>Full intelligence result — chain + rule signals + bias.</summary>
public record OptionsIntelligenceDto(
    OptionChainDto                     Snapshot,
    IReadOnlyList<OptionsRuleSignalDto> Signals,
    string                             OverallBias,  // "Bullish" | "Bearish" | "Neutral"
    decimal                            BiasScore,    // -100 to +100
    string                             LastUpdated);
