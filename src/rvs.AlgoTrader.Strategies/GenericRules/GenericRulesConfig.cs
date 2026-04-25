using System.Text.Json;
using System.Text.Json.Serialization;

namespace rvs.AlgoTrader.Strategies.GenericRules;

// ─────────────────────────────────────────────────────────────────────────────
// GenericRulesConfig — mirrors the frontend Strategy TypeScript type.
// Stored as strategy_definitions.definition_json (JSONB).
// When running a backtest: passed as ParametersJson to GenericRulesStrategy.
// ─────────────────────────────────────────────────────────────────────────────

public class GenericRulesConfig
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("primaryTimeframe")]
    public string? PrimaryTimeframe { get; set; }

    [JsonPropertyName("tradingStyle")]
    public string? TradingStyle { get; set; }

    [JsonPropertyName("instruments")]
    public string[]? Instruments { get; set; }

    // ── Indicators ────────────────────────────────────────────────────────────
    [JsonPropertyName("indicators")]
    public IndicatorDef[] Indicators { get; set; } = [];

    // ── Rule blocks ───────────────────────────────────────────────────────────
    [JsonPropertyName("longEntry")]
    public RuleBlock LongEntry { get; set; } = new();

    [JsonPropertyName("shortEntry")]
    public RuleBlock ShortEntry { get; set; } = new();

    [JsonPropertyName("longExit")]
    public RuleBlock LongExit { get; set; } = new();

    [JsonPropertyName("shortExit")]
    public RuleBlock ShortExit { get; set; } = new();

    // ── Execution model ───────────────────────────────────────────────────────
    [JsonPropertyName("entryExecution")]
    public EntryExecutionConfig? EntryExecution { get; set; }

    // ── Risk defaults ─────────────────────────────────────────────────────────
    // Derived from stopLoss/profitTarget in UI, or sensible defaults.
    // stopLoss: { type: "ATRMultiple", value: 3.0 }
    [JsonPropertyName("stopLoss")]
    public StopTargetConfig? StopLoss { get; set; }

    [JsonPropertyName("profitTarget")]
    public StopTargetConfig? ProfitTarget { get; set; }

    // ── Options spread config ─────────────────────────────────────────────────
    [JsonPropertyName("optionsConfig")]
    public OptionsConfig? OptionsConfig { get; set; }

    // ── Convenience helpers ───────────────────────────────────────────────────
    public bool AllowShort => ShortEntry.Enabled;

    /// <summary>ATR stop multiplier — from stopLoss config or default 10.0 (wide disaster stop).
    /// Signal-exit strategies use EMA/ST crossovers as primary exits; 10× ATR ensures the
    /// disaster stop doesn't interfere with normal 5m–1h fluctuations.
    /// Users who want a tighter stop should configure stopLoss.type = "ATRMultiple" explicitly.</summary>
    public decimal AtrStopMult =>
        StopLoss?.Type == "ATRMultiple" && StopLoss.Value > 0 ? StopLoss.Value : 10.0m;

    /// <summary>Risk:reward ratio — from profitTarget config or 99 (no fixed TP, exit by signal).</summary>
    public decimal RiskRewardRatio =>
        ProfitTarget?.Value > 0 ? ProfitTarget.Value : 99m;

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static GenericRulesConfig FromJson(string json)
        => JsonSerializer.Deserialize<GenericRulesConfig>(json, JsonOpts)
           ?? new GenericRulesConfig();

    public string ToJson()
        => JsonSerializer.Serialize(this, JsonOpts);
}

// ─────────────────────────────────────────────────────────────────────────────
// Indicator definition — mirrors frontend IndicatorConfig
// ─────────────────────────────────────────────────────────────────────────────

public class IndicatorDef
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>
    /// "SMA" | "EMA" | "HullMA" | "RSI" | "ATR" | "BollingerBands" | "MACD" |
    /// "VWAP" | "Volume" | "CCI" | "ADX" | "SuperTrend" | "Stochastics" |
    /// "SessionFilter" | "DayOfWeekFilter" | etc.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("timeframe")]
    public string? Timeframe { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("signalLayer")]
    public string? SignalLayer { get; set; }

    [JsonPropertyName("layerOrder")]
    public int LayerOrder { get; set; }

    /// <summary>
    /// Free-form parameters from UI.
    /// Examples: {"period":20} {"fastPeriod":12,"slowPeriod":26,"signalPeriod":9}
    ///           {"startTime":"09:15","endTime":"15:14"} {"days":["Mon","Tue","Wed","Thu","Fri"]}
    /// </summary>
    [JsonPropertyName("baseParams")]
    public Dictionary<string, JsonElement> BaseParams { get; set; } = new();

    /// <summary>Safely read an int param. Returns defaultValue if missing or wrong type.</summary>
    public int GetInt(string key, int defaultValue = 0)
    {
        if (!BaseParams.TryGetValue(key, out var el)) return defaultValue;
        return el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v) ? v : defaultValue;
    }

    /// <summary>Safely read a decimal param.</summary>
    public decimal GetDecimal(string key, decimal defaultValue = 0m)
    {
        if (!BaseParams.TryGetValue(key, out var el)) return defaultValue;
        return el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var v) ? v : defaultValue;
    }

    /// <summary>Safely read a string param.</summary>
    public string GetString(string key, string defaultValue = "")
    {
        if (!BaseParams.TryGetValue(key, out var el)) return defaultValue;
        return el.ValueKind == JsonValueKind.String ? el.GetString() ?? defaultValue : defaultValue;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Rule blocks — mirrors frontend EntryExitBlock / RuleGroup / Condition
// ─────────────────────────────────────────────────────────────────────────────

public class RuleBlock
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>"AND" | "OR" — how rule groups combine.</summary>
    [JsonPropertyName("groupOperator")]
    public string GroupOperator { get; set; } = "AND";

    [JsonPropertyName("groups")]
    public RuleGroup[] Groups { get; set; } = [];
}

public class RuleGroup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>"AND" | "OR" — how conditions within this group combine.</summary>
    [JsonPropertyName("logicalOperator")]
    public string LogicalOperator { get; set; } = "AND";

    [JsonPropertyName("conditions")]
    public Condition[] Conditions { get; set; } = [];
}

public class Condition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("left")]
    public ConditionOperand Left { get; set; } = new();

    /// <summary>
    /// "&gt;" | "&lt;" | "&gt;=" | "&lt;=" | "==" | "!=" | "crossesAbove" | "crossesBelow"
    /// Note: JSON stores these as the literal characters &gt;, &lt;, etc.
    /// </summary>
    [JsonPropertyName("operator")]
    public string Operator { get; set; } = ">";

    [JsonPropertyName("right")]
    public ConditionOperand? Right { get; set; }
}

public class ConditionOperand
{
    /// <summary>
    /// "indicator" — reference to a computed indicator (by id)
    /// "value"     — static number
    /// "window"    — aggregated over N bars
    /// "percentile" — percentile rank of indicator
    /// "slope"     — rising/falling indicator
    /// "sessionState" — boolean session property
    /// "close"     — current bar's close price (shorthand)
    /// "open" | "high" | "low" | "volume" — current bar OHLCV
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "value";

    // kind == "indicator" | "percentile" | "slope"
    [JsonPropertyName("indicatorId")]
    public string? IndicatorId { get; set; }

    /// <summary>
    /// Sub-field for multi-value indicators:
    /// BollingerBands: "upper" | "middle" | "lower"
    /// MACD: "macd" | "signal" | "histogram"
    /// Stochastics: "k" | "d"
    /// Default (omitted): primary value.
    /// </summary>
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    // kind == "value"
    [JsonPropertyName("value")]
    public decimal? Value { get; set; }

    // kind == "window"
    [JsonPropertyName("expr")]
    public WindowExpression? Expr { get; set; }

    // kind == "percentile" | "slope"
    [JsonPropertyName("lookbackBars")]
    public int? LookbackBars { get; set; }

    // kind == "percentile"
    [JsonPropertyName("pct")]
    public decimal? Pct { get; set; }

    // kind == "sessionState"
    [JsonPropertyName("property")]
    public string? Property { get; set; }
}

public class WindowExpression
{
    [JsonPropertyName("sourceIndicatorId")]
    public string SourceIndicatorId { get; set; } = "";

    [JsonPropertyName("lookbackBars")]
    public int LookbackBars { get; set; } = 14;

    /// <summary>"avg" | "max" | "min" | "highest" | "lowest"</summary>
    [JsonPropertyName("aggregationType")]
    public string AggregationType { get; set; } = "avg";

    [JsonPropertyName("multiplier")]
    public decimal? Multiplier { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Supporting config types
// ─────────────────────────────────────────────────────────────────────────────

public class EntryExecutionConfig
{
    /// <summary>"NextBarOpen" | "SameBarClose" | "LimitOnNextBar"</summary>
    [JsonPropertyName("orderType")]
    public string OrderType { get; set; } = "NextBarOpen";

    [JsonPropertyName("noTradeAfterHour")]
    public int? NoTradeAfterHour { get; set; }

    [JsonPropertyName("noTradeAfterMinute")]
    public int? NoTradeAfterMinute { get; set; }
}

public class StopTargetConfig
{
    /// <summary>"ATRMultiple" | "FixedPoints" | "StructureLowHigh" | "RRDerived"</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    // Frontend serializes this as "baseValue" (StopTargetConfig.baseValue in TypeScript).
    // The old "value" field is kept as a fallback alias for backward compatibility.
    [JsonPropertyName("baseValue")]
    public decimal BaseValue { get; set; }

    [JsonPropertyName("value")]
    public decimal ValueAlias { get; set; }

    [JsonIgnore]
    public decimal Value => BaseValue > 0 ? BaseValue : ValueAlias;
}

// ─────────────────────────────────────────────────────────────────────────────
// Options spread configuration
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// When Enabled = true, GenericRulesStrategy routes entry signals through the
/// options spread engine instead of placing a plain equity Buy/Sell.
/// The indicator/rule sections of the strategy still define WHEN to enter;
/// this section defines WHAT spread structure to build.
/// </summary>
public class OptionsConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>
    /// "BullCallSpread" | "BearPutSpread" | "BullPutSpread" | "BearCallSpread" |
    /// "IronCondor" | "ShortStraddle" | "ShortStrangle" | "CalendarSpread" |
    /// "LongCall" | "LongPut"
    /// </summary>
    [JsonPropertyName("spreadType")]
    public string SpreadType { get; set; } = "BullCallSpread";

    /// <summary>
    /// Custom leg definitions. When non-empty, overrides built-in preset defaults.
    /// Each element maps to one SpreadLeg passed to SpreadOrderManager.
    /// </summary>
    [JsonPropertyName("legs")]
    public OptionsSpreadLegDef[] Legs { get; set; } = [];

    /// <summary>"Weekly" | "Monthly"</summary>
    [JsonPropertyName("expiryPreference")]
    public string ExpiryPreference { get; set; } = "Weekly";

    /// <summary>% of net premium received/paid at which to close the spread for a loss.</summary>
    [JsonPropertyName("stopLossPct")]
    public decimal StopLossPct { get; set; } = 50m;

    /// <summary>% of net premium at which to take profit.</summary>
    [JsonPropertyName("profitTargetPct")]
    public decimal ProfitTargetPct { get; set; } = 50m;

    // ── IV / PCR filters ──────────────────────────────────────────────────────
    // Checked from StrategyContext before an entry signal is fired.
    // Add IVRank / PCR / AtmIV indicators to the indicator list to expose them
    // in rule conditions; these numeric filters provide a fast pre-check shortcut.

    [JsonPropertyName("minIvRank")]
    public decimal? MinIvRank { get; set; }

    [JsonPropertyName("maxIvRank")]
    public decimal? MaxIvRank { get; set; }

    [JsonPropertyName("minAtmIv")]
    public decimal? MinAtmIv { get; set; }

    [JsonPropertyName("maxAtmIv")]
    public decimal? MaxAtmIv { get; set; }

    [JsonPropertyName("minPcr")]
    public decimal? MinPcr { get; set; }

    [JsonPropertyName("maxPcr")]
    public decimal? MaxPcr { get; set; }
}

/// <summary>
/// One leg in a user-defined options spread. Serialised inside OptionsConfig.Legs.
/// GenericRulesStrategy converts these into domain SpreadLeg records.
/// </summary>
public class OptionsSpreadLegDef
{
    /// <summary>"CE" | "PE"</summary>
    [JsonPropertyName("optionType")]
    public string OptionType { get; set; } = "CE";

    /// <summary>"Buy" | "Sell"</summary>
    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "Buy";

    /// <summary>"Atm" | "OtmByStrike" | "OtmByPct" | "ByDelta"</summary>
    [JsonPropertyName("selectionMode")]
    public string SelectionMode { get; set; } = "Atm";

    /// <summary>Number of strike intervals OTM (used when SelectionMode = "OtmByStrike").</summary>
    [JsonPropertyName("otmStrikes")]
    public int OtmStrikes { get; set; }

    /// <summary>Percent OTM as a fraction, e.g. 0.02 = 2% (used when SelectionMode = "OtmByPct").</summary>
    [JsonPropertyName("otmPct")]
    public decimal OtmPct { get; set; }

    /// <summary>Target delta, e.g. 0.30 for 30-delta (used when SelectionMode = "ByDelta").</summary>
    [JsonPropertyName("targetDelta")]
    public decimal TargetDelta { get; set; } = 0.30m;

    /// <summary>Number of lots.</summary>
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; } = 1;

    [JsonPropertyName("nearestWeekly")]
    public bool NearestWeekly { get; set; } = true;
}
