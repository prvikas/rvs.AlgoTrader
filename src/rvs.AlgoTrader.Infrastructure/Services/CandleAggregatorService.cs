using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
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

    // Active partial candles keyed by "SYMBOL:TIMEFRAME"
    private readonly Dictionary<string, PartialCandle> _partials = new();
    private readonly object _lock = new();

    // Subscribed symbols — updated by StrategyInstanceManager
    private HashSet<string> _subscribedSymbols = [];
    // Read active broker from config; defaults to MStock
    private string _brokerName = string.Empty;
    private readonly string[] _timeframes = ["1m", "3m", "5m", "15m", "30m", "60m", "1d"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Read active broker from config at startup; can be overridden by UpdateSubscriptions()
        _brokerName = configuration["Broker:ActiveBroker"] ?? "MStock";
        logger.LogInformation("[CandleAggregator] Background service started. Active broker: {Broker}. Waiting for symbol subscriptions...", _brokerName);

        var backoffSeconds = BackoffInitialSeconds;

        while (!stoppingToken.IsCancellationRequested)
        {
            // Wait until symbols are subscribed before attempting broker connection
            if (_subscribedSymbols.Count == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ContinueWith(_ => { });
                continue;
            }

            logger.LogInformation("[CandleAggregator] Starting tick stream for broker {Broker} with {Count} symbols",
                _brokerName, _subscribedSymbols.Count);

            try
            {
                await foreach (var tick in brokerFactory.GetStreamClient(_brokerName)
                    .StreamAsync(_subscribedSymbols, stoppingToken))
                {
                    // Feed is alive — reset backoff and update health monitor
                    backoffSeconds = BackoffInitialSeconds;
                    healthMonitor.RecordTick(_brokerName);

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
                logger.LogWarning("[CandleAggregator] Broker {Broker} stream ended. Scheduling reconnect.", _brokerName);
                healthMonitor.RecordDisconnect(_brokerName);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown — exit gracefully
                break;
            }
            catch (Exception ex)
            {
                healthMonitor.RecordDisconnect(_brokerName);
                logger.LogWarning(ex,
                    "[CandleAggregator] Broker {Broker} stream disconnected. Reconnecting in {Backoff}s (attempt #{Attempts})...",
                    _brokerName, backoffSeconds, healthMonitor.GetStatus(_brokerName)?.ReconnectAttempts + 1);
            }

            // ── Exponential backoff delay before reconnect ────────────────────
            healthMonitor.RecordReconnectAttempt(_brokerName);
            await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), stoppingToken).ContinueWith(_ => { });

            // Double backoff, capped at max
            backoffSeconds = Math.Min(backoffSeconds * 2, BackoffMaxSeconds);

            if (!stoppingToken.IsCancellationRequested)
                healthMonitor.RecordReconnect(_brokerName);
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

    public void UpdateSubscriptions(IEnumerable<string> brokerTokens, string brokerName)
    {
        _subscribedSymbols = [..brokerTokens];
        _brokerName = brokerName;
    }

    private static ZonedDateTime GetBarStart(ZonedDateTime tick, string timeframe)
    {
        var local = tick.LocalDateTime;
        return timeframe switch
        {
            "1m" => new LocalDateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0)
                .InZoneLeniently(Ist),
            "3m" => new LocalDateTime(local.Year, local.Month, local.Day, local.Hour, (local.Minute / 3) * 3, 0)
                .InZoneLeniently(Ist),
            "5m" => new LocalDateTime(local.Year, local.Month, local.Day, local.Hour, (local.Minute / 5) * 5, 0)
                .InZoneLeniently(Ist),
            "15m" => new LocalDateTime(local.Year, local.Month, local.Day, local.Hour, (local.Minute / 15) * 15, 0)
                .InZoneLeniently(Ist),
            "30m" => new LocalDateTime(local.Year, local.Month, local.Day, local.Hour, (local.Minute / 30) * 30, 0)
                .InZoneLeniently(Ist),
            "60m" => new LocalDateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0)
                .InZoneLeniently(Ist),
            "1d" => new LocalDateTime(local.Year, local.Month, local.Day, 9, 15, 0)
                .InZoneLeniently(Ist),
            _ => tick
        };
    }

    private static ZonedDateTime GetBarEnd(ZonedDateTime barStart, string timeframe)
        => timeframe switch
        {
            "1m" => barStart.Plus(Duration.FromMinutes(1)),
            "3m" => barStart.Plus(Duration.FromMinutes(3)),
            "5m" => barStart.Plus(Duration.FromMinutes(5)),
            "15m" => barStart.Plus(Duration.FromMinutes(15)),
            "30m" => barStart.Plus(Duration.FromMinutes(30)),
            "60m" => barStart.Plus(Duration.FromMinutes(60)),
            "1d" => barStart.Date.At(new LocalTime(15, 30, 0)).InZoneLeniently(Ist),
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
