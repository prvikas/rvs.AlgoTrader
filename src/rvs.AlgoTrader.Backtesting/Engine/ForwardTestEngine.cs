using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Backtesting.Engine;

/// <summary>
/// Forward test (paper trading) engine.
/// Receives real-time CandleClosedEvents from CandleAggregatorService,
/// evaluates strategy signals, simulates fills, and persists virtual trades.
/// No real orders placed.
/// Implements IForwardTestEngine defined in Application layer so
/// StrategyEvaluationQueue (Infrastructure) can depend on it without
/// a direct reference to rvs.AlgoTrader.Backtesting.
/// </summary>
public class ForwardTestEngine(
    IStrategyFactory strategyFactory,
    ICandleCache candleCache,
    IForwardTestFillSimulator fillSimulator,
    IForwardTestSessionRepository sessionRepo,
    IForwardTestTradeRepository tradeRepo,
    IClock clock,
    ILogger<ForwardTestEngine> logger) : IForwardTestEngine  // IForwardTestEngine from Application.Services
{
    // ConcurrentDictionary for thread safety in case of concurrent candle processing
    private readonly Dictionary<Guid, ForwardTestState> _activeStates = new();

    public async Task ProcessCandleAsync(
        StrategyInstance instance, ClosedCandle candle, CancellationToken ct)
    {
        if (!_activeStates.TryGetValue(instance.Id, out var state))
        {
            logger.LogDebug("[ForwardTest] No active session for {Instance}", instance.Name);
            return;
        }

        var candles = await candleCache.GetAsync(instance.InternalSymbol, instance.Timeframe, 500, ct);
        if (candles.Count < 20) return;

        // Check if open position should close (SL/TP check against this candle)
        if (state.OpenTrade != null)
        {
            var closeResult = TryClosePosition(state.OpenTrade, candle);
            if (closeResult != null)
            {
                var now = clock.NowInstant();
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
                    CloseReason = closeResult.Reason,
                    OpenedAt = state.OpenTrade.OpenedAt,
                    ClosedAt = now,
                    EntryTime = state.OpenTrade.OpenedAt,
                    ExitTime = now
                };

                state.TotalPnl += closeResult.Pnl;
                state.ClosedTradeCount++;
                if (closeResult.Pnl > 0) state.WinCount++;

                await tradeRepo.AddAsync(trade, ct);
                state.OpenTrade = null;
                return;
            }
        }

        if (state.OpenTrade != null) return; // already in position

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

        if (signal.Signal is not ("BUY" or "SELL")) return;

        var config = new FillSimConfig(SlippagePct: 0.0002m);
        var fillResult = await fillSimulator.SimulateFillAsync(signal, [candle], clock, config, ct);
        if (!fillResult.Filled || fillResult.FillPrice == null) return;

        var entryPrice = fillResult.FillPrice.Value;
        state.OpenTrade = new ForwardTestOpenTrade(
            signal.Signal, 1, entryPrice,
            signal.StopLoss ?? entryPrice * 0.99m,
            signal.TakeProfit ?? entryPrice * 1.02m,
            clock.NowInstant());

        logger.LogInformation("[ForwardTest] {Signal} {Symbol} @ {Price} (simulated)",
            signal.Signal, candle.InternalSymbol, entryPrice);
    }

    public async Task<Guid> StartSessionAsync(StrategyInstance instance, decimal initialCapital, CancellationToken ct)
    {
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
        return session.Id;
    }

    public async Task StopSessionAsync(Guid instanceId, CancellationToken ct)
    {
        if (!_activeStates.TryGetValue(instanceId, out var state)) return;
        _activeStates.Remove(instanceId);

        var session = await sessionRepo.GetByIdAsync(state.SessionId, ct);
        if (session != null)
        {
            session.EndedAt = clock.NowInstant();
            session.FinalPnl = state.TotalPnl;
            session.TradeCount = state.ClosedTradeCount;
            session.WinRate = state.ClosedTradeCount > 0
                ? (decimal)state.WinCount / state.ClosedTradeCount
                : 0;
            session.Status = "Stopped";
            await sessionRepo.UpdateAsync(session, ct);
        }
    }

    private static PositionCloseResult? TryClosePosition(ForwardTestOpenTrade trade, ClosedCandle candle)
    {
        decimal exitPrice;
        string reason;

        if (trade.Direction == "BUY")
        {
            if (candle.Low <= trade.StopLoss)
            { exitPrice = trade.StopLoss; reason = "STOP_LOSS"; }
            else if (candle.High >= trade.TakeProfit)
            { exitPrice = trade.TakeProfit; reason = "TAKE_PROFIT"; }
            else return null;
        }
        else
        {
            if (candle.High >= trade.StopLoss)
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

    private class ForwardTestState
    {
        public Guid SessionId { get; }
        public decimal InitialCapital { get; }
        public ForwardTestOpenTrade? OpenTrade { get; set; }
        public decimal TotalPnl { get; set; }
        public int ClosedTradeCount { get; set; }
        public int WinCount { get; set; }

        public ForwardTestState(Guid sessionId, decimal initialCapital)
        {
            SessionId = sessionId;
            InitialCapital = initialCapital;
        }
    }

    private record ForwardTestOpenTrade(
        string Direction, int Quantity, decimal EntryPrice,
        decimal StopLoss, decimal TakeProfit, Instant OpenedAt);

    private record PositionCloseResult(decimal ExitPrice, decimal Pnl, string Reason);
}
