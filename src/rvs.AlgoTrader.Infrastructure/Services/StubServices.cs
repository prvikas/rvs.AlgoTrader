using NodaTime;
using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Infrastructure.Services;

// ─────────────────────────────────────────────────────────────────────────────
// BacktestService: real implementation wrapping BacktestEngine.
// Maps BacktestRequestDto → BacktestRequest, runs the engine, maps result back.
// ─────────────────────────────────────────────────────────────────────────────

public class BacktestService(IBacktestEngine engine) : IBacktestService
{
    public async Task<BacktestResultDto> RunAsync(BacktestRequestDto dto, CancellationToken ct)
    {
        var request = new BacktestRequest(
            StrategyName: dto.StrategyName,
            ParametersJson: dto.ParametersJson,
            InternalSymbol: dto.InternalSymbol,
            Timeframe: dto.Timeframe,
            FromDate: dto.FromDate,
            ToDate: dto.ToDate,
            InitialCapital: dto.InitialCapital,
            RiskPerTradePercent: dto.RiskPerTradePercent,
            FillModel: (FillModel)dto.FillModel,
            SlippageBasisPoints: dto.SlippageBasisPoints,
            BrokerageFlatPerSide: dto.BrokerageFlatPerSide);

        var result = await engine.RunAsync(request, ct);
        return MapToDto(result);
    }

    public Task<object> RunWalkForwardAsync(BacktestRequestDto dto, CancellationToken ct)
        // Walk-forward: run multiple non-overlapping windows, return aggregate metrics
        => Task.FromResult<object>(new { Error = "Walk-forward UI not yet wired" });

    private static BacktestResultDto MapToDto(BacktestResult r) => new(
        Id: null,
        Success: r.Success,
        StrategyName: r.StrategyName,
        Symbol: r.Symbol,
        Timeframe: r.Timeframe,
        FromDate: r.FromDate,
        ToDate: r.ToDate,
        InitialCapital: r.InitialCapital,
        FinalEquity: r.FinalEquity,
        TotalPnl: r.TotalPnl,
        TotalReturn: r.TotalReturn,
        MaxDrawdown: r.MaxDrawdown,
        SharpeRatio: r.SharpeRatio,
        CalmarRatio: r.CalmarRatio,
        ProfitFactor: r.ProfitFactor,
        WinRate: r.WinRate,
        TotalTrades: r.TotalTrades,
        WinCount: r.WinCount,
        LossCount: r.LossCount,
        AvgWin: r.AvgWin,
        AvgLoss: r.AvgLoss,
        MaxConsecutiveLosses: r.MaxConsecutiveLosses,
        ExpectancyPerTrade: r.ExpectancyPerTrade,
        DataHash: r.DataHash,
        Error: r.Error,
        StartedAt: DateTimeOffset.UtcNow,
        Trades: r.Trades.Select(t => new BacktestTradeDto(
            Direction: t.Direction,
            EntryPrice: t.EntryPrice,
            ExitPrice: t.ExitPrice,
            Quantity: t.Quantity,
            ExitReason: t.ExitReason,
            GrossPnl: t.GrossPnl,
            NetPnl: t.NetPnl,
            EntryTime: t.EntryTime.ToInstant().ToDateTimeOffset().ToString("o"),
            ExitTime: t.ExitTime.ToInstant().ToDateTimeOffset().ToString("o")
        )).ToList());
}

/// <summary>
/// Stub backtest reproduction service.
/// </summary>
public class BacktestReproductionService : IBacktestReproductionService
{
    public Task<BacktestResultDto?> ReproduceAsync(BacktestResultDto original, CancellationToken ct)
        => Task.FromResult<BacktestResultDto?>(null);
}
