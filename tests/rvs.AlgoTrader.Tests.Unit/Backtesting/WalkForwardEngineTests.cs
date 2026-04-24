using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Backtesting.Engine;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Tests.Unit.Backtesting;

public class WalkForwardEngineTests
{
    private static BacktestResult MakeResult(bool success, decimal pnl, decimal sharpe = 0.5m) =>
        new(
            StrategyName: "Test", Symbol: "NIFTY", Timeframe: "1d",
            FromDate: new LocalDate(2023, 1, 1), ToDate: new LocalDate(2023, 6, 30),
            InitialCapital: 100_000m,
            FinalEquity: success ? 100_000m + pnl : 100_000m,
            TotalPnl: pnl,
            TotalReturn: pnl / 100_000m,
            MaxDrawdown: 0.05m,
            SharpeRatio: sharpe,
            CalmarRatio: 1m, ProfitFactor: 1.5m, WinRate: 0.6m,
            TotalTrades: 10, WinCount: 6, LossCount: 4,
            AvgWin: 500m, AvgLoss: -300m, MaxConsecutiveLosses: 2,
            ExpectancyPerTrade: 180m, SortinoRatio: 0.8m,
            DailySharpe: sharpe, MonthlySharpe: sharpe, MonthlyWinRate: 0.6m,
            DrawdownRecoveryBars: 5, MaxLots: 10, DataHash: "hash",
            Error: success ? null : "fail",
            Success: success,
            Trades: [], MonthlyBreakdown: [], YearlyBreakdown: [],
            ChartSample: null,
            CircuitBreakerHit: false, CircuitBreakerReason: null,
            VaR95: 0m, CVaR95: 0m, OmegaRatio: 1m, Skewness: 0m, Kurtosis: 0m,
            DeploymentRating: "B", DeploymentRationale: "ok",
            SkippedSignalCount: 0);

    private static WalkForwardEngine BuildEngine(IBacktestEngine engine) =>
        new(engine, NullLogger<WalkForwardEngine>.Instance);

    // ── GenerateWindows via RunAsync ──────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ProducesCorrectWindowCount()
    {
        // 365 days total: IS=180, OOS=60 → step=60 → windows at day 0, 60, 120, 180 = 4 windows
        // window valid when current + 180 + 60 <= ToDate → current <= ToDate - 240
        // ToDate - FromDate = 364 days; 0+240<=364, 60+240<=364, 120+240<=364, 180+240=420>364 → 3 windows
        var mockEngine = new Mock<IBacktestEngine>();
        mockEngine.Setup(e => e.RunAsync(It.IsAny<BacktestRequest>(), It.IsAny<CancellationToken>(), null))
                  .ReturnsAsync(MakeResult(true, 1000m));

        var engine = BuildEngine(mockEngine.Object);
        var dto = new BacktestRequestDto(
            StrategyName: "VCP", ParametersJson: "{}",
            InternalSymbol: "NIFTY", Timeframe: "1d",
            FromDate: new LocalDate(2023, 1, 1),
            ToDate: new LocalDate(2023, 12, 31),
            WalkForward: new WalkForwardConfigDto(180, 60, 60));

        var result = await engine.RunAsync(dto, CancellationToken.None);

        result.Windows.Count.Should().Be(3);
        mockEngine.Verify(e => e.RunAsync(It.IsAny<BacktestRequest>(), It.IsAny<CancellationToken>(), null),
            Times.Exactly(6)); // 2 calls per window (IS + OOS)
    }

    [Fact]
    public async Task RunAsync_EfficiencyRatio_IsCorrect()
    {
        // IS=180, OOS=60, Step=60, range Jan1–Nov27 (330 days) → exactly 2 windows:
        //   window1: Jan01+240=Aug29 ≤ Nov27 ✓, window2: Mar02+240=Oct28 ≤ Nov27 ✓
        //   window3: May01+240=Dec27 > Nov27 ✗ → stop
        // OOS pnl = +1000, -500 → 1 profitable out of 2 → ratio = 0.5
        var mockEngine = new Mock<IBacktestEngine>();
        var seq = mockEngine.SetupSequence(e => e.RunAsync(It.IsAny<BacktestRequest>(), It.IsAny<CancellationToken>(), null));
        seq.ReturnsAsync(MakeResult(true, 500m));   // IS window 1
        seq.ReturnsAsync(MakeResult(true, 1000m));  // OOS window 1 — profitable
        seq.ReturnsAsync(MakeResult(true, 500m));   // IS window 2
        seq.ReturnsAsync(MakeResult(true, -500m));  // OOS window 2 — loss

        var engine = BuildEngine(mockEngine.Object);
        var dto = new BacktestRequestDto(
            StrategyName: "VCP", ParametersJson: "{}",
            InternalSymbol: "NIFTY", Timeframe: "1d",
            FromDate: new LocalDate(2023, 1, 1),
            ToDate: new LocalDate(2023, 11, 27),   // 330-day range → exactly 2 windows
            WalkForward: new WalkForwardConfigDto(180, 60, 60));

        var result = await engine.RunAsync(dto, CancellationToken.None);

        result.Windows.Count.Should().Be(2);
        result.EfficiencyRatio.Should().Be(0.5m);
        result.TotalOosPnl.Should().Be(500m); // 1000 + (-500)
    }

    [Fact]
    public async Task RunAsync_StepDaysSmallerThanOosDays_ProducesMoreWindows()
    {
        // IS=180, OOS=60, Step=30 → more windows than Step=60
        var mockEngine = new Mock<IBacktestEngine>();
        mockEngine.Setup(e => e.RunAsync(It.IsAny<BacktestRequest>(), It.IsAny<CancellationToken>(), null))
                  .ReturnsAsync(MakeResult(true, 200m));

        var engine = BuildEngine(mockEngine.Object);

        var dtoStep60 = new BacktestRequestDto("VCP", "{}", "NIFTY", "1d",
            new LocalDate(2023, 1, 1), new LocalDate(2023, 12, 31),
            WalkForward: new WalkForwardConfigDto(180, 60, 60));
        var dtoStep30 = new BacktestRequestDto("VCP", "{}", "NIFTY", "1d",
            new LocalDate(2023, 1, 1), new LocalDate(2023, 12, 31),
            WalkForward: new WalkForwardConfigDto(180, 60, 30));

        var r60 = await engine.RunAsync(dtoStep60, CancellationToken.None);
        var r30 = await engine.RunAsync(dtoStep30, CancellationToken.None);

        r30.Windows.Count.Should().BeGreaterThan(r60.Windows.Count);
    }

    [Fact]
    public async Task RunAsync_WindowDatesAreContiguous()
    {
        var mockEngine = new Mock<IBacktestEngine>();
        mockEngine.Setup(e => e.RunAsync(It.IsAny<BacktestRequest>(), It.IsAny<CancellationToken>(), null))
                  .ReturnsAsync(MakeResult(true, 500m));

        var engine = BuildEngine(mockEngine.Object);
        var dto = new BacktestRequestDto("VCP", "{}", "NIFTY", "1d",
            new LocalDate(2023, 1, 1), new LocalDate(2023, 12, 31),
            WalkForward: new WalkForwardConfigDto(180, 60, 60));

        var result = await engine.RunAsync(dto, CancellationToken.None);

        foreach (var w in result.Windows)
        {
            // IS ends one day before OOS starts
            w.InSampleTo.PlusDays(1).Should().Be(w.OoSFrom);
            // IS window spans exactly 180 days inclusive: To = From + 179
            w.InSampleTo.Should().Be(w.InSampleFrom.PlusDays(179));
            // OOS window spans exactly 60 days inclusive: To = From + 59
            w.OoSTo.Should().Be(w.OoSFrom.PlusDays(59));
        }
    }

    [Fact]
    public async Task RunAsync_CostFieldsForwardedToEachBacktestRequest()
    {
        var capturedRequests = new List<BacktestRequest>();
        var mockEngine = new Mock<IBacktestEngine>();
        mockEngine.Setup(e => e.RunAsync(It.IsAny<BacktestRequest>(), It.IsAny<CancellationToken>(), null))
                  .Callback<BacktestRequest, CancellationToken, IProgress<BacktestProgress>?>(
                      (req, _, _) => capturedRequests.Add(req))
                  .ReturnsAsync(MakeResult(true, 100m));

        var engine = BuildEngine(mockEngine.Object);
        var dto = new BacktestRequestDto("VCP", "{}", "NIFTY", "1d",
            new LocalDate(2023, 1, 1), new LocalDate(2023, 12, 31),
            FillModel: 1,           // NextBarOpenPlusSlippage
            SlippageBasisPoints: 10m,
            BrokerageFlatPerSide: 25m,
            TrailActivationR: 2m,
            TrailOffsetR: 0.25m,
            BreakEvenAt1R: true,
            CircuitBreakerPct: 0.3m,
            WalkForward: new WalkForwardConfigDto(180, 60, 60));

        await engine.RunAsync(dto, CancellationToken.None);

        capturedRequests.Should().NotBeEmpty();
        foreach (var req in capturedRequests)
        {
            req.FillModel.Should().Be(FillModel.NextBarOpenPlusSlippage);
            req.SlippageBasisPoints.Should().Be(10m);
            req.BrokerageFlatPerSide.Should().Be(25m);
            req.TrailActivationR.Should().Be(2m);
            req.TrailOffsetR.Should().Be(0.25m);
            req.BreakEvenAt1R.Should().BeTrue();
            req.CircuitBreakerPct.Should().Be(0.3m);
        }
    }
}
