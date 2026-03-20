using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Backtesting.Engine;

/// <summary>
/// Walk-forward optimization engine.
/// Splits data into in-sample (optimize) and out-of-sample (validate) windows.
/// Prevents overfitting by validating on unseen data.
/// </summary>
public class WalkForwardEngine(BacktestEngine backtestEngine, ILogger<WalkForwardEngine> logger)
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    public async Task<WalkForwardResult> RunAsync(WalkForwardRequest request, CancellationToken ct)
    {
        logger.LogInformation("[WalkForward] Starting for {Strategy}/{Symbol}/{Tf}",
            request.StrategyName, request.InternalSymbol, request.Timeframe);

        var windows = GenerateWindows(request);
        var windowResults = new List<WalkForwardWindowResult>();
        decimal totalOosPnl = 0;

        foreach (var window in windows)
        {
            logger.LogDebug("[WalkForward] Window IS:{IsFrom}-{IsTo} OOS:{OosFrom}-{OosTo}",
                window.InSampleFrom, window.InSampleTo, window.OoSFrom, window.OoSTo);

            // In-sample run
            var isResult = await backtestEngine.RunAsync(new BacktestRequest(
                request.StrategyName, request.ParametersJson,
                request.InternalSymbol, request.Timeframe,
                window.InSampleFrom, window.InSampleTo,
                request.InitialCapital, request.RiskPerTradePercent), ct);

            // Out-of-sample validation
            var oosResult = await backtestEngine.RunAsync(new BacktestRequest(
                request.StrategyName, request.ParametersJson,
                request.InternalSymbol, request.Timeframe,
                window.OoSFrom, window.OoSTo,
                request.InitialCapital, request.RiskPerTradePercent), ct);

            windowResults.Add(new WalkForwardWindowResult(window, isResult, oosResult));
            if (oosResult.Success) totalOosPnl += oosResult.TotalPnl;
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

        while (current.PlusDays(inSampleDays + oosDays) <= request.ToDate)
        {
            windows.Add(new WalkForwardWindow(
                current,
                current.PlusDays(inSampleDays - 1),
                current.PlusDays(inSampleDays),
                current.PlusDays(inSampleDays + oosDays - 1)));
            current = current.PlusDays(oosDays); // anchored walk-forward
        }
        return windows;
    }
}

public record WalkForwardRequest(
    string StrategyName, string ParametersJson,
    string InternalSymbol, string Timeframe,
    LocalDate FromDate, LocalDate ToDate,
    decimal InitialCapital, decimal RiskPerTradePercent,
    int InSampleDays = 180, int OosDays = 60);

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
