using System.Collections.Concurrent;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Infrastructure.Redis;

/// <summary>
/// In-memory candle cache. Used when Redis is not available (local dev / single-instance).
/// Keeps up to 500 bars per symbol/timeframe combination.
/// </summary>
public sealed class InMemoryCandleCache : ICandleCache
{
    private const int MaxBars = 500;
    private readonly ConcurrentDictionary<string, SortedList<long, ClosedCandle>> _store = new();

    private static string Key(string symbol, string timeframe) => $"{symbol}:{timeframe}";

    public Task<IReadOnlyList<ClosedCandle>> GetAsync(string symbol, string timeframe, int count, CancellationToken ct)
    {
        if (!_store.TryGetValue(Key(symbol, timeframe), out var list))
            return Task.FromResult<IReadOnlyList<ClosedCandle>>(Array.Empty<ClosedCandle>());

        lock (list)
        {
            var result = list.Values.TakeLast(count).ToList();
            return Task.FromResult<IReadOnlyList<ClosedCandle>>(result);
        }
    }

    public Task AppendAsync(ClosedCandle candle, CancellationToken ct)
    {
        var key = Key(candle.InternalSymbol, candle.Timeframe);
        var list = _store.GetOrAdd(key, _ => new SortedList<long, ClosedCandle>());
        var score = candle.OpenTime.ToInstant().ToUnixTimeTicks();

        lock (list)
        {
            list[score] = candle;
            while (list.Count > MaxBars)
                list.RemoveAt(0);
        }
        return Task.CompletedTask;
    }

    public Task WarmAsync(string symbol, string timeframe, IEnumerable<ClosedCandle> candles, CancellationToken ct)
    {
        var key = Key(symbol, timeframe);
        var list = new SortedList<long, ClosedCandle>();
        foreach (var c in candles)
            list[c.OpenTime.ToInstant().ToUnixTimeTicks()] = c;
        _store[key] = list;
        return Task.CompletedTask;
    }
}
