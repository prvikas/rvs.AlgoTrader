using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Backtesting.Engine;

/// <summary>
/// Forward test (paper trading) engine — registered as Singleton so in-memory
/// session state (_activeStates) persists across HTTP requests and candle events.
///
/// All scoped dependencies (ICandleCache, IForwardTestFillSimulator,
/// IForwardTestSessionRepository, IForwardTestTradeRepository) are resolved
/// per call via IServiceScopeFactory — same pattern as CandleAggregatorService.
///
/// Implements Application.Services.IForwardTestEngine so that
/// StrategyEvaluationQueue (Infrastructure) can depend on it without a
/// direct reference to rvs.AlgoTrader.Backtesting.
/// </summary>
public class ForwardTestEngine(
    IServiceScopeFactory scopeFactory,
    IStrategyFactory strategyFactory,
    IClock clock,
    ILogger<ForwardTestEngine> logger) : IForwardTestEngine
{
    // Keyed by strategy instance ID; holds in-memory position/P&L state.
    // ConcurrentDictionary not needed — MassTransit consumer serialises access per instance.
    private readonly Dictionary<Guid, ForwardTestState> _activeStates = new();

    // ── IForwardTestEngine ───────────────────────────────────────────────────

    public async Task ProcessCandleAsync(
        StrategyInstance instance, ClosedCandle candle, CancellationToken ct)
    {
        if (!_activeStates.TryGetValue(instance.Id, out var state))
        {
            logger.LogDebug("[ForwardTest] No active session for {Instance}", instance.Name);
            return;
        }

        // Create a DI scope for all scoped services needed in this candle tick
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var candleCache   = sp.GetRequiredService<ICandleCache>();
        var fillSimulator = sp.GetRequiredService<IForwardTestFillSimulator>();
        var tradeRepo     = sp.GetRequiredService<IForwardTestTradeRepository>();

        var candles = await candleCache.GetAsync(instance.InternalSymbol, instance.Timeframe, 500, ct);
        if (candles.Count < 20) return;

        // ── Check if open position should close (SL/TP) ───────────────────
        if (state.OpenTrade != null)
        {
            var closeResult = TryClosePosition(state.OpenTrade, candle);
            if (closeResult != null)
            {
                // ExitTime: use candle.OpenTime as the intrabar fill estimate (mirrors BacktestEngine).
                // ClosedAt retains wall-clock time for audit/monitoring purposes.
                var exitInstant = candle.OpenTime.ToInstant();
                var trade = new ForwardTestTrade
                {
                    Id = Guid.NewGuid(),
                    SessionId = state.SessionId,
                    InternalSymbol = candle.InternalSymbol,
                    Direction = state.OpenTrade.Direction,
                    Quantity = state.OpenTrade.Quantity,
                    EntryPrice = state.OpenTrade.EntryPrice,
                    ExitPrice = closeResult.ExitPrice,
                    SimulatedFillPrice = closeResult.ExitPrice,
                    Slippage = 0m,
                    Pnl = closeResult.Pnl,
                    RealizedPnl = closeResult.Pnl,
                    CloseReason = closeResult.Reason,
                    OpenedAt = state.OpenTrade.OpenedAt,
                    ClosedAt = clock.NowInstant(),
                    EntryTime = state.OpenTrade.OpenedAt,
                    ExitTime = exitInstant
                };

                state.TotalPnl += closeResult.Pnl;
                state.ClosedTradeCount++;
                if (closeResult.Pnl > 0) state.WinCount++;

                await tradeRepo.AddAsync(trade, ct);
                state.OpenTrade = null;
                return;
            }
        }

        if (state.OpenTrade != null) return; // already in position — wait for close

        // ── Evaluate strategy for new entry ──────────────────────────────
        var strategy = strategyFactory.Create(instance.StrategyType, instance.ParametersJson);
        var correlationId = Guid.NewGuid().ToString("N");
        var context = new StrategyContext(
            instance.Id,
            instance.InternalSymbol,
            instance.Timeframe,
            candles,
            instance.ParametersJson ?? "{}",
            correlationId);

        SignalResult signal;
        try
        {
            signal = await strategy.EvaluateAsync(context, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[ForwardTest] Strategy eval error for {Instance}", instance.Name);
            return;
        }

        if (signal.Signal is not (SignalType.Buy or SignalType.Sell)) return;

        var fillConfig = new FillSimConfig(SlippagePct: 0.0002m);
        var fillResult = await fillSimulator.SimulateFillAsync(signal, [candle], clock, fillConfig, ct);
        if (!fillResult.Filled || fillResult.FillPrice == null) return;

        var entryPrice = fillResult.FillPrice.Value;
        var credential = instance.Credential ?? throw new InvalidOperationException($"BrokerCredential not found for instance {instance.Id}");
        var lotSize    = credential.LotSize > 0 ? credential.LotSize : 1;
        state.OpenTrade = new ForwardTestOpenTrade(
            signal.Signal.ToString().ToUpperInvariant(), lotSize, entryPrice,
            signal.StopLoss ?? entryPrice * 0.99m,
            signal.TakeProfit ?? entryPrice * 1.02m,
            clock.NowInstant());

        logger.LogInformation("[ForwardTest] {Signal} {Symbol} @ {Price} (simulated)",
            signal.Signal, candle.InternalSymbol, entryPrice);
    }

    public async Task<Guid> StartSessionAsync(
        StrategyInstance instance, decimal initialCapital, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IForwardTestSessionRepository>();

        var session = new ForwardTestSession
        {
            Id = Guid.NewGuid(),
            StrategyInstanceId = instance.Id,
            StartedAt = clock.NowInstant(),
            InitialCapital = initialCapital,
            Status = "Running"
        };
        await sessionRepo.AddAsync(session, ct);
        _activeStates[instance.Id] = new ForwardTestState(session.Id, initialCapital);

        logger.LogInformation("[ForwardTest] Session started for {Instance} (capital={Capital})",
            instance.Name, initialCapital);
        return session.Id;
    }

    public async Task StopSessionAsync(Guid instanceId, CancellationToken ct)
    {
        if (!_activeStates.TryGetValue(instanceId, out var state)) return;
        _activeStates.Remove(instanceId);

        using var scope = scopeFactory.CreateScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IForwardTestSessionRepository>();

        var session = await sessionRepo.GetByIdAsync(state.SessionId, ct);
        if (session != null)
        {
            session.EndedAt = clock.NowInstant();
            session.FinalPnl = state.TotalPnl;
            session.FinalCapital = state.InitialCapital + state.TotalPnl;
            session.TradeCount = state.ClosedTradeCount;
            session.WinRate = state.ClosedTradeCount > 0
                ? (decimal)state.WinCount / state.ClosedTradeCount
                : 0;
            session.Status = "Stopped";
            await sessionRepo.UpdateAsync(session, ct);
        }

        logger.LogInformation("[ForwardTest] Session stopped for instance {InstanceId}. Trades={Trades} PnL={Pnl}",
            instanceId, state.ClosedTradeCount, state.TotalPnl);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PositionCloseResult? TryClosePosition(ForwardTestOpenTrade trade, ClosedCandle candle)
    {
        decimal exitPrice;
        string reason;

        if (trade.Direction == "BUY")
        {
            if (candle.Open <= trade.StopLoss)
            // Gap-fill: bar opened below SL — fill at open
            { exitPrice = candle.Open; reason = "STOP_LOSS"; }
            else if (candle.Low <= trade.StopLoss && candle.High >= trade.TakeProfit)
            {
                // Both SL and TP touched in same bar — midpoint heuristic
                var mid = (candle.High + candle.Low) / 2m;
                if (mid >= trade.TakeProfit)
                { exitPrice = trade.TakeProfit; reason = "TAKE_PROFIT"; }
                else
                { exitPrice = trade.StopLoss; reason = "STOP_LOSS"; }
            }
            else if (candle.Low <= trade.StopLoss)
            { exitPrice = trade.StopLoss; reason = "STOP_LOSS"; }
            else if (candle.High >= trade.TakeProfit)
            { exitPrice = trade.TakeProfit; reason = "TAKE_PROFIT"; }
            else return null;
        }
        else // SELL / short
        {
            if (candle.Open >= trade.StopLoss)
            // Gap-fill: bar opened above SL
            { exitPrice = candle.Open; reason = "STOP_LOSS"; }
            else if (candle.High >= trade.StopLoss && candle.Low <= trade.TakeProfit)
            {
                var mid = (candle.High + candle.Low) / 2m;
                if (mid <= trade.TakeProfit)
                { exitPrice = trade.TakeProfit; reason = "TAKE_PROFIT"; }
                else
                { exitPrice = trade.StopLoss; reason = "STOP_LOSS"; }
            }
            else if (candle.High >= trade.StopLoss)
            { exitPrice = trade.StopLoss; reason = "STOP_LOSS"; }
            else if (candle.Low <= trade.TakeProfit)
            { exitPrice = trade.TakeProfit; reason = "TAKE_PROFIT"; }
            else return null;
        }

        var pnl = trade.Direction == "BUY"
            ? (exitPrice - trade.EntryPrice) * trade.Quantity
            : (trade.EntryPrice - exitPrice) * trade.Quantity;

        return new PositionCloseResult(exitPrice, pnl, reason);
    }

    // ── Private types ─────────────────────────────────────────────────────────

    private class ForwardTestState(Guid sessionId, decimal initialCapital)
    {
        public Guid SessionId { get; } = sessionId;
        public decimal InitialCapital { get; } = initialCapital;
        public ForwardTestOpenTrade? OpenTrade { get; set; }
        public decimal TotalPnl { get; set; }
        public int ClosedTradeCount { get; set; }
        public int WinCount { get; set; }
    }

    private record ForwardTestOpenTrade(
        string Direction, int Quantity, decimal EntryPrice,
        decimal StopLoss, decimal TakeProfit, Instant OpenedAt);

    private record PositionCloseResult(decimal ExitPrice, decimal Pnl, string Reason);
}
