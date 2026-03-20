using rvs.AlgoTrader.Application.Services;
namespace rvs.AlgoTrader.Infrastructure.Services;

public sealed class TransactionCostCalculator : ITransactionCostCalculator
{
    public TransactionCosts Calculate(decimal tradeValue, bool isBuy, CostProfile profile)
    {
        var brokerage = tradeValue * profile.BrokeragePct;
        var stt = isBuy ? 0m : tradeValue * profile.SttPct; // STT on sell side for intraday equity
        var gst = brokerage * profile.GstPct;
        var sebi = tradeValue * profile.SebiChargesPct;
        var stamp = isBuy ? tradeValue * profile.StampDutyPct : 0m; // Stamp duty on buy side only
        var slippage = tradeValue * profile.SlippagePct;
        return new TransactionCosts(brokerage, stt, gst, sebi, stamp, slippage);
    }
}
