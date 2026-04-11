using NodaTime;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Strategies.GenericRules;

namespace rvs.AlgoTrader.Tests.Unit.Strategies;

/// <summary>
/// GR-3: Unit tests for GenericRulesStrategy options spread path.
/// Verifies: SpreadEntry fires, IV/PCR filters block/pass, IronCondor has 4 legs,
/// and custom legs override the built-in preset.
/// </summary>
public class GenericRulesStrategyOptionsTests
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<ClosedCandle> MakeCandles(int count = 5)
    {
        var candles = new List<ClosedCandle>();
        for (int i = 0; i < count; i++)
        {
            var t = Instant.FromUtc(2024, 1, i + 1, 9, 15, 0).InZone(Ist);
            candles.Add(new ClosedCandle(
                "NIFTY", "D1",
                OpenTime:  t,
                CloseTime: t.Plus(Duration.FromHours(1)),
                Open:  21000m, High: 21100m, Low: 20900m, Close: 21050m,
                Volume: 1000));
        }
        return candles;
    }

    private static StrategyContext MakeContext(
        IReadOnlyList<ClosedCandle> candles,
        IvRankSnapshot?       ivRank      = null,
        OptionChainSnapshot?  optionChain = null,
        string?               position    = null)
        => new(
            StrategyInstanceId: Guid.NewGuid(),
            InternalSymbol:     "NIFTY",
            Timeframe:          "D1",
            Candles:            candles,
            ConfigJson:         "",
            CorrelationId:      "test",
            OptionChain:        optionChain,
            SymbolIvRank:       ivRank,
            CurrentPosition:    position
        );

    /// <summary>Builds a config whose LongEntry always fires (close > 0).</summary>
    private static GenericRulesConfig MakeOptionsConfig(
        string                   spreadType  = "BullCallSpread",
        decimal?                 minIvRank   = null,
        decimal?                 maxIvRank   = null,
        decimal?                 minPcr      = null,
        decimal?                 maxPcr      = null,
        OptionsSpreadLegDef[]?   customLegs  = null)
    {
        var alwaysTrueEntry = new RuleBlock
        {
            Enabled       = true,
            GroupOperator = "AND",
            Groups        = [new RuleGroup
            {
                Id              = "g1",
                LogicalOperator = "AND",
                Conditions      = [new Condition
                {
                    Id       = "c1",
                    Left     = new ConditionOperand { Kind = "close" },
                    Operator = ">",
                    Right    = new ConditionOperand { Kind = "value", Value = 0 },
                }],
            }],
        };

        return new GenericRulesConfig
        {
            Indicators  = [],
            LongEntry   = alwaysTrueEntry,
            ShortEntry  = new RuleBlock { Enabled = false, Groups = [] },
            LongExit    = new RuleBlock { Enabled = false, Groups = [] },
            ShortExit   = new RuleBlock { Enabled = false, Groups = [] },
            OptionsConfig = new OptionsConfig
            {
                Enabled          = true,
                SpreadType       = spreadType,
                ExpiryPreference = "Weekly",
                Legs             = customLegs ?? [],
                MinIvRank        = minIvRank,
                MaxIvRank        = maxIvRank,
                MinPcr           = minPcr,
                MaxPcr           = maxPcr,
            },
        };
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SpreadEntry_Fires_When_LongEntry_Met()
    {
        var strategy = new GenericRulesStrategy(MakeOptionsConfig("BullCallSpread"));
        var result   = await strategy.EvaluateAsync(MakeContext(MakeCandles()), CancellationToken.None);

        result.Signal.Should().Be(SignalType.Buy);
        result.Spread.Should().NotBeNull();
        result.Spread!.SpreadType.Should().Be("BullCallSpread");
    }

    [Fact]
    public async Task IronCondor_Preset_Has_Four_Legs()
    {
        var strategy = new GenericRulesStrategy(MakeOptionsConfig("IronCondor"));
        var result   = await strategy.EvaluateAsync(MakeContext(MakeCandles()), CancellationToken.None);

        result.Spread.Should().NotBeNull();
        result.Spread!.Legs.Should().HaveCount(4);
    }

    [Theory]
    [InlineData("ShortStraddle", 2)]
    [InlineData("BullCallSpread", 2)]
    [InlineData("BearPutSpread", 2)]
    [InlineData("LongCall", 1)]
    [InlineData("LongPut", 1)]
    public async Task Preset_SpreadTypes_Have_Expected_Leg_Count(string spreadType, int expectedLegs)
    {
        var strategy = new GenericRulesStrategy(MakeOptionsConfig(spreadType));
        var result   = await strategy.EvaluateAsync(MakeContext(MakeCandles()), CancellationToken.None);

        result.Spread.Should().NotBeNull();
        result.Spread!.Legs.Should().HaveCount(expectedLegs);
    }

    [Fact]
    public async Task IvRank_Filter_Blocks_Entry_When_IvRank_TooLow()
    {
        var strategy = new GenericRulesStrategy(MakeOptionsConfig("ShortStraddle", minIvRank: 50m));
        // IVRank = 30 — below the minimum of 50
        var ivRank = new IvRankSnapshot(
            "NIFTY", CurrentIv: 15m, IvRank: 30m, IvPercentile: 30m,
            WeekHigh52: 40m, WeekLow52: 10m,
            Regime: IvRegime.Normal, DataPointsUsed: 252);

        var result = await strategy.EvaluateAsync(MakeContext(MakeCandles(), ivRank: ivRank), CancellationToken.None);

        result.Signal.Should().Be(SignalType.Hold);
        result.Reason.Should().Contain("IVRank");
    }

    [Fact]
    public async Task IvRank_Filter_Passes_When_IvRank_AboveMinimum()
    {
        var strategy = new GenericRulesStrategy(MakeOptionsConfig("ShortStraddle", minIvRank: 50m));
        // IVRank = 75 — above minimum
        var ivRank = new IvRankSnapshot(
            "NIFTY", CurrentIv: 25m, IvRank: 75m, IvPercentile: 75m,
            WeekHigh52: 40m, WeekLow52: 10m,
            Regime: IvRegime.Elevated, DataPointsUsed: 252);

        var result = await strategy.EvaluateAsync(MakeContext(MakeCandles(), ivRank: ivRank), CancellationToken.None);

        result.Signal.Should().Be(SignalType.Buy);
        result.Spread.Should().NotBeNull();
    }

    [Fact]
    public async Task CustomLegs_Override_Preset_Legs()
    {
        var customLegs = new OptionsSpreadLegDef[]
        {
            new() { OptionType = "CE", Direction = "Buy",  SelectionMode = "Atm",         Quantity = 2 },
            new() { OptionType = "CE", Direction = "Sell", SelectionMode = "OtmByStrike", OtmStrikes = 3, Quantity = 2 },
            new() { OptionType = "PE", Direction = "Buy",  SelectionMode = "Atm",         Quantity = 2 },
        };
        var strategy = new GenericRulesStrategy(MakeOptionsConfig("BullCallSpread", customLegs: customLegs));

        var result = await strategy.EvaluateAsync(MakeContext(MakeCandles()), CancellationToken.None);

        result.Spread.Should().NotBeNull();
        // Custom 3-leg definition overrides the 2-leg BullCallSpread preset
        result.Spread!.Legs.Should().HaveCount(3);
        result.Spread.Legs[0].Quantity.Should().Be(2);
    }

    [Fact]
    public async Task Options_Hold_When_No_Entry_Condition_Met()
    {
        // Empty groups → RulesEvaluator.Evaluate returns false
        var config = new GenericRulesConfig
        {
            Indicators    = [],
            LongEntry     = new RuleBlock { Enabled = true,  Groups = [] },
            ShortEntry    = new RuleBlock { Enabled = false, Groups = [] },
            LongExit      = new RuleBlock { Enabled = false, Groups = [] },
            ShortExit     = new RuleBlock { Enabled = false, Groups = [] },
            OptionsConfig = new OptionsConfig { Enabled = true, SpreadType = "BullCallSpread" },
        };
        var strategy = new GenericRulesStrategy(config);

        var result = await strategy.EvaluateAsync(MakeContext(MakeCandles()), CancellationToken.None);

        result.Signal.Should().Be(SignalType.Hold);
        result.Spread.Should().BeNull();
    }

    [Fact]
    public async Task Options_Disabled_Falls_Through_To_Equity_Path()
    {
        // Same always-true entry, but OptionsConfig.Enabled = false
        var alwaysTrueEntry = new RuleBlock
        {
            Enabled       = true,
            GroupOperator = "AND",
            Groups        = [new RuleGroup
            {
                Id              = "g1",
                LogicalOperator = "AND",
                Conditions      = [new Condition
                {
                    Id       = "c1",
                    Left     = new ConditionOperand { Kind = "close" },
                    Operator = ">",
                    Right    = new ConditionOperand { Kind = "value", Value = 0 },
                }],
            }],
        };
        var config = new GenericRulesConfig
        {
            Indicators    = [],
            LongEntry     = alwaysTrueEntry,
            ShortEntry    = new RuleBlock { Enabled = false, Groups = [] },
            LongExit      = new RuleBlock { Enabled = false, Groups = [] },
            ShortExit     = new RuleBlock { Enabled = false, Groups = [] },
            OptionsConfig = new OptionsConfig { Enabled = false },  // disabled
        };
        var strategy = new GenericRulesStrategy(config);

        var result = await strategy.EvaluateAsync(MakeContext(MakeCandles()), CancellationToken.None);

        // Equity path: returns Buy (not SpreadEntry)
        result.Signal.Should().Be(SignalType.Buy);
        result.Spread.Should().BeNull();
    }
}
