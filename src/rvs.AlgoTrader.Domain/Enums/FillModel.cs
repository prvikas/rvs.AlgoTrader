namespace rvs.AlgoTrader.Domain.Enums;

/// <summary>
/// Controls how backtest fills are simulated.
/// </summary>
public enum FillModel
{
    /// <summary>
    /// Fill at the open of the bar immediately after the signal bar.
    /// This is the default and most realistic for liquid large-cap Indian equities.
    /// Avoids lookahead bias — signal is generated at bar close, fill happens next open.
    /// </summary>
    NextBarOpen = 0,

    /// <summary>
    /// Fill at next bar open plus a configurable slippage in basis points.
    /// Use this when simulating real-world market impact for mid/small-cap names
    /// or larger position sizes where slippage is meaningful.
    /// </summary>
    NextBarOpenPlusSlippage = 1,

    /// <summary>
    /// Fill at the close of the signal bar.
    /// WARNING: This introduces lookahead bias for bar-close strategies.
    /// Only use for strategies that evaluate mid-bar and place limit orders
    /// that reliably execute before bar close (e.g. option selling at specific strikes).
    /// </summary>
    SignalBarClose = 2,
}
