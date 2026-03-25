# Skill: Performance Patterns — AlgoTrader

## Purpose
Performance-critical patterns for the trading platform's hot paths.
Load when writing indicator logic, candle cache, evaluation queues, or any code on the tick-processing path.

---

## Incremental Indicator Pattern (O(1) Update)

```csharp
// BAD — recomputes full history on every tick (O(N))
public decimal CalculateEMA(IReadOnlyList<decimal> allCloses, int period)
{
    // Called 1000 times/second across 100 symbols = 100,000 full recalculations
    return closes.Skip(closes.Count - period).Average(); // oversimplified, but the point stands
}

// GOOD — O(1) update using running state
public class IncrementalEMA : IIncrementalIndicator<decimal?>
{
    private readonly int _period;
    private readonly decimal _multiplier;
    private decimal? _current;
    private int _samplesReceived;

    public IncrementalEMA(int period)
    {
        _period = period;
        _multiplier = 2m / (period + 1);
    }

    public decimal? Current => _samplesReceived >= _period ? _current : null;

    public void Update(Candle candle)
    {
        _samplesReceived++;
        if (_samplesReceived == 1)
        {
            _current = candle.Close; // First sample = SMA seed
        }
        else if (_samplesReceived < _period)
        {
            // Build up initial SMA (accumulate sum externally if needed)
            _current = (_current! * (_samplesReceived - 1) + candle.Close) / _samplesReceived;
        }
        else
        {
            // EMA formula: EMA = Close × multiplier + Previous EMA × (1 - multiplier)
            _current = candle.Close * _multiplier + _current! * (1 - _multiplier);
        }
    }

    public void Reset()
    {
        _current = null;
        _samplesReceived = 0;
    }
}
```

### IncrementalATR
```csharp
public class IncrementalATR : IIncrementalIndicator<decimal?>
{
    private readonly int _period;
    private decimal? _prevClose;
    private decimal? _currentAtr;
    private readonly Queue<decimal> _trueRanges;

    public IncrementalATR(int period)
    {
        _period = period;
        _trueRanges = new Queue<decimal>(period);
    }

    public decimal? Current => _trueRanges.Count >= _period ? _currentAtr : null;

    public void Update(Candle candle)
    {
        decimal trueRange;
        if (_prevClose is null)
        {
            trueRange = candle.High - candle.Low;
        }
        else
        {
            trueRange = Math.Max(
                candle.High - candle.Low,
                Math.Max(
                    Math.Abs(candle.High - _prevClose.Value),
                    Math.Abs(candle.Low - _prevClose.Value)
                )
            );
        }

        _trueRanges.Enqueue(trueRange);
        if (_trueRanges.Count > _period) _trueRanges.Dequeue();

        if (_trueRanges.Count == _period)
        {
            _currentAtr = _currentAtr is null
                ? _trueRanges.Average()
                : (_currentAtr * (_period - 1) + trueRange) / _period; // Wilder's smoothing
        }

        _prevClose = candle.Close;
    }

    public void Reset() { _trueRanges.Clear(); _prevClose = null; _currentAtr = null; }
}
```

### IncrementalVWAP (Resets Daily)
```csharp
public class IncrementalVWAP : IIncrementalIndicator<decimal?>
{
    private readonly IClock _clock;
    private DateOnly _currentDate;
    private decimal _cumulativeTPV;  // typical price × volume
    private long _cumulativeVolume;

    public IncrementalVWAP(IClock clock)
    {
        _clock = clock;
        _currentDate = _clock.Today();
    }

    public decimal? Current => _cumulativeVolume > 0 
        ? _cumulativeTPV / _cumulativeVolume 
        : null;

    public void Update(Candle candle)
    {
        // Reset on new trading day
        var today = _clock.Today();
        if (today != _currentDate)
        {
            _currentDate = today;
            _cumulativeTPV = 0;
            _cumulativeVolume = 0;
        }

        var typicalPrice = (candle.High + candle.Low + candle.Close) / 3m;
        _cumulativeTPV += typicalPrice * candle.Volume;
        _cumulativeVolume += candle.Volume;
    }

    public void Reset() { _cumulativeTPV = 0; _cumulativeVolume = 0; }
}
```

---

## Redis Candle Cache — Sorted Set Pattern

```csharp
// Key: candles:{symbol}:{timeframe}
// Score: Unix timestamp (allows O(log N) range queries)
// Max entries: 500 (rolling window)

public class CandleCache : ICandleCache
{
    private readonly IDatabase _redis;
    private const int MaxBarsPerKey = 500;

    private static string Key(string symbol, string timeframe) =>
        $"candles:{symbol}:{timeframe}";

    public async Task AppendAsync(
        string symbol, string timeframe, Candle candle, CancellationToken ct)
    {
        var key = Key(symbol, timeframe);
        var score = candle.Timestamp.ToUnixTimeSeconds();
        var value = JsonSerializer.Serialize(candle);
        
        // Idempotent: ZADD NX (don't overwrite if score exists)
        await _redis.SortedSetAddAsync(key, value, score, When.NotExists);
        
        // Trim to MaxBarsPerKey (keep latest 500)
        var count = await _redis.SortedSetLengthAsync(key);
        if (count > MaxBarsPerKey)
        {
            await _redis.SortedSetRemoveRangeByRankAsync(key, 0, count - MaxBarsPerKey - 1);
        }
    }

    public async Task<IReadOnlyList<Candle>> GetAsync(
        string symbol, string timeframe, int count, CancellationToken ct)
    {
        var key = Key(symbol, timeframe);
        
        // O(log N + M) — get last N entries by score
        var entries = await _redis.SortedSetRangeByRankAsync(key, -count, -1);
        
        if (!entries.Any())
        {
            // Cache miss → fall through to TimescaleDB
            return await _dbRepo.GetLatestCandlesAsync(symbol, timeframe, count, ct);
        }
        
        return entries.Select(e => JsonSerializer.Deserialize<Candle>(e!)!).ToList();
    }
}
```

---

## Strategy Evaluation Queue — Per-Instance Channels

```csharp
// One unbounded channel per strategy instance
// One dedicated consumer Task per instance
// Ensures evaluations are sequential per instance (no race conditions)

public class StrategyEvaluationQueue : IDisposable
{
    private readonly Channel<CandleClosedEvent> _channel;
    private readonly Task _consumerTask;
    private readonly CancellationTokenSource _cts;

    public StrategyEvaluationQueue(
        IStrategy strategy,
        IStrategyScheduler scheduler,
        IStrategyExecutionThrottler throttler,
        StrategyInstanceConfig config,
        IClock clock)
    {
        _channel = Channel.CreateUnbounded<CandleClosedEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,  // Only one consumer
            SingleWriter = false  // Multiple producers (multiple symbols)
        });

        _cts = new CancellationTokenSource();
        _consumerTask = Task.Run(() => ConsumeAsync(strategy, scheduler, throttler, config, clock, _cts.Token));
    }

    public ValueTask EnqueueAsync(CandleClosedEvent evt) =>
        _channel.Writer.WriteAsync(evt);

    private async Task ConsumeAsync(
        IStrategy strategy, IStrategyScheduler scheduler,
        IStrategyExecutionThrottler throttler, StrategyInstanceConfig config,
        IClock clock, CancellationToken ct)
    {
        await foreach (var evt in _channel.Reader.ReadAllAsync(ct))
        {
            // 1. Check schedule
            if (!scheduler.IsWithinScheduledSession(config))
            {
                await _signalJournal.RecordSkippedAsync(evt, SkippedReason.OutsideSchedule, ct);
                continue;
            }

            // 2. Try acquire throttler slot
            if (!await throttler.TryAcquireSlotAsync(config.InstanceId.ToString(), ct))
            {
                await _signalJournal.RecordSkippedAsync(evt, SkippedReason.Throttled, ct);
                continue;
            }

            try
            {
                // 3. Evaluate with timeout
                using var evalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                evalCts.CancelAfter(config.EvaluationTimeoutMs);
                
                var context = await BuildContextAsync(evt, config, evalCts.Token);
                var signal = await strategy.EvaluateAsync(context, evalCts.Token);
                
                await _signalJournal.RecordAsync(signal, ct);
                
                if (signal.Signal is SignalType.Buy or SignalType.Sell)
                    await _executionEngine.ExecuteAsync(signal, context, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Evaluation timed out (not global cancellation)
                await _signalJournal.RecordSkippedAsync(evt, SkippedReason.Timeout, ct);
            }
            finally
            {
                throttler.ReleaseSlot(config.InstanceId.ToString());
            }
        }
    }
}
```

---

## TimescaleDB Query Patterns

```sql
-- ALWAYS include time predicate — otherwise full table scan
-- BAD:
SELECT * FROM candles WHERE symbol = 'RELIANCE' AND timeframe = '15m' ORDER BY timestamp DESC LIMIT 500;

-- GOOD (with time filter — TimescaleDB uses chunk exclusion):
SELECT * FROM candles 
WHERE symbol = 'RELIANCE' 
  AND timeframe = '15m'
  AND timestamp >= NOW() - INTERVAL '30 days'
ORDER BY timestamp DESC 
LIMIT 500;

-- Best practice for latest N candles:
SELECT * FROM candles
WHERE symbol = $1 AND timeframe = $2
  AND timestamp >= $3  -- pass (now - N * interval) as parameter
ORDER BY timestamp DESC
LIMIT $4;
```

```csharp
// In CandleRepository:
public async Task<IReadOnlyList<Candle>> GetLatestAsync(
    string symbol, string timeframe, int count, CancellationToken ct)
{
    // Calculate minimum timestamp to avoid full scan
    var intervalMinutes = TimeframeToMinutes(timeframe);
    var lookbackBuffer = count * 2; // 2x buffer for holidays/weekends
    var fromTime = _clock.NowInstant() - Duration.FromMinutes(intervalMinutes * lookbackBuffer);

    return await _db.Candles
        .Where(c => c.Symbol == symbol 
            && c.Timeframe == timeframe 
            && c.Timestamp >= fromTime.ToDateTimeOffset())  // time predicate first
        .OrderByDescending(c => c.Timestamp)
        .Take(count)
        .ToListAsync(ct);
}
```

---

## Memory Allocation — Hot Path Considerations

```csharp
// Avoid allocations on the tick-processing path

// BAD — allocates new list on every tick
public IReadOnlyList<Candle> GetRecentCandles() => 
    _allCandles.TakeLast(500).ToList(); // copies 500 items every tick

// GOOD — pre-allocated circular buffer
public class CandleBuffer
{
    private readonly Candle[] _buffer;
    private int _head; // next write position
    private int _count;
    private readonly int _capacity;

    public CandleBuffer(int capacity) 
    { 
        _capacity = capacity;
        _buffer = new Candle[capacity]; 
    }

    public void Add(Candle c)
    {
        _buffer[_head] = c;
        _head = (_head + 1) % _capacity;
        if (_count < _capacity) _count++;
    }

    public ReadOnlySpan<Candle> GetLatest(int n) => 
        // Returns span without allocation
        MemoryMarshal.CreateReadOnlySpan(ref _buffer[Math.Max(0, _head - n)], Math.Min(n, _count));
}
```

---

## Benchmark Targets

```
Metric                                    | Target   | How to Measure
-----------------------------------------|----------|------------------
IncrementalEMA.Update()                  | < 100ns  | BenchmarkDotNet
ICandleCache.GetAsync() (cache hit)      | < 5ms    | BenchmarkDotNet
ICandleCache.GetAsync() (cache miss, DB) | < 50ms   | Integration test
IStrategy.EvaluateAsync()                | < 100ms  | BenchmarkDotNet
Strategy evaluation throughput           | 100/sec  | Load test
Order placement API (p95)                | < 200ms  | k6 load test
Broker order placement (p95)             | < 500ms  | Latency log
```

Run benchmarks:
```bash
cd benchmarks/rvs.AlgoTrader.Benchmarks
dotnet run -c Release
```
