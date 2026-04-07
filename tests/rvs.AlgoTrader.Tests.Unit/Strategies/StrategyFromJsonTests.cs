using rvs.AlgoTrader.Strategies.FibOptionSpread;
using rvs.AlgoTrader.Strategies.IntradayPcrOptions;
using rvs.AlgoTrader.Strategies.VcpSwing;

namespace rvs.AlgoTrader.Tests.Unit.Strategies;

/// <summary>
/// Verifies B3: FromJson() throws ArgumentException for zero/invalid params.
/// Ensures that passing {} or zero-value params is caught before the strategy runs.
/// </summary>
public class StrategyFromJsonTests
{
    // ── VcpSwing ──────────────────────────────────────────────────────────────

    [Fact]
    public void VcpSwing_FromJson_ValidDefaults_Succeeds()
    {
        var config = VcpSwingConfig.FromJson("{}");
        config.Sma200Period.Should().Be(200);
        config.PivotBars.Should().Be(3);
    }

    [Theory]
    [InlineData("""{"Sma200Period":0}""",    "Sma200Period")]
    [InlineData("""{"Sma150Period":0}""",    "Sma150Period")]
    [InlineData("""{"Sma50Period":0}""",     "Sma50Period")]
    [InlineData("""{"PivotBars":0}""",       "PivotBars")]
    [InlineData("""{"MinContractions":0}""", "MinContractions")]
    [InlineData("""{"AtrPeriod":0}""",       "AtrPeriod")]
    [InlineData("""{"LookbackBars":0}""",    "LookbackBars")]
    [InlineData("""{"RiskRewardRatio":0}""", "RiskRewardRatio")]
    public void VcpSwing_FromJson_ZeroParam_Throws(string json, string paramName)
    {
        var act = () => VcpSwingConfig.FromJson(json);
        act.Should().Throw<ArgumentException>()
           .Which.ParamName.Should().Be(paramName);
    }

    // ── FibOptionSpread ───────────────────────────────────────────────────────

    [Fact]
    public void FibOptionSpread_FromJson_ValidDefaults_Succeeds()
    {
        var config = FibOptionSpreadConfig.FromJson("{}");
        config.SwingLookback.Should().Be(50);
        config.ShortLegDelta.Should().Be(0.25m);
    }

    [Theory]
    [InlineData("""{"SwingLookback":0}""",    "SwingLookback")]
    [InlineData("""{"Sma50Period":0}""",      "Sma50Period")]
    [InlineData("""{"WingWidthStrikes":0}""", "WingWidthStrikes")]
    [InlineData("""{"ShortLegDelta":0}""",    "ShortLegDelta")]
    public void FibOptionSpread_FromJson_ZeroParam_Throws(string json, string paramName)
    {
        var act = () => FibOptionSpreadConfig.FromJson(json);
        act.Should().Throw<ArgumentException>()
           .Which.ParamName.Should().Be(paramName);
    }

    // ── IntradayPcrOptions ────────────────────────────────────────────────────

    [Fact]
    public void IntradayPcr_FromJson_ValidDefaults_Succeeds()
    {
        var config = IntradayPcrOptionsConfig.FromJson("{}");
        config.PcrUpperThreshold.Should().BeGreaterThan(config.PcrLowerThreshold);
    }

    [Theory]
    [InlineData("""{"GapThresholdPts":0}""",  "GapThresholdPts")]
    [InlineData("""{"TargetDelta":0}""",       "TargetDelta")]
    [InlineData("""{"VwapTolerancePct":0}""",  "VwapTolerancePct")]
    [InlineData("""{"RiskRewardRatio":0}""",   "RiskRewardRatio")]
    public void IntradayPcr_FromJson_ZeroParam_Throws(string json, string paramName)
    {
        var act = () => IntradayPcrOptionsConfig.FromJson(json);
        act.Should().Throw<ArgumentException>()
           .Which.ParamName.Should().Be(paramName);
    }

    [Fact]
    public void IntradayPcr_FromJson_UpperBelowLower_Throws()
    {
        // PCR thresholds must be ordered: upper > lower
        var json = """{"PcrUpperThreshold":0.5,"PcrLowerThreshold":0.8}""";
        var act  = () => IntradayPcrOptionsConfig.FromJson(json);
        act.Should().Throw<ArgumentException>()
           .Which.ParamName.Should().Be("PcrUpperThreshold");
    }
}
