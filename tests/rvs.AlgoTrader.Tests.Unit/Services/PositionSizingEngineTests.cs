using Microsoft.Extensions.Logging.Abstractions;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Infrastructure.Services;

namespace rvs.AlgoTrader.Tests.Unit.Services;

/// <summary>
/// UT-1: Kelly criterion edge cases + hard-cap enforcement.
/// Covers PS-1 formula regression and the MaxCapitalPct cap from P007 review.
/// </summary>
public class PositionSizingEngineTests
{
    private readonly PositionSizingEngine _engine = new(NullLogger<PositionSizingEngine>.Instance);

    // ── Kelly formula ────────────────────────────────────────────────────────

    [Fact]
    public void Kelly_TypicalInputs_ProducesPositiveSize()
    {
        // w=0.55, r=1.5 → kelly = 0.5*(0.55 - 0.45/1.5) = 0.5*(0.55 - 0.30) = 0.5*0.25 = 0.125
        var config = new PositionSizingConfig(WinRate: 0.55m, AvgWinLossRatio: 1.5m, MaxLots: 1000, MaxCapitalPct: 50m);
        var (lots, rationale) = _engine.Compute(SizingModel.KellyCriterion, 500_000m, 1000m, null, null, config);

        lots.Should().BeGreaterThan(0);
        rationale.Should().Contain("KellyCriterion");
    }

    [Fact]
    public void Kelly_WinRateZero_ReturnsFallbackOneLot()
    {
        // w=0: kelly = 0.5*(0 - 1/r) < 0 → negative → returns minimum 1 lot
        var config = new PositionSizingConfig(WinRate: 0m, AvgWinLossRatio: 1.5m, FixedLots: 1);
        var (lots, rationale) = _engine.Compute(SizingModel.KellyCriterion, 100_000m, 500m, null, null, config);

        lots.Should().Be(1, "negative Kelly fraction must fall back to 1 lot minimum");
        rationale.Should().Contain("minimum 1 lot");
    }

    [Fact]
    public void Kelly_WinRateAbovePoint5_AvgWinLossOne_ProducesPositive()
    {
        // w=0.6, r=1.0 → kelly = 0.5*(0.6 - 0.4/1.0) = 0.5*0.2 = 0.10 → invest 10% of equity
        var config = new PositionSizingConfig(WinRate: 0.6m, AvgWinLossRatio: 1.0m, MaxLots: 1000, MaxCapitalPct: 50m);
        var (lots, _) = _engine.Compute(SizingModel.KellyCriterion, 200_000m, 1000m, null, null, config);

        lots.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Kelly_FractionCappedAt25Pct_WhenKellyExceedsLimit()
    {
        // Very high win rate / ratio → raw Kelly could exceed 25% cap
        var config = new PositionSizingConfig(WinRate: 0.9m, AvgWinLossRatio: 5.0m, MaxLots: 10_000, MaxCapitalPct: 100m);
        var (lots, _) = _engine.Compute(SizingModel.KellyCriterion, 1_000_000m, 1000m, null, null, config);

        // Max invest = 1_000_000 * 25% = 250_000 → max 250 lots at price 1000
        lots.Should().BeLessOrEqualTo(250, "Kelly is capped at 25% of equity per trade");
    }

    // ── MaxCapitalPct hard cap ───────────────────────────────────────────────

    [Fact]
    public void HardCap_MaxCapitalPct_LimitsPosition()
    {
        // FixedFractional: 1% risk on ₹100K equity, stop 10pts away → 100 lots raw
        // MaxCapitalPct = 5% → max investment = 5% × ₹100K = ₹5,000 → 5 lots at price ₹1,000
        var config = new PositionSizingConfig(RiskPerTradePct: 1.0m, MaxLots: 10_000, MaxCapitalPct: 5m);
        var (lots, rationale) = _engine.Compute(SizingModel.FixedFractional, 100_000m, 1000m, 990m, null, config);

        lots.Should().BeLessOrEqualTo(5, "MaxCapitalPct=5% caps position at 5 lots");
        rationale.Should().Contain("capped");
    }

    [Fact]
    public void HardCap_MaxLots_LimitsPosition()
    {
        var config = new PositionSizingConfig(RiskPerTradePct: 10m, MaxLots: 3, MaxCapitalPct: 100m);
        var (lots, _) = _engine.Compute(SizingModel.FixedFractional, 1_000_000m, 100m, 95m, null, config);

        lots.Should().BeLessOrEqualTo(3, "MaxLots=3 is an absolute ceiling");
    }

    // ── AtrBased multiplier ──────────────────────────────────────────────────

    [Fact]
    public void AtrBased_WithMultiplier_UsesAtrTimesMultiplier()
    {
        // equity=₹10L, risk=1%=₹10,000; ATR=100, mult=2 → riskPerLot=200 → 50 lots
        var config = new PositionSizingConfig(RiskPerTradePct: 1.0m, AtrMultiplier: 2.0m, MaxLots: 200, MaxCapitalPct: 100m);
        var (lots, rationale) = _engine.Compute(SizingModel.AtrBased, 1_000_000m, 5000m, null, 100m, config);

        lots.Should().Be(50);
        rationale.Should().Contain("mult=2");
    }

    [Fact]
    public void AtrBased_NullAtr_FallsBackToFixedLots()
    {
        var config = new PositionSizingConfig(FixedLots: 3);
        var (lots, rationale) = _engine.Compute(SizingModel.AtrBased, 100_000m, 1000m, null, null, config);

        lots.Should().Be(3);
        rationale.Should().Contain("fallback");
    }
}
