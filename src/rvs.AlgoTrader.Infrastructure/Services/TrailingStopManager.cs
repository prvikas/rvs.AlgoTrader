using System.Collections.Concurrent;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Full trailing stop state machine for all 7 StopType variants.
/// State is in-memory, keyed by positionId. Singleton-safe (ConcurrentDictionary).
///
/// StopType behaviours:
///   Fixed           — static level; IsTriggered when price crosses stop
///   TrailingFixed   — stop trails BestPrice by a fixed ₹ offset
///   TrailingPercent — stop trails BestPrice by a % (params.TrailOffset as decimal percent)
///   TrailingAtr     — stop trails BestPrice by N × ATR (requires atr arg in Update)
///   BreakEven       — slides stop to entry once unrealised gain ≥ BreakEvenAt1R × initial risk (1R)
///   TimeStop        — marks triggered if BarsInTrade ≥ MaxBarsInTrade without profit
///   Chandelier      — stop = BestPrice − N × ATR (long) / BestPrice + N × ATR (short)
/// </summary>
public sealed class TrailingStopManager : ITrailingStopManager
{
    private readonly ConcurrentDictionary<Guid, TrailingStopEntry> _state = new();

    public TrailingStopEntry Register(
        Guid positionId,
        decimal entryPrice,
        decimal initialStop,
        OrderDirection direction,
        StopType stopType,
        TrailingStopParams parameters)
    {
        var entry = new TrailingStopEntry(
            PositionId:  positionId,
            EntryPrice:  entryPrice,
            CurrentStop: initialStop,
            BestPrice:   entryPrice,
            Direction:   direction,
            StopType:    stopType,
            Parameters:  parameters,
            IsTriggered: false,
            BarsInTrade: 0);

        _state[positionId] = entry;
        return entry;
    }

    public TrailingStopEntry Update(Guid positionId, decimal currentPrice, decimal? atr = null)
    {
        if (!_state.TryGetValue(positionId, out var entry)) return null!;
        if (entry.IsTriggered) return entry;

        bool isLong   = entry.Direction == OrderDirection.Buy;
        decimal best  = isLong
            ? Math.Max(entry.BestPrice, currentPrice)
            : Math.Min(entry.BestPrice, currentPrice);

        decimal newStop = entry.StopType switch
        {
            StopType.Fixed           => entry.CurrentStop,
            StopType.TrailingFixed   => TrailingFixed(best, isLong, entry),
            StopType.TrailingPercent => TrailingPercent(best, isLong, entry),
            StopType.TrailingAtr     => TrailingAtr(best, isLong, atr, entry),
            StopType.BreakEven       => BreakEven(currentPrice, isLong, entry),
            StopType.TimeStop        => entry.CurrentStop, // stop level unchanged; trigger via bars
            StopType.Chandelier      => Chandelier(best, isLong, atr, entry),
            _                        => entry.CurrentStop
        };

        // Stop is a ratchet — only moves in favour
        newStop = isLong
            ? Math.Max(newStop, entry.CurrentStop)
            : Math.Min(newStop, entry.CurrentStop);

        bool triggered = IsStopTriggered(currentPrice, newStop, isLong, entry);

        var updated = entry with
        {
            BestPrice   = best,
            CurrentStop = newStop,
            IsTriggered = triggered,
            BarsInTrade = entry.BarsInTrade + 1
        };
        _state[positionId] = updated;
        return updated;
    }

    public void Unregister(Guid positionId) => _state.TryRemove(positionId, out _);

    public TrailingStopEntry? Get(Guid positionId)
        => _state.TryGetValue(positionId, out var e) ? e : null;

    // ── Stop calculation helpers ──────────────────────────────────────────────

    private static decimal TrailingFixed(decimal best, bool isLong, TrailingStopEntry e)
        => isLong ? best - e.Parameters.TrailOffset : best + e.Parameters.TrailOffset;

    private static decimal TrailingPercent(decimal best, bool isLong, TrailingStopEntry e)
    {
        decimal pct = e.Parameters.TrailOffset / 100m;
        return isLong ? best * (1m - pct) : best * (1m + pct);
    }

    private static decimal TrailingAtr(decimal best, bool isLong, decimal? atr, TrailingStopEntry e)
    {
        if (atr is null or 0) return e.CurrentStop;
        decimal trail = atr.Value * e.Parameters.AtrMultiplier;
        return isLong ? best - trail : best + trail;
    }

    private static decimal BreakEven(decimal current, bool isLong, TrailingStopEntry e)
    {
        decimal initialRisk = Math.Abs(e.EntryPrice - e.CurrentStop);
        decimal gain        = isLong ? current - e.EntryPrice : e.EntryPrice - current;
        if (initialRisk > 0 && gain >= initialRisk * e.Parameters.BreakEvenAt1R)
            return e.EntryPrice; // slide to entry
        return e.CurrentStop;
    }

    private static decimal Chandelier(decimal best, bool isLong, decimal? atr, TrailingStopEntry e)
    {
        if (atr is null or 0) return e.CurrentStop;
        decimal trail = atr.Value * e.Parameters.AtrMultiplier;
        return isLong ? best - trail : best + trail;
    }

    private static bool IsStopTriggered(decimal price, decimal stop, bool isLong, TrailingStopEntry e)
    {
        if (e.StopType == StopType.TimeStop)
            return e.Parameters.MaxBarsInTrade > 0
                   && e.BarsInTrade >= e.Parameters.MaxBarsInTrade
                   && (isLong ? price <= e.EntryPrice : price >= e.EntryPrice);

        return isLong ? price <= stop : price >= stop;
    }
}
