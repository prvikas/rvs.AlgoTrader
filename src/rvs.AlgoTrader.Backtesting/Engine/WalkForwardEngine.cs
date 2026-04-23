using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Backtesting.Engine;

/// <summary>
/// Walk-forward optimization engine.
/// Splits data into in-sample (optimize) and out-of-sample (validate) windows.
/// Prevents overfitting by validating on unseen data.
/// </summary>
public class WalkForwardEngine(BacktestEngine backtestEngine, ILogger<WalkForwardEngine> logger)
    : IWalkForwardEngine
{
    /// <inheritdoc/>
    public async Task<WalkForwardResultDto> RunAsync(BacktestRequestDto dto, CancellationToken ct)
    {
        var cfg = dto.WalkForward ?? new WalkForwardConfigDto(180, 60, 60);
        var request = new WalkForwardRequest(
            StrategyName:        dto.StrategyName,
            ParametersJson:      dto.ParametersJson,
            InternalSymbol:      dto.InternalSymbol,
            Timeframe:           dto.Timeframe,
            FromDate:            dto.FromDate,
            ToDate:              dto.ToDate,
            InitialCapital:      dto.InitialCapital,
            RiskPerTradePercent: dto.RiskPerTradePercent,
            InSampleDays:        cfg.InSampleDays,
            OosDays:             cfg.OutOfSampleDays,
            StepDays:            cfg.StepDays,
            CompoundEquity:      false,
            FillModel:           (FillModel)dto.FillModel,
            SlippageBasisPoints: dto.SlippageBasisPoints,
            BrokerageFlatPerSide:dto.BrokerageFlatPerSide,
            TrailActivationR:    dto.TrailActivationR,
            TrailOffsetR:        dto.TrailOffsetR,
            BreakEvenAt1R:       dto.BreakEvenAt1R,
            CircuitBreakerPct:   dto.CircuitBreakerPct);

        var result = await RunAsync(request, ct);

        var windows = result.Windows.Select(w => new WalkForwardWindowResultDto(
            InSampleFrom: w.Window.InSampleFrom,
            InSampleTo:   w.Window.InSampleTo,
            OoSFrom:      w.Window.OoSFrom,
            OoSTo:        w.Window.OoSTo,
            IsSuccess:    w.IsResult.Success,
            IsTotalPnl:   w.IsResult.TotalPnl,
            IsSharpe:     w.IsResult.SharpeRatio,
            OosSuccess:   w.OosResult.Success,
            OosTotalPnl:  w.OosResult.TotalPnl,
            OosSharpe:    w.OosResult.SharpeRatio)).ToList();

        return new WalkForwardResultDto(
            result.StrategyName, result.Symbol, windows,
            result.TotalOosPnl, result.EfficiencyRatio);
    }
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    public async Task<WalkForwardResult> RunAsync(WalkForwardRequest request, CancellationToken ct)
    {
        logger.LogInformation("[WalkForward] Starting for {Strategy}/{Symbol}/{Tf}",
            request.StrategyName, request.InternalSymbol, request.Timeframe);

        var windows = GenerateWindows(request);
        var windowResults  = new List<WalkForwardWindowResult>();
        decimal totalOosPnl   = 0;
        // When CompoundEquity=true, each window's IS starts where the previous OOS ended.
        // When false (default), every window starts at the same InitialCapital.
        decimal windowCapital = request.InitialCapital;

        foreach (var window in windows)
        {
            logger.LogDebug("[WalkForward] Window IS:{IsFrom}-{IsTo} OOS:{OosFrom}-{OosTo} Capital={Cap:F0}",
                window.InSampleFrom, window.InSampleTo, window.OoSFrom, window.OoSTo, windowCapital);

            var currentCapital = request.CompoundEquity ? windowCapital : request.InitialCapital;

            // In-sample run (used for optimization context; always starts at currentCapital)
            var isResult = await backtestEngine.RunAsync(new BacktestRequest(
                request.StrategyName, request.ParametersJson,
                request.InternalSymbol, request.Timeframe,
                window.InSampleFrom, window.InSampleTo,
                currentCapital, request.RiskPerTradePercent,
                FillModel:            request.FillModel,
                SlippageBasisPoints:  request.SlippageBasisPoints,
                BrokerageFlatPerSide: request.BrokerageFlatPerSide,
                TrailActivationR:     request.TrailActivationR,
                TrailOffsetR:         request.TrailOffsetR,
                BreakEvenAt1R:        request.BreakEvenAt1R,
                CircuitBreakerPct:    request.CircuitBreakerPct), ct);

            // OOS starts where IS ended when compounding, otherwise at currentCapital.
            var oosCapital = request.CompoundEquity && isResult.Success && isResult.FinalEquity > 0
                ? isResult.FinalEquity
                : currentCapital;

            var oosResult = await backtestEngine.RunAsync(new BacktestRequest(
                request.StrategyName, request.ParametersJson,
                request.InternalSymbol, request.Timeframe,
                window.OoSFrom, window.OoSTo,
                oosCapital, request.RiskPerTradePercent,
                FillModel:            request.FillModel,
                SlippageBasisPoints:  request.SlippageBasisPoints,
                BrokerageFlatPerSide: request.BrokerageFlatPerSide,
                TrailActivationR:     request.TrailActivationR,
                TrailOffsetR:         request.TrailOffsetR,
                BreakEvenAt1R:        request.BreakEvenAt1R,
                CircuitBreakerPct:    request.CircuitBreakerPct), ct);

            windowResults.Add(new WalkForwardWindowResult(window, isResult, oosResult));
            if (oosResult.Success)
            {
                totalOosPnl += oosResult.TotalPnl;
                // Carry OOS final equity forward so the next window's position sizing reflects
                // actual compounded capital.
                if (request.CompoundEquity && oosResult.FinalEquity > 0)
                    windowCapital = oosResult.FinalEquity;
            }
        }

        var efficiencyRatio = windowResults.Count > 0
            ? (decimal)windowResults.Count(w => w.OosResult.TotalPnl > 0) / windowResults.Count
            : 0;

        return new WalkForwardResult(
            request.StrategyName, request.InternalSymbol,
            windowResults, totalOosPnl, efficiencyRatio);
    }

    private static List<WalkForwardWindow> GenerateWindows(WalkForwardRequest request)
    {
        var windows = new List<WalkForwardWindow>();
        var current = request.FromDate;
        var inSampleDays = request.InSampleDays;
        var oosDays = request.OosDays;
        // StepDays=0 means "use OosDays" (non-overlapping anchored walk-forward)
        var stepDays = request.StepDays > 0 ? request.StepDays : oosDays;

        while (current.PlusDays(inSampleDays + oosDays) <= request.ToDate)
        {
            windows.Add(new WalkForwardWindow(
                current,
                current.PlusDays(inSampleDays - 1),
                current.PlusDays(inSampleDays),
                current.PlusDays(inSampleDays + oosDays - 1)));
            current = current.PlusDays(stepDays);
        }
        return windows;
    }
}

public record WalkForwardRequest(
    string StrategyName, string ParametersJson,
    string InternalSymbol, string Timeframe,
    LocalDate FromDate, LocalDate ToDate,
    decimal InitialCapital, decimal RiskPerTradePercent,
    int InSampleDays = 180, int OosDays = 60,
    // StepDays controls how far the window start advances after each iteration.
    // Default = OosDays (non-overlapping). Set smaller for overlapping OOS windows.
    int StepDays = 0,
    // When true, each window's IS starting capital = previous OOS FinalEquity (compounding mode).
    // When false (default), every window starts at InitialCapital (independent-window mode).
    bool CompoundEquity = false,
    // Cost / fill / stop parameters forwarded to every BacktestRequest in the walk-forward run.
    FillModel FillModel = FillModel.NextBarOpen,
    decimal SlippageBasisPoints = 5m,
    decimal BrokerageFlatPerSide = 20m,
    decimal TrailActivationR = 0m,
    decimal TrailOffsetR = 0.5m,
    bool BreakEvenAt1R = false,
    decimal CircuitBreakerPct = 0m);

public record WalkForwardWindow(
    LocalDate InSampleFrom, LocalDate InSampleTo,
    LocalDate OoSFrom, LocalDate OoSTo);

public record WalkForwardWindowResult(
    WalkForwardWindow Window,
    BacktestResult IsResult,
    BacktestResult OosResult);

public record WalkForwardResult(
    string StrategyName, string Symbol,
    IReadOnlyList<WalkForwardWindowResult> Windows,
    decimal TotalOosPnl, decimal EfficiencyRatio);
