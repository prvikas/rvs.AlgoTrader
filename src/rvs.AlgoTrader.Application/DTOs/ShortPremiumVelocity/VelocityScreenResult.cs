namespace rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;

/// <summary>
/// Result of the hard-gate + soft-filter screener.
/// All hard gates must pass before a new position is opened.
/// Soft violations reduce the aggression cap but do not block entry.
///
/// Passed:                  True when all hard gates passed and a new position may be opened.
/// FailedHardGates:         Names of hard gates that failed. Empty when Passed = true.
/// SoftViolations:          Advisory soft-filter violations that reduce the aggression cap.
/// SuggestedAggressionCap: Max aggression multiplier after soft-violation penalties.
/// RequiresMandatoryHedge: True when tail-risk score or regime requires a hedge before entry.
/// </summary>
public record VelocityScreenResult(
    bool                  Passed,
    IReadOnlyList<string> FailedHardGates,
    IReadOnlyList<string> SoftViolations,
    decimal               SuggestedAggressionCap,
    bool                  RequiresMandatoryHedge
);
