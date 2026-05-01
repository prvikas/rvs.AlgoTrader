namespace rvs.AlgoTrader.Domain.Enums;

/// <summary>
/// Tags every leg of a multi-leg short-premium position so hedge legs can be
/// attributed, net-costed, and rolled independently without a separate DB table.
/// </summary>
public enum LegType
{
    /// <summary>The primary premium-selling leg (short straddle, short strangle, or credit-spread short side).</summary>
    ShortPremium,

    /// <summary>A protective hedge leg paired with a ShortPremium leg (put, call debit spread, vega hedge, tail-risk).</summary>
    Hedge,

    /// <summary>A dynamic delta-neutralisation leg (futures or spot) adjusted intraday as delta drifts.</summary>
    DeltaHedge,
}
