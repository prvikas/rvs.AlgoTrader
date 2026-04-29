using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Constants;
using rvs.AlgoTrader.Domain.Events;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Aggregates raw BrokerTick stream into OHLCV candles for each configured timeframe.
/// Publishes CandleClosedEvent (via MassTransit) only for CLOSED bars — never partial/open bars.
/// Writes closed candles to ICandleCache (Redis sorted set, 500-bar limit) and CandleRepository.
/// Active broker is read from config (Broker:ActiveBroker), defaulting to MStock.
/// Reconnection uses exponential backoff (2s → 4s → 8s … capped at 120s) with
/// <see cref="IDataFeedHealthMonitor"/> instrumentation.
///
/// Gap backfill (AP-014 / feed reliability):
///   After every reconnect, BackfillAfterReconnectAsync checks the most recent bar in cache
///   for each symbol × intraday timeframe. If the last bar is stale (older than 1 × period),
///   it fetches up to <see cref="BackfillMaxBars"/> bars from the broker's historical data API
///   and warms the cache — ensuring strategies never evaluate on stale pre-disconnect data.
///   Backfill is skipped outside IST market hours (09:15–15:30) so no spurious broker calls
///   are made overnight or on non-trading days.
/// </summary>
public class CandleAggregatorService(
    IBrokerClientFactory brokerFactory,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IDataFeedHealthMonitor healthMonitor,
    ILogger<CandleAggregatorService> logger) : BackgroundService
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    // Exponential backoff bounds (seconds)
    private const int BackoffInitialSeconds = 2;
    private const int BackoffMaxSeconds     = 120;

    // Backfill configuration (AP-014: always bound historical queries by time)
    private const int BackfillMaxBars = 50;  // max bars to fetch per symbol×TF on reconnect

    // Intraday timeframes eligible for gap backfill (daily has no intraday gaps)
    private static readonly IReadOnlyList<string> _intradayTimeframes =
    [
        Timeframes.OneMinute, Timeframes.ThreeMinute, Timeframes.FiveMinute,
        Timeframes.FifteenMinute, Timeframes.ThirtyMinute, Timeframes.SixtyMinute,
    ];

    // Active partial candles keyed by "SYMBOL:TIMEFRAME"
    private readonly Dictionary<string, PartialCandle> _partials = new();
    private readonly object _lock = new();

    // Subscribed symbols and active broker — guarded by _lock (#24)
    private HashSet<string> _subscribedSymbols = [];
    private string _brokerName = string.Empty;

    // All supported timeframes as a static readonly — no per-instance allocation (#22)
    private static readonly IReadOnlyList<string> _timeframes = Timeframes.All;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Read active broker from config at startup; can be overridden by UpdateSubscriptions()
        lock (_lock)
        {
            _brokerName = configuration["Broker:ActiveBroker"] ?? BrokerNames.Default;
        }
        logger.LogInformation("[CandleAggregator] Background service started. Active broker: {Broker}. Waiting for symbol subscriptions...", _brokerName);

        var backoffSeconds = BackoffInitialSeconds;

        while (!stoppingToken.IsCancellationRequested)
        {
            // Snapshot subscribed symbols and broker under lock to avoid torn reads (#24)
            HashSet<string> symbols;
            string broker;
            lock (_lock)
            {
                symbols = _subscribedSymbols;
                broker  = _brokerName;
            }

            // Wait until symbols are subscribed before attempting broker connection
            if (symbols.Count == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ContinueWith(_ => { });
                continue;
            }

            logger.LogInformation("[CandleAggregator] Starting tick stream for broker {Broker} with {Count} symbols",
                broker, symbols.Count);

            try
            {
                await foreach (var tick in brokerFactory.GetStreamClient(broker)
                    .StreamAsync(symbols, stoppingToken))
                {
                    // Feed is alive — reset backoff and update health monitor
                    backoffSeconds = BackoffInitialSeconds;
                    healthMonitor.RecordTick(broker);

                    try
                    {
                        await ProcessTickAsync(tick, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "[CandleAggregator] Error processing tick for {Symbol}", tick.Symbol);
                    }
                }

                // StreamAsync completed without exception — broker closed connection cleanly
                logger.LogWarning("[CandleAggregator] Broker {Broker} stream ended. Scheduling reconnect.", broker);
                healthMonitor.RecordDisconnect(broker);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown — exit gracefully
                break;
            }
            catch (Exception ex)
            {
                healthMonitor.RecordDisconnect(broker);
                logger.LogWarning(ex,
                    "[CandleAggregator] Broker {Broker} stream disconnected. Reconnecting in {Backoff}s (attempt #{Attempts})...",
                    broker, backoffSeconds, healthMonitor.GetStatus(broker)?.ReconnectAttempts + 1);
            }

            // ── Exponential backoff delay before reconnect ────────────────────
            healthMonitor.RecordReconnectAttempt(broker);
            await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), stoppingToken).ContinueWith(_ => { });

            // Double backoff, capped at max
            backoffSeconds = Math.Min(backoffSeconds * 2, BackoffMaxSeconds);

            if (!stoppingToken.IsCancellationRequested)
            {
                healthMonitor.RecordReconnect(broker);

                // AP-014: fill any bars that accumulated while the feed was down.
                // Runs best-effort — a failure here must never block the reconnect loop.
                try
                {
                    await BackfillAfterReconnectAsync(symbols, broker, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "[CandleAggregator] Gap backfill failed for broker {Broker} — resuming live feed",
                        broker);
                }
            }
        }

        logger.LogInformation("[CandleAggregator] Background service stopped.");
    }

    private async Task ProcessTickAsync(BrokerTick tick, CancellationToken ct)
    {
        foreach (var timeframe in _timeframes)
        {
            var barStart = GetBarStart(tick.Timestamp, timeframe);
            var barEnd = GetBarEnd(barStart, timeframe);
            var key = $"{tick.Symbol}:{timeframe}";

            lock (_lock)
            {
                if (!_partials.TryGetValue(key, out var partial))
                {
                    _partials[key] = new PartialCandle(tick.Symbol, timeframe, barStart, barEnd,
                        tick.Ltp, tick.Ltp, tick.Ltp, tick.Ltp, tick.Volume);
                    return;
                }

                // Check if current tick belongs to a new bar
                if (tick.Timestamp.ToInstant() >= partial.BarEnd.ToInstant())
                {
                    // Close the current bar and schedule publish
                    var closedCandle = partial.ToClosedCandle();
                    _partials[key] = new PartialCandle(tick.Symbol, timeframe, barStart, barEnd,
                        tick.Ltp, tick.Ltp, tick.Ltp, tick.Ltp, tick.Volume);

                    // Publish outside lock
                    _ = Task.Run(() => PublishClosedCandleAsync(closedCandle, ct), ct);
                }
                else
                {
                    // Update partial: high/low/close/volume
                    _partials[key] = partial with
                    {
                        High = Math.Max(partial.High, tick.Ltp),
                        Low = Math.Min(partial.Low, tick.Ltp),
                        Close = tick.Ltp,
                        Volume = tick.Volume
                    };
                }
            }
        }
    }

    private async Task PublishClosedCandleAsync(ClosedCandle candle, CancellationToken ct)
    {
        try
        {
            // Use scoped services from a scope (IHostedService is singleton)
            using var scope = scopeFactory.CreateScope();
            var candleCache = scope.ServiceProvider.GetRequiredService<ICandleCache>();
            var candleRepo = scope.ServiceProvider.GetRequiredService<ICandleRepository>();
            var bus = scope.ServiceProvider.GetRequiredService<MassTransit.IPublishEndpoint>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            // 1. Write to Redis cache
            await candleCache.AppendAsync(candle, ct);

            // 2. Persist to DB
            await candleRepo.BulkInsertAsync([candle], ct);

            // 3. Publish domain event — triggers StrategyEvaluationQueue
            var correlationId = Guid.NewGuid().ToString();
            await bus.Publish(new CandleClosedEvent(
                candle.InternalSymbol,
                candle.Timeframe,
                candle,
                correlationId,
                clock.NowIst()), ct);

            logger.LogDebug("[CandleAggregator] Closed {Tf} candle for {Symbol} at {Time}",
                candle.Timeframe, candle.InternalSymbol, candle.OpenTime);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[CandleAggregator] Failed to publish closed candle {Symbol}:{Tf}",
                candle.InternalSymbol, candle.Timeframe);
        }
    }

    /// <summary>
    /// Fetches missing intraday bars for each symbol × timeframe after the feed reconnects.
    ///
    /// Algorithm per (symbol, timeframe):
    ///   1. Get the most recent bar from ICandleCache (at most 1 bar).
    ///   2. If the last bar's close-time is within 1 × period of now → cache is fresh, skip.
    ///   3. Otherwise compute from = lastBarCloseTime.Date (AP-014: bounded query).
    ///   4. Fetch up to BackfillMaxBars bars via IBrokerMarketDataClient.GetHistoricalDataAsync.
    ///   5. Warm cache with the new bars — AppendAsync on each bar.
    ///
    /// Only runs during IST market hours (09:15–15:30) to avoid spurious overnight calls.
    /// Runs best-effort; individual symbol failures do not abort the overall loop.
    /// </summary>
    private async Task BackfillAfterReconnectAsync(
        HashSet<string> symbols,
        string broker,
        CancellationToken ct)
    {
        using var scope  = scopeFactory.CreateScope();
        var cache        = scope.ServiceProvider.GetRequiredService<ICandleCache>();
        var clock        = scope.ServiceProvider.GetRequiredService<IClock>();
        var marketCal    = scope.ServiceProvider.GetRequiredService<IMarketCalendarService>();
        var brokerClient = brokerFactory.GetMarketDataClient(broker);

        var nowIst    = clock.NowInstant().InZone(Ist);
        var nowLocal  = nowIst.LocalDateTime;
        var todayDate = nowIst.Date;

        // Skip entirely outside IST market hours (09:15–15:30) — AP-014: bound timestamps
        var mktOpen  = new LocalTime(9,  15, 0);
        var mktClose = new LocalTime(15, 30, 0);
        if (nowLocal.TimeOfDay < mktOpen || nowLocal.TimeOfDay > mktClose)
        {
            logger.LogDebug("[CandleAggregator] Gap backfill skipped — outside market hours ({Time} IST)", nowLocal);
            return;
        }

        // Skip on non-trading days
        try
        {
            var tradingDay = DateOnly.FromDateTime(todayDate.ToDateTimeUnspecified());
            if (!await marketCal.IsTradingDayAsync(tradingDay, ct))
            {
                logger.LogDebug("[CandleAggregator] Gap backfill skipped — non-trading day {Date}", todayDate);
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[CandleAggregator] Could not verify trading day — proceeding with backfill anyway");
        }

        logger.LogInformation(
            "[CandleAggregator] Gap backfill starting for {Count} symbol(s) after reconnect",
            symbols.Count);

        var backfillCount = 0;
        var toDate        = DateOnly.FromDateTime(todayDate.ToDateTimeUnspecified());

        foreach (var symbol in symbols)
        {
            foreach (var tf in _intradayTimeframes)
            {
                if (ct.IsCancellationRequested) return;

                try
                {
                    // 1. Get most recent bar from cache to establish anchor point
                    var cached = await cache.GetAsync(symbol, tf, count: 1, ct);
                    if (cached.Count == 0) continue;   // nothing in cache — warming is handled elsewhere

                    var lastCached = cached[^1];
                    var tfPeriod   = TimeframeToDuration(tf);

                    // 2. Determine if cache is stale — last bar's close time + 1 period < now
                    var staleThreshold = lastCached.CloseTime.ToInstant().Plus(tfPeriod);
                    if (clock.NowInstant() <= staleThreshold)
                        continue;   // cache is still fresh within one period

                    // 3. Compute bounded query window (AP-014):
                    //    from = date of last cached bar's open (inclusive of that day)
                    //    to   = today
                    var fromDate = DateOnly.FromDateTime(
                        lastCached.OpenTime.LocalDateTime.ToDateTimeUnspecified());

                    // 4. Fetch historical bars from broker
                    //    BrokerToken = InternalSymbol (brokers resolve internally)
                    var bars = await brokerClient.GetHistoricalDataAsync(
                        new HistoricalDataQuery(
                            BrokerToken:    symbol,
                            InternalSymbol: symbol,
                            Timeframe:      tf,
                            FromDate:       fromDate,
                            ToDate:         toDate),
                        ct);

                    if (bars.Count == 0) continue;

                    // 5. Convert OhlcvBar → ClosedCandle and deduplicate against cached anchor.
                    //    Skip bars whose OpenTime ≤ last cached bar's OpenTime (already in cache).
                    var newBars = bars
                        .Where(b => b.OpenTime.ToInstant() > lastCached.OpenTime.ToInstant())
                        .Take(BackfillMaxBars)
                        .Select(b => OhlcvBarToClosedCandle(b, tf))
                        .ToList();

                    if (newBars.Count == 0) continue;

                    foreach (var bar in newBars)
                        await cache.AppendAsync(bar, ct);

                    backfillCount += newBars.Count;

                    logger.LogInformation(
                        "[CandleAggregator] Backfilled {Count} {Tf} bars for {Symbol} (gap: {From} → {To})",
                        newBars.Count, tf, symbol, fromDate, toDate);
                }
                catch (Exception ex)
                {
                    // Per-symbol failure must never abort the outer loop
                    logger.LogWarning(ex,
                        "[CandleAggregator] Backfill failed for {Symbol}:{Tf} — skipping", symbol, tf);
                }
            }
        }

        logger.LogInformation(
            "[CandleAggregator] Gap backfill complete — {Total} bar(s) restored across all symbols",
            backfillCount);
    }

    /// <summary>
    /// Converts a broker <see cref="OhlcvBar"/> to a domain <see cref="ClosedCandle"/>.
    /// CloseTime is computed as OpenTime + period for the given timeframe.
    /// </summary>
    private static ClosedCandle OhlcvBarToClosedCandle(OhlcvBar bar, string timeframe)
    {
        var closeTime = bar.OpenTime.Plus(TimeframeToDuration(timeframe));
        return new ClosedCandle(
            bar.InternalSymbol, bar.Timeframe,
            OpenTime: bar.OpenTime, CloseTime: closeTime,
            Open: bar.Open, High: bar.High, Low: bar.Low, Close: bar.Close,
            Volume: bar.Volume);
    }

    /// <summary>Maps a timeframe string to its period Duration for staleness checks.</summary>
    private static Duration TimeframeToDuration(string timeframe) => timeframe switch
    {
        Timeframes.OneMinute     => Duration.FromMinutes(1),
        Timeframes.ThreeMinute   => Duration.FromMinutes(3),
        Timeframes.FiveMinute    => Duration.FromMinutes(5),
        Timeframes.FifteenMinute => Duration.FromMinutes(15),
        Timeframes.ThirtyMinute  => Duration.FromMinutes(30),
        Timeframes.SixtyMinute   => Duration.FromMinutes(60),
        _                        => Duration.FromMinutes(1),
    };

    /// <summary>
    /// Called by StrategyInstanceManager when running instances change.
    /// Both fields updated atomically under _lock to prevent torn reads in ExecuteAsync (#24).
    /// </summary>
    public void UpdateSubscriptions(IEnumerable<string> brokerTokens, string brokerName)
    {
        lock (_lock)
        {
            _subscribedSymbols = [..brokerTokens];
            _brokerName = brokerName;
        }
    }

    private static ZonedDateTime GetBarStart(ZonedDateTime tick, string timeframe)
    {
        var local = tick.LocalDateTime;
        return timeframe switch
        {
            Timeframes.OneMinute     => new LocalDateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0)
                .InZoneLeniently(Ist),
            Timeframes.ThreeMinute   => new LocalDateTime(local.Year, local.Month, local.Day, local.Hour, (local.Minute / 3) * 3, 0)
                .InZoneLeniently(Ist),
            Timeframes.FiveMinute    => new LocalDateTime(local.Year, local.Month, local.Day, local.Hour, (local.Minute / 5) * 5, 0)
                .InZoneLeniently(Ist),
            Timeframes.FifteenMinute => new LocalDateTime(local.Year, local.Month, local.Day, local.Hour, (local.Minute / 15) * 15, 0)
                .InZoneLeniently(Ist),
            Timeframes.ThirtyMinute  => new LocalDateTime(local.Year, local.Month, local.Day, local.Hour, (local.Minute / 30) * 30, 0)
                .InZoneLeniently(Ist),
            Timeframes.SixtyMinute   => new LocalDateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0)
                .InZoneLeniently(Ist),
            Timeframes.Daily         => new LocalDateTime(local.Year, local.Month, local.Day, 9, 15, 0)
                .InZoneLeniently(Ist),
            _ => tick
        };
    }

    private static ZonedDateTime GetBarEnd(ZonedDateTime barStart, string timeframe)
        => timeframe switch
        {
            Timeframes.OneMinute     => barStart.Plus(Duration.FromMinutes(1)),
            Timeframes.ThreeMinute   => barStart.Plus(Duration.FromMinutes(3)),
            Timeframes.FiveMinute    => barStart.Plus(Duration.FromMinutes(5)),
            Timeframes.FifteenMinute => barStart.Plus(Duration.FromMinutes(15)),
            Timeframes.ThirtyMinute  => barStart.Plus(Duration.FromMinutes(30)),
            Timeframes.SixtyMinute   => barStart.Plus(Duration.FromMinutes(60)),
            Timeframes.Daily         => barStart.Date.At(new LocalTime(15, 30, 0)).InZoneLeniently(Ist),
            _ => barStart.Plus(Duration.FromMinutes(1))
        };

    private record PartialCandle(
        string Symbol, string Timeframe,
        ZonedDateTime BarStart, ZonedDateTime BarEnd,
        decimal Open, decimal High, decimal Low, decimal Close, long Volume)
    {
        public ClosedCandle ToClosedCandle() => new(
            Symbol, Timeframe, BarStart, BarEnd, Open, High, Low, Close, Volume);
    }
}
