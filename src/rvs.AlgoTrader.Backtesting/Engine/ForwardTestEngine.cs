using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using static rvs.AlgoTrader.Domain.Enums.SizingModel;

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
    IPositionSizingEngine sizingEngine,
    IClock clock,
    ILogger<ForwardTestEngine> logger,
    // SS-1: Required for spread simulation (MaxLossMultiple enforcement in paper trading).
    // Singleton — safe to inject into a Singleton ForwardTestEngine.
    IBlackScholesEngine bsEngine) : IForwardTestEngine
{
    // Keyed by strategy instance ID; holds in-memory position/P&L state.
    // ConcurrentDictionary guards against concurrent Start/Stop vs ProcessCandle races.
    private readonly ConcurrentDictionary<Guid, ForwardTestState> _activeStates = new();

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
        var candleCache        = sp.GetRequiredService<ICandleCache>();
        var fillSimulator      = sp.GetRequiredService<IForwardTestFillSimulator>();
        var tradeRepo          = sp.GetRequiredService<IForwardTestTradeRepository>();
        var optionChainService = sp.GetService<IOptionChainService>(); // optional — null when not registered

        var candles = await candleCache.GetAsync(instance.InternalSymbol, instance.Timeframe, 500, ct);
        if (candles.Count < 20) return;

        // ── SS-1: Monitor open spread position per bar ────────────────────
        if (state.OpenSpread != null)
        {
            var barDate     = candle.OpenTime.Date;
            decimal spot    = candle.Close;
            bool   spreadClosed = false;
            string closeReason  = "";
            decimal spreadPnl   = 0m;

            // Expiry check
            if (barDate >= state.OpenSpread.ExpiryDate)
            {
                // At expiry net time-value = 0 → costToClose ≈ 0 → P&L = NetCredit.
                // Conservative for debit spreads (ignores far leg residual value), but acceptable
                // given the linear approximation used throughout ForwardTestEngine.
                spreadPnl   = FtSpreadPnl(state.OpenSpread);
                spreadClosed = true; closeReason = "EXPIRY";
            }
            else
            {
                decimal currentVal = FtSpreadCurrentValue(state.OpenSpread, spot, barDate);
                // Unified P&L = NetCredit − cost-to-close (see BacktestEngine.TryCloseSpreadSim).
                spreadPnl = state.OpenSpread.NetCredit - currentVal;

                decimal absCredit = Math.Abs(state.OpenSpread.NetCredit);
                // SS-1: MaxLossMultiple
                if (absCredit > 0 && spreadPnl <= -(absCredit * state.OpenSpread.MaxLossMultiple))
                { spreadClosed = true; closeReason = "MAX_LOSS_MULTIPLE"; }
                // Profit target
                else if (absCredit > 0 && spreadPnl >= absCredit * state.OpenSpread.ProfitTargetPct)
                { spreadClosed = true; closeReason = "PROFIT_TARGET"; }
                // FIB-2: underlying stop
                else if (state.OpenSpread.UnderlyingStop.HasValue)
                {
                    bool isUptrend = state.OpenSpread.SpreadType.Contains("put", StringComparison.OrdinalIgnoreCase);
                    bool breached  = isUptrend ? spot < state.OpenSpread.UnderlyingStop.Value
                                               : spot > state.OpenSpread.UnderlyingStop.Value;
                    if (breached) { spreadPnl = -absCredit * state.OpenSpread.MaxLossMultiple; spreadClosed = true; closeReason = "UNDERLYING_STOP"; }
                }
            }

            if (spreadClosed)
            {
                var closedTrade = new ForwardTestTrade
                {
                    Id              = Guid.NewGuid(),
                    SessionId       = state.SessionId,
                    InternalSymbol  = candle.InternalSymbol,
                    Direction       = state.OpenSpread.NetCredit >= 0 ? "CREDIT-SPREAD" : "DEBIT-SPREAD",
                    Quantity        = state.OpenSpread.LegCount,
                    EntryPrice      = Math.Abs(state.OpenSpread.NetCredit),
                    ExitPrice       = Math.Abs(state.OpenSpread.NetCredit) - spreadPnl,
                    SimulatedFillPrice = Math.Abs(state.OpenSpread.NetCredit) - spreadPnl,
                    Slippage        = 0m,
                    Pnl             = spreadPnl,
                    RealizedPnl     = spreadPnl,
                    CloseReason     = closeReason,
                    OpenedAt        = state.OpenSpread.EntryTime,
                    ClosedAt        = clock.NowInstant(),
                    EntryTime       = state.OpenSpread.EntryTime,
                    ExitTime        = candle.OpenTime.ToInstant()
                };
                state.TotalPnl += spreadPnl;
                state.ClosedTradeCount++;
                if (spreadPnl > 0) state.WinCount++;
                await tradeRepo.AddAsync(closedTrade, ct);
                state.OpenSpread = null;
                logger.LogInformation("[ForwardTest] Spread closed: {Reason} PnL={Pnl:F2}", closeReason, spreadPnl);
            }

            if (state.OpenSpread != null) return; // still managing spread — no new entry
        }

        // ── Check if open single-leg position should close (SL/TP) ────────
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
        var strategy      = strategyFactory.Create(instance.StrategyType, instance.ParametersJson);
        var correlationId = Guid.NewGuid().ToString("N");

        // CS-1: Fetch near and far option chains for CalendarSpread strategies.
        // Also fetch near chain for all option strategies so they can run their IV filters.
        OptionChainSnapshot? nearChain = null;
        OptionChainSnapshot? farChain  = null;
        if (optionChainService != null)
        {
            try
            {
                var nearExpiry = optionChainService.GetNearestWeeklyExpiry(instance.InternalSymbol);
                nearChain = await optionChainService.GetSnapshotAsync(instance.InternalSymbol, nearExpiry, ct);

                // CS-1: far chain only for CalendarSpread
                if (instance.StrategyType == "CalendarSpread")
                {
                    var farExpiry = optionChainService.GetNearestMonthlyExpiry(instance.InternalSymbol);
                    farChain = await optionChainService.GetSnapshotAsync(instance.InternalSymbol, farExpiry, ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug("[ForwardTest] Option chain unavailable for {Symbol}: {Err}",
                    instance.InternalSymbol, ex.Message);
            }
        }

        var context = new StrategyContext(
            instance.Id,
            instance.InternalSymbol,
            instance.Timeframe,
            candles,
            instance.ParametersJson ?? "{}",
            correlationId,
            OptionChain:     nearChain,
            NearExpiryChain: nearChain,   // CS-1
            FarExpiryChain:  farChain);   // CS-1

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

        // ── SS-1: Handle spread entry ────────────────────────────────────
        if (signal.Spread != null)
        {
            var barDate    = candle.OpenTime.Date;
            decimal atmIvPct = nearChain?.AtmIv
                ?? (signal.DiagnosticsJson is IReadOnlyDictionary<string, decimal> d
                    && d.TryGetValue("atmIv", out var iv) ? iv : 15m);

            double ivFrac  = Math.Max(0.05, (double)(atmIvPct / 100m));
            var nearExpiry = BacktestEngine.NearestWeeklyExpiry(barDate);
            var farExpiry  = BacktestEngine.NearestMonthlyExpiry(barDate);

            decimal netCredit = 0m;
            int     legCount  = 0;
            const double RFR  = 0.065;

            foreach (var leg in signal.Spread.Legs)
            {
                var expiry = leg.NearestWeekly ? nearExpiry : farExpiry;
                double tte = Math.Max(0.001, (expiry - barDate).Days / 365.0);
                decimal si = 50m; // default NIFTY strike interval
                decimal strike = BacktestEngine.ResolveStrike(leg, candle.Close, ivFrac, tte, RFR, si, bsEngine);
                var g = bsEngine.Compute(candle.Close, strike, tte, ivFrac, RFR,
                    leg.OptionType == OptionType.Call);
                decimal prem = Math.Max(0m, g.TheoreticalPrice);
                netCredit += leg.Direction == OrderDirection.Sell ? prem : -prem;
                legCount++;
            }

            var (maxLoss, profitTarget, _) = BacktestEngine.ExtractSpreadConfig(instance.ParametersJson);
            state.OpenSpread = new ForwardTestSpreadState(
                SpreadType:       signal.Spread.SpreadType,
                ExpiryDate:       nearExpiry,
                NetCredit:        netCredit,
                MaxLossMultiple:  maxLoss,
                ProfitTargetPct:  profitTarget,
                UnderlyingStop:   signal.Spread.UnderlyingStopLevel,
                EntryTime:        candle.OpenTime.ToInstant(),
                EntryIvFraction:  ivFrac,
                LegCount:         legCount)
            { OriginalDte = Math.Max(1.0, (nearExpiry - barDate).Days) };

            logger.LogInformation("[ForwardTest] Spread entered {Type} credit={Credit:F2}", signal.Spread.SpreadType, netCredit);
            return;
        }

        if (signal.Signal is not (SignalType.Buy or SignalType.Sell)) return;

        var fillConfig = new FillSimConfig(SlippagePct: 0.0002m);
        var fillResult = await fillSimulator.SimulateFillAsync(signal, [candle], clock, fillConfig, ct);
        if (!fillResult.Filled || fillResult.FillPrice == null) return;

        var entryPrice = fillResult.FillPrice.Value;
        var credential = instance.Credential ?? throw new InvalidOperationException($"BrokerCredential not found for instance {instance.Id}");

        // #176: Use risk-based position sizing (1% of allocated capital per trade) instead of
        // a fixed lot size, so position size scales with account equity and respects stop distance.
        var allocatedCapital = instance.AllocatedCapital > 0 ? instance.AllocatedCapital
                             : credential.LotSize > 0 ? credential.LotSize * entryPrice : entryPrice;
        if (allocatedCapital < 100)
        {
            logger.LogWarning("[ForwardTest] AllocatedCapital not configured for {Instance} — skipping signal", instance.Name);
            return;
        }
        var (lots, sizingRationale) = sizingEngine.Compute(
            FixedFractional,
            allocatedCapital,
            entryPrice,
            signal.StopLoss,
            atr: null,
            new PositionSizingConfig(FixedLots: credential.LotSize > 0 ? credential.LotSize : 1));
        logger.LogDebug("[ForwardTest] Sizing: {Rationale}", sizingRationale);

        state.OpenTrade = new ForwardTestOpenTrade(
            signal.Signal.ToString().ToUpperInvariant(), Math.Max(1, lots), entryPrice,
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
        _activeStates[instance.Id] = new ForwardTestState(session.Id, initialCapital); // ConcurrentDictionary indexer is thread-safe

        logger.LogInformation("[ForwardTest] Session started for {Instance} (capital={Capital})",
            instance.Name, initialCapital);
        return session.Id;
    }

    public async Task StopSessionAsync(Guid instanceId, CancellationToken ct)
    {
        if (!_activeStates.TryRemove(instanceId, out var state)) return;

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

    public async Task RecoverActiveSessionsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IForwardTestSessionRepository>();

        var runningSessions = await sessionRepo.GetRunningAsync(ct);
        var recovered = 0;

        foreach (var session in runningSessions)
        {
            // Re-hydrate in-memory state from last known session values.
            // Open position is NOT restored here (Phase 2 requires persisted open position columns).
            // Any open trade is treated as closed at session stop price — the engine resumes tracking
            // from a flat position, which is the safe default.
            var state = new ForwardTestState(session.Id, session.InitialCapital);
            if (_activeStates.TryAdd(session.StrategyInstanceId, state))
            {
                recovered++;
                logger.LogInformation(
                    "[ForwardTest] Recovered session {SessionId} for instance {InstanceId}",
                    session.Id, session.StrategyInstanceId);
            }
        }

        logger.LogInformation("[ForwardTest] Session recovery complete — {Recovered} of {Total} running sessions restored",
            recovered, runningSessions.Count);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PositionCloseResult? TryClosePosition(ForwardTestOpenTrade trade, ClosedCandle candle)
    {
        decimal exitPrice;
        string reason;

        if (trade.Direction == "BUY")
        {
            // Gap-fill: bar opened below SL — fill at open
            if (candle.Open <= trade.StopLoss)
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
            // Gap-fill: bar opened above SL
            if (candle.Open >= trade.StopLoss)
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

    // ── Spread simulation helpers (SS-1) ─────────────────────────────────────

    private static decimal FtSpreadCurrentValue(ForwardTestSpreadState pos, decimal spot, LocalDate barDate)
    {
        // Simple approximation: use linear time-value decay from entry DTE
        // Full B-S repricing is too expensive per bar without knowing individual strikes;
        // use a net intrinsic proxy: (expiryDate - barDate).Days / totalDte × entryNetAbs
        // This is a conservative approximation; SpreadBacktestEngine uses full B-S.
        double daysLeft  = Math.Max(0.0, (pos.ExpiryDate - barDate).Days);
        double totalDays = Math.Max(1.0, pos.OriginalDte);
        decimal fraction = (decimal)(daysLeft / totalDays);
        return Math.Abs(pos.NetCredit) * fraction; // time-value decays linearly as approximation
    }

    // At expiry the linear time-value approximation returns 0 (daysLeft = 0),
    // so cost-to-close ≈ 0 and P&L = NetCredit (positive for credit spreads,
    // negative for debit spreads unless the far leg is worth more — not modelled here).
    private static decimal FtSpreadPnl(ForwardTestSpreadState pos) => pos.NetCredit;

    // ── Private types ─────────────────────────────────────────────────────────

    private class ForwardTestState(Guid sessionId, decimal initialCapital)
    {
        public Guid    SessionId      { get; }      = sessionId;
        public decimal InitialCapital { get; }      = initialCapital;
        public ForwardTestOpenTrade?   OpenTrade    { get; set; }
        // SS-1: open spread tracking
        public ForwardTestSpreadState? OpenSpread   { get; set; }
        public decimal TotalPnl       { get; set; }
        public int     ClosedTradeCount { get; set; }
        public int     WinCount       { get; set; }
    }

    private record ForwardTestOpenTrade(
        string Direction, int Quantity, decimal EntryPrice,
        decimal StopLoss, decimal TakeProfit, Instant OpenedAt);

    /// <summary>
    /// SS-1: In-memory state for a simulated spread position in ForwardTestEngine.
    /// Created when strategy signals a spread; closed when exit conditions are met.
    /// </summary>
    private sealed record ForwardTestSpreadState(
        string   SpreadType,
        LocalDate ExpiryDate,
        decimal  NetCredit,         // positive = credit received; negative = debit paid
        decimal  MaxLossMultiple,
        decimal  ProfitTargetPct,
        decimal? UnderlyingStop,
        Instant  EntryTime,
        double   EntryIvFraction,
        int      LegCount)
    {
        // Original DTE at entry — used for linear time-decay approximation in FtSpreadCurrentValue
        public double OriginalDte { get; init; } = 7.0; // default 1 week; overridden at construction
    }

    private record PositionCloseResult(decimal ExitPrice, decimal Pnl, string Reason);
}
