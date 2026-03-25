using rvs.AlgoTrader.Application.Services;
namespace rvs.AlgoTrader.Infrastructure.Services;

public sealed class TransactionCostCalculator : ITransactionCostCalculator
{
    public TransactionCosts Calculate(decimal tradeValue, bool isBuy, CostProfile profile)
    {
        // Flat brokerage takes priority over percentage-based brokerage.
        // Indian discount brokers (Zerodha, Upstox) charge ₹20/order or 0.03% — whichever is lower.
        // When BrokerageFlatPerSide > 0, use flat rate (already per-side, no need to halve).
        decimal brokerage;
        if (profile.BrokerageFlatPerSide > 0)
        {
            // Flat ₹ per order leg (e.g. ₹20 for Zerodha/Upstox)
            brokerage = profile.BrokerageFlatPerSide;
        }
        else
        {
            brokerage = tradeValue * profile.BrokeragePct;
        }

        var stt = isBuy ? 0m : tradeValue * profile.SttPct; // STT on sell side for intraday equity
        var gst = brokerage * profile.GstPct;
        var sebi = tradeValue * profile.SebiChargesPct;
        var stamp = isBuy ? tradeValue * profile.StampDutyPct : 0m; // Stamp duty on buy side only

        // SlippagePct-based slippage (spread/market-impact proxy)
        // SlippageBasisPoints is handled at the engine level (applied to fill price directly)
        var slippage = tradeValue * profile.SlippagePct;

        return new TransactionCosts(brokerage, stt, gst, sebi, stamp, slippage);
    }
}
