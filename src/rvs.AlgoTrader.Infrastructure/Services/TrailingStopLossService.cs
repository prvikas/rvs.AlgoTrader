using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Services;

public sealed class TrailingStopLossService(
    IPositionRepository positions,
    IClock clock) : ITrailingStopLossService
{

    public async Task<decimal?> UpdateTrailingStopAsync(Position position, decimal currentPrice, TrailingSLConfig config, CancellationToken ct)
    {
        if (position.StopLoss is null) return null;

        var entryPrice = position.AvgPrice;
        var pnlPct = (currentPrice - entryPrice) / entryPrice;

        // Only activate trailing SL once profit reaches activation threshold
        if (pnlPct < config.ActivationPct) return position.StopLoss;

        // New SL = currentPrice * (1 - stepPct)  [for long positions]
        // SL never moves backwards (never lower than current SL for long)
        var newSl = currentPrice * (1m - config.StepPct);
        if (newSl <= position.StopLoss) return position.StopLoss; // Never regress

        position.UpdateStopLoss(newSl, clock.NowInstant());
        await positions.UpdateAsync(position, ct);
        return newSl;
    }

    /// <inheritdoc/>
    public TrailingStopState UpdateStop(TrailingStopState state, decimal currentPrice)
    {
        var entryPrice = state.EntryPrice;
        var activationThreshold = state.ActivationThresholdPercent / 100m;
        var trailStep = state.TrailStepPercent / 100m;

        if (state.Direction == "BUY")
        {
            var gainPct = (currentPrice - entryPrice) / entryPrice;
            if (gainPct < activationThreshold)
                return state; // Not yet activated

            var newStop = currentPrice * (1m - trailStep);
            if (newStop <= state.CurrentStop)
                return state; // Never regress

            return state with { CurrentStop = newStop };
        }
        else // SELL / short
        {
            var gainPct = (entryPrice - currentPrice) / entryPrice;
            if (gainPct < activationThreshold)
                return state; // Not yet activated

            var newStop = currentPrice * (1m + trailStep);
            if (newStop >= state.CurrentStop)
                return state; // Never regress for short

            return state with { CurrentStop = newStop };
        }
    }
}
