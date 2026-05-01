namespace rvs.AlgoTrader.Domain.Enums;

/// <summary>
/// Identifies the protective purpose of a hedge leg placed alongside a short-premium position.
/// Stored on Position.HedgeType to allow attribution, P&amp;L netting, and roll logic
/// to treat each hedge type differently without a separate table.
/// </summary>
public enum HedgeType
{
    /// <summary>Long put bought to cap downside on a short-call or short-straddle leg.</summary>
    ProtectivePut,

    /// <summary>Long call debit spread (buy ATM/OTM call, sell further OTM call) to hedge a short-put side.</summary>
    CallDebitSpread,

    /// <summary>Options position taken specifically to reduce net vega exposure of the book.</summary>
    VegaHedge,

    /// <summary>Position taken to neutralise delta exposure, typically a futures contract or spot position.</summary>
    DeltaHedge,

    /// <summary>Deep OTM put or put spread bought for low-probability tail-risk protection.</summary>
    TailRiskHedge,
}
