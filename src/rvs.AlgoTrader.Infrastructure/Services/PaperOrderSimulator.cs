using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Simulates order fills for paper-trading mode using live market data.
/// No real orders are placed. Fills at current price + slippage.
/// Paper P&amp;L is tracked in forward_test_trades (tagged with IsPaper=true).
/// Idempotency key prevents duplicate paper fills on retry.
/// </summary>
public sealed class PaperOrderSimulator(
    IForwardTestTradeRepository tradeRepo,
    ISlippageModel slippageModel,
    IClock clock) : IPaperOrderSimulator
{
    // Default slippage: 5 basis points percentage on each side
    private static readonly SlippageConfig DefaultSlippage = new(
        Domain.Enums.SlippageModelType.Percentage,
        Pct: 0.0005m);

    public async Task<PaperFillResult> SimulateFillAsync(
        StrategyInstance instance,
        SignalResult signal,
        decimal currentPrice,
        CancellationToken ct)
    {
        var direction  = signal.Signal == Domain.Enums.SignalType.Buy
            ? Domain.Enums.OrderDirection.Buy
            : Domain.Enums.OrderDirection.Sell;

        decimal fillPrice = slippageModel.ApplySlippage(
            currentPrice, direction, DefaultSlippage);

        // Lot size defaults to 1 for paper trading (broker credential no longer available)
        int lots     = 1;
        var now      = clock.NowInstant();
        var paperId  = $"PAPER-{instance.Id:N}-{now.ToUnixTimeTicks()}";

        // Persist as a forward-test trade so paper P&L appears in the same reporting flow
        var trade = new ForwardTestTrade
        {
            Id             = Guid.NewGuid(),
            SessionId      = instance.RuntimeState?.CurrentRunId ?? Guid.Empty,
            InternalSymbol = instance.InternalSymbol,
            Direction      = direction.ToString(),
            EntryPrice     = fillPrice,
            Quantity       = lots,
            EntryTime      = now
        };
        await tradeRepo.AddAsync(trade, ct);

        return new PaperFillResult(
            Filled:          true,
            FillPrice:       fillPrice,
            FilledQuantity:  lots,
            PaperOrderId:    paperId,
            FilledAt:        now.ToDateTimeOffset());
    }
}
