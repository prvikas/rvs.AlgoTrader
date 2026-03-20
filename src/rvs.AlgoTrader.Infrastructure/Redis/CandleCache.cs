using System.Text.Json;
using StackExchange.Redis;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.ValueObjects;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
namespace rvs.AlgoTrader.Infrastructure.Redis;

public sealed class CandleCache(IConnectionMultiplexer redis) : ICandleCache
{
    private const int MaxBars = 500;
    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions().ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

    private static string CacheKey(string symbol, string timeframe) => $"candles:{symbol}:{timeframe}";

    public async Task<IReadOnlyList<ClosedCandle>> GetAsync(string symbol, string timeframe, int count, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var key = CacheKey(symbol, timeframe);
        var entries = await db.SortedSetRangeByRankAsync(key, -count, -1);
        return entries.Select(e => JsonSerializer.Deserialize<ClosedCandle>(e!, JsonOpts)!).ToList();
    }

    public async Task AppendAsync(ClosedCandle candle, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var key = CacheKey(candle.InternalSymbol, candle.Timeframe);
        var score = candle.OpenTime.ToInstant().ToUnixTimeTicks();
        var json = JsonSerializer.Serialize(candle, JsonOpts);
        await db.SortedSetAddAsync(key, json, score);
        // Trim to MaxBars
        await db.SortedSetRemoveRangeByRankAsync(key, 0, -(MaxBars + 1));
    }

    public async Task WarmAsync(string symbol, string timeframe, IEnumerable<ClosedCandle> candles, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var key = CacheKey(symbol, timeframe);
        await db.KeyDeleteAsync(key);
        var entries = candles.Select(c => new SortedSetEntry(
            JsonSerializer.Serialize(c, JsonOpts),
            c.OpenTime.ToInstant().ToUnixTimeTicks()
        )).ToArray();
        if (entries.Length > 0)
            await db.SortedSetAddAsync(key, entries);
    }
}
