using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Infrastructure.Services;

// ─────────────────────────────────────────────────────────────────────────────
// BacktestService: real implementation wrapping BacktestEngine.
// Maps BacktestRequestDto → BacktestRequest, runs the engine, maps result back.
// Auto-downloads candle history from broker when the DB has insufficient data.
// ─────────────────────────────────────────────────────────────────────────────

public class BacktestService(
    IBacktestEngine engine,
    IWalkForwardEngine walkForwardEngine,
    IHistoricalDownloadService downloader,
    IBacktestRunRepository runRepo,
    IClock clock,
    ILogger<BacktestService> logger) : IBacktestService
{
    public async Task<BacktestResultDto> RunAsync(BacktestRequestDto dto, CancellationToken ct)
    {
        var startedAt = clock.NowInstant().ToDateTimeOffset();
        var request = BuildRequest(dto);

        // First attempt
        var result = await engine.RunAsync(request, ct, progress: null);

        // If insufficient data, auto-download from broker and retry once
        if (!result.Success &&
            result.Error != null &&
            result.Error.StartsWith("Insufficient candle data"))
        {
            logger.LogInformation(
                "[BacktestService] Insufficient candle data for {Symbol}/{Tf}. Auto-downloading from {Broker}...",
                dto.InternalSymbol, dto.Timeframe, dto.BrokerName);

            var from = new DateOnly(dto.FromDate.Year, dto.FromDate.Month, dto.FromDate.Day);
            var to   = new DateOnly(dto.ToDate.Year,   dto.ToDate.Month,   dto.ToDate.Day);

            // Download the lowest useful timeframe: prefer 1m, fall back to requested
            var downloadTf = dto.Timeframe == "1d" ? "1d" : "1m";
            var dl = await downloader.DownloadAsync(dto.InternalSymbol, dto.BrokerName, downloadTf, from, to, ct);

            if (!dl.Success || dl.BarCount == 0)
            {
                return MapToDto(result, startedAt) with
                {
                    Error = $"No data available for {dto.InternalSymbol}/{dto.Timeframe} " +
                            $"({dto.FromDate} – {dto.ToDate}). " +
                            $"Download attempt: {dl.Error ?? "0 bars returned"}. " +
                            "Ensure the instrument has a valid broker token and the date range is correct."
                };
            }

            logger.LogInformation("[BacktestService] Downloaded {Count} bars ({Tf}). Retrying backtest...",
                dl.BarCount, downloadTf);

            result = await engine.RunAsync(request, ct, progress: null);
        }

        // Persist successful run to DB
        if (result.Success)
        {
            var dto2 = MapToDto(result, startedAt);
            await runRepo.SaveAsync(dto2, ct);
            return dto2;
        }

        return MapToDto(result, startedAt);
    }

    public async Task<object> RunWalkForwardAsync(BacktestRequestDto dto, CancellationToken ct)
    {
        logger.LogInformation("[BacktestService] Walk-forward for {Strategy}/{Symbol}/{Tf}",
            dto.StrategyName, dto.InternalSymbol, dto.Timeframe);
        return await walkForwardEngine.RunAsync(dto, ct);
    }

    private static BacktestRequest BuildRequest(BacktestRequestDto dto) => new(
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
        BrokerageFlatPerSide: dto.BrokerageFlatPerSide,
        TrailActivationR: dto.TrailActivationR,
        TrailOffsetR: dto.TrailOffsetR,
        BreakEvenAt1R: dto.BreakEvenAt1R,
        CircuitBreakerPct: dto.CircuitBreakerPct);

    private static BacktestResultDto MapToDto(BacktestResult r, DateTimeOffset startedAt) => new(
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
        SortinoRatio: r.SortinoRatio,
        DailySharpe: r.DailySharpe,
        MonthlySharpe: r.MonthlySharpe,
        MonthlyWinRate: r.MonthlyWinRate,
        DrawdownRecoveryBars: r.DrawdownRecoveryBars,
        MaxLots: r.MaxLots,
        DataHash: r.DataHash,
        Error: r.Error,
        StartedAt: startedAt,
        Trades: r.Trades.Select(t =>
        {
            // R-multiple: NetPnl ÷ initial monetary risk
            var riskPerUnit = Math.Abs(t.EntryPrice - t.InitialStopLoss);
            var initialRisk = riskPerUnit > 0 ? riskPerUnit * t.Quantity : 1m;
            return new BacktestTradeDto(
                Direction: t.Direction,
                EntryPrice: t.EntryPrice,
                ExitPrice: t.ExitPrice,
                Quantity: t.Quantity,
                ExitReason: t.ExitReason,
                GrossPnl: t.GrossPnl,
                NetPnl: t.NetPnl,
                EntryTime: t.EntryTime.ToInstant().ToDateTimeOffset().ToString("o"),
                ExitTime: t.ExitTime.ToInstant().ToDateTimeOffset().ToString("o"),
                Mae: t.Direction == "BUY"
                    ? Math.Max(0m, t.EntryPrice - t.WorstPrice)
                    : Math.Max(0m, t.WorstPrice - t.EntryPrice),
                Mfe: t.Direction == "BUY"
                    ? Math.Max(0m, t.BestPrice - t.EntryPrice)
                    : Math.Max(0m, t.EntryPrice - t.BestPrice),
                EntryCommission: t.EntryCommission,
                ExitCommission:  t.ExitCommission,
                TotalCost:       t.EntryCommission + t.ExitCommission,
                SlippageAmount:  t.SlippageAmount,
                StopLoss:        t.StopLoss,
                TakeProfit:      t.TakeProfit,
                HoldingBars:     t.HoldingBars,
                RMultiple:       Math.Round(t.NetPnl / initialRisk, 2),
                LegsJson:        t.LegsJson);
        }).ToList(),
        MonthlyBreakdown: r.MonthlyBreakdown.Select(m => new BacktestMonthlyBreakdownDto(m.Year, m.Month, m.Pnl, m.Trades, m.WinRate)).ToList(),
        YearlyBreakdown: r.YearlyBreakdown.Select(y => new BacktestYearlyBreakdownDto(y.Year, y.Pnl, y.Return, y.Trades, y.WinRate)).ToList(),
        ChartSample: r.ChartSample?.Select(b => new BacktestChartBarDto(
            b.TimeMs, b.Open, b.High, b.Low, b.Close, b.Volume,
            b.Signal, b.SignalPrice, b.StopLoss, b.TakeProfit, b.Indicators)).ToList(),
        CircuitBreakerHit: r.CircuitBreakerHit,
        CircuitBreakerReason: r.CircuitBreakerReason,
        VaR95: r.VaR95,
        CVaR95: r.CVaR95,
        OmegaRatio: r.OmegaRatio,
        Skewness: r.Skewness,
        Kurtosis: r.Kurtosis,
        DeploymentRating: r.DeploymentRating,
        DeploymentRationale: r.DeploymentRationale,
        SkippedSignalCount: r.SkippedSignalCount);
}

/// <summary>
/// Re-runs a previously completed backtest using the same symbol/timeframe/date range.
/// ParametersJson is not stored in the result DTO so this uses an empty JSON object
/// (strategy defaults). The caller can compare DataHash to verify deterministic output.
/// </summary>
public class BacktestReproductionService(IBacktestService backtest) : IBacktestReproductionService
{
    public async Task<BacktestResultDto?> ReproduceAsync(BacktestResultDto original, CancellationToken ct)
    {
        var request = new BacktestRequestDto(
            StrategyName:         original.StrategyName,
            ParametersJson:       "{}",
            InternalSymbol:       original.Symbol,
            Timeframe:            original.Timeframe,
            FromDate:             original.FromDate,
            ToDate:               original.ToDate,
            InitialCapital:       original.InitialCapital,
            ScenarioId:           original.ScenarioId);

        var result = await backtest.RunAsync(request, ct);
        return result;
    }
}
