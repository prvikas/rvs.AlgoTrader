using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Applies a slippage model to an ideal fill price.
/// Buy orders slip up (higher fill); sell orders slip down (lower fill).
/// Thread-safe: stateless.
/// </summary>
public sealed class SlippageModel : ISlippageModel
{
    public decimal ApplySlippage(
        decimal idealPrice,
        OrderDirection direction,
        SlippageConfig config,
        decimal? atr = null,
        long? volume = null)
    {
        if (idealPrice <= 0) return idealPrice;

        decimal slipAmount = config.ModelType switch
        {
            SlippageModelType.None         => 0m,
            SlippageModelType.Fixed        => config.FixedPoints,
            SlippageModelType.Percentage   => idealPrice * config.Pct,
            SlippageModelType.BidAsk       => idealPrice * config.BidAskSpreadPct / 2m,
            SlippageModelType.AtrFraction  => (atr ?? 0m) * config.AtrMultiplier,
            SlippageModelType.VolumeImpact => ComputeVolumeImpact(idealPrice, volume, config),
            _ => 0m
        };

        // Buy: price goes up; Sell: price goes down
        return direction == OrderDirection.Buy
            ? idealPrice + slipAmount
            : idealPrice - slipAmount;
    }

    private static decimal ComputeVolumeImpact(decimal price, long? volume, SlippageConfig config)
    {
        // Simplified Almgren–Chriss: impact ∝ 1 / sqrt(volume)
        // Clip at 2% of price to avoid extreme values on illiquid instruments
        if (volume is null or 0) return price * 0.002m;
        double impact = (double)config.VolumeImpactPct / Math.Sqrt((double)volume.Value);
        return Math.Min((decimal)impact * price, price * 0.02m);
    }
}
