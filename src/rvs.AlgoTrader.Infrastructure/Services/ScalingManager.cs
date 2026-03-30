using System.Collections.Concurrent;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Manages scaled entry (pyramid) and partial exit for positions.
///
/// VCP default:  tranche[0] = 70 lots pct at initial entry
///               tranche[1] = 30 lots pct when price bounces off EMA21
///
/// Each call to Evaluate() checks the next pending tranche trigger.
/// RecordFill() advances the tranche counter and updates average price.
///
/// Singleton-safe: state held in ConcurrentDictionary keyed by positionId.
/// </summary>
public sealed class ScalingManager(IClock clock) : IScalingManager
{
    private readonly ConcurrentDictionary<Guid, ScalingState> _state = new();

    public ScalingState Register(
        Guid positionId,
        ScalingConfig config,
        decimal initialEntryPrice,
        int initialLots)
    {
        var firstExec = new TrancheExecution(0, initialEntryPrice, initialLots, clock.NowInstant().ToDateTimeOffset());
        var state = new ScalingState(
            PositionId:        positionId,
            Config:            config,
            CurrentTranche:    0,
            AverageEntryPrice: initialEntryPrice,
            TotalLots:         initialLots,
            ExecutedTranches:  [firstExec]);

        _state[positionId] = state;
        return state;
    }

    public (bool ShouldTrigger, int Lots, string Reason) Evaluate(
        Guid positionId, decimal currentPrice, decimal? ema = null, decimal? atr = null)
    {
        if (!_state.TryGetValue(positionId, out var state))
            return (false, 0, "No scaling state registered");

        int nextIdx = state.CurrentTranche + 1;
        if (nextIdx >= state.Config.Tranches.Count)
            return (false, 0, "All tranches already executed");

        if (state.Config.Mode == ScalingMode.None)
            return (false, 0, "ScalingMode.None — no auto-scaling");

        var tranche = state.Config.Tranches[nextIdx];
        var (triggered, reason) = CheckTrigger(tranche, currentPrice, state, ema, atr);
        if (!triggered) return (false, 0, reason);

        // Compute lot count from LotsPct of total planned lots
        int totalPlannedLots = state.Config.Tranches.Sum(t => t.LotsPct);
        int lots = totalPlannedLots > 0
            ? (int)Math.Round(state.TotalLots * tranche.LotsPct / (decimal)totalPlannedLots)
            : 1;

        return (true, Math.Max(1, lots), reason);
    }

    public ScalingState RecordFill(Guid positionId, decimal fillPrice, int filledLots)
    {
        if (!_state.TryGetValue(positionId, out var state)) return null!;

        int newTranche = state.CurrentTranche + 1;
        int newTotal   = state.TotalLots + filledLots;
        // Weighted average price
        decimal newAvg = (state.AverageEntryPrice * state.TotalLots + fillPrice * filledLots) / newTotal;

        var exec = new TrancheExecution(newTranche, fillPrice, filledLots, clock.NowInstant().ToDateTimeOffset());
        var updated = state with
        {
            CurrentTranche    = newTranche,
            AverageEntryPrice = newAvg,
            TotalLots         = newTotal,
            ExecutedTranches  = state.ExecutedTranches.Append(exec).ToList()
        };
        _state[positionId] = updated;
        return updated;
    }

    public void Unregister(Guid positionId) => _state.TryRemove(positionId, out _);

    public ScalingState? Get(Guid positionId)
        => _state.TryGetValue(positionId, out var s) ? s : null;

    // ── Trigger evaluation ────────────────────────────────────────────────────

    private static (bool Triggered, string Reason) CheckTrigger(
        ScaleTranche tranche, decimal currentPrice, ScalingState state,
        decimal? ema, decimal? atr)
    {
        var lastFill = state.ExecutedTranches.LastOrDefault();
        if (lastFill is null) return (false, "No prior fill");

        switch (tranche.Trigger)
        {
            case TrancheTrigger.PriceMoveUp:
            {
                decimal threshold = lastFill.FillPrice * (1m + tranche.TriggerPct / 100m);
                return currentPrice >= threshold
                    ? (true,  $"PriceMoveUp: {currentPrice:N2} ≥ {threshold:N2} ({tranche.TriggerPct}% from {lastFill.FillPrice:N2})")
                    : (false, $"PriceMoveUp: waiting for {threshold:N2} (now {currentPrice:N2})");
            }

            case TrancheTrigger.PriceMoveDown:
            {
                decimal threshold = lastFill.FillPrice * (1m - tranche.TriggerPct / 100m);
                return currentPrice <= threshold
                    ? (true,  $"PriceMoveDown: {currentPrice:N2} ≤ {threshold:N2}")
                    : (false, $"PriceMoveDown: waiting for {threshold:N2}");
            }

            case TrancheTrigger.EmaBounce:
            {
                if (ema is null) return (false, "EmaBounce: EMA not provided");
                // Triggered when price dips to EMA and recovers (price is back above EMA)
                bool bounced = currentPrice > ema.Value && currentPrice > lastFill.FillPrice * 0.995m;
                return bounced
                    ? (true,  $"EmaBounce: price={currentPrice:N2} above EMA{tranche.EmaPeriod}={ema:N2}")
                    : (false, $"EmaBounce: price={currentPrice:N2}, EMA{tranche.EmaPeriod}={ema:N2} — no bounce yet");
            }

            case TrancheTrigger.ManualOnly:
                return (false, "ManualOnly tranche — requires explicit call");

            default:
                return (false, $"Unknown trigger {tranche.Trigger}");
        }
    }
}
