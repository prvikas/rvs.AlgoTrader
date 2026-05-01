namespace rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;

/// <summary>Action that IRollDecisionEngine recommends for an open position.</summary>
public enum RollAction
{
    /// <summary>Roll to a new expiry (close current, open fresh at target DTE).</summary>
    Roll,

    /// <summary>Close the position outright, take the P&amp;L, and do not re-enter immediately.</summary>
    Close,

    /// <summary>No action — hold the position through the current expiry.</summary>
    Hold,
}

/// <summary>
/// Immutable result from IRollDecisionEngine describing whether to roll, close, or hold
/// an open SPV position, with blocking-gate traceability.
///
/// BlockingGates: Names of hard gates that blocked rolling (empty when Action = Roll or Hold freely).
/// Reason:        Human-readable explanation for logging and the dashboard.
/// TargetDte:     Target DTE for the rolled position. Null when Action = Close or Hold.
/// </summary>
public record RollDecision(
    RollAction            Action,
    IReadOnlyList<string> BlockingGates,
    string                Reason,
    int?                  TargetDte
);
