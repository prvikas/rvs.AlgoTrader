using System.Text.Json;
using System.Text.Json.Serialization;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;

// ─────────────────────────────────────────────────────────────────────────────
// ShortPremiumVelocityConfig — canonical scenario-overridable config for STRAT-004.
//
// Pattern: static FromJson(string) + static GetSchema() → IReadOnlyList<StrategyParamDef>
// Every value below is a compiled default only; any property can be overridden via
// StrategyScenario.ParametersJsonOverride (merged by ScenarioParamMerger at runtime).
//
// NOTE: Placed in Application (not Strategies) because Application-layer service
//       interfaces (IVelocityServices) reference this type — putting it in Strategies
//       would create an Application→Strategies circular dependency.
// ─────────────────────────────────────────────────────────────────────────────

public record StressOverlayConfig(
    int     DurationSessions,
    decimal VixStart,
    decimal VixPeak,
    decimal VixEnd,
    decimal SpotDrawdownPct,
    int     RecoverySessions
);

public class ShortPremiumVelocityConfig
{
    // ── JSON serialiser options ───────────────────────────────────────────────
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Per-regime dictionaries (keyed on the VelocityLowVolCompression…VelocityPanic values)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Maximum tail-risk score above which entry is blocked, per regime.</summary>
    public Dictionary<MarketRegime, decimal> TailRiskScoreCeiling { get; init; } = new()
    {
        [MarketRegime.VelocityLowVolCompression]      = 40m,
        [MarketRegime.VelocityChoppyMeanReversion]    = 50m,
        [MarketRegime.VelocityPostPanicNormalization] = 55m,
        [MarketRegime.VelocityHighVolExpansion]        = 65m,
        [MarketRegime.VelocityPanic]                   = 0m,  // no entry in panic
    };

    /// <summary>Max gamma-per-theta ratio; positions above this are not opened, per regime.</summary>
    public Dictionary<MarketRegime, decimal> GammaPerThetaCeiling { get; init; } = new()
    {
        [MarketRegime.VelocityLowVolCompression]      = 1.5m,
        [MarketRegime.VelocityChoppyMeanReversion]    = 1.3m,
        [MarketRegime.VelocityPostPanicNormalization] = 1.2m,
        [MarketRegime.VelocityHighVolExpansion]        = 1.0m,
        [MarketRegime.VelocityPanic]                   = 0m,
    };

    /// <summary>Minimum liquidity score (bid-ask / ATM premium) below which entry is blocked.</summary>
    public Dictionary<MarketRegime, decimal> LiquiditySurvivalFloor { get; init; } = new()
    {
        [MarketRegime.VelocityLowVolCompression]      = 0.05m,
        [MarketRegime.VelocityChoppyMeanReversion]    = 0.06m,
        [MarketRegime.VelocityPostPanicNormalization] = 0.07m,
        [MarketRegime.VelocityHighVolExpansion]        = 0.08m,
        [MarketRegime.VelocityPanic]                   = 1.00m, // effectively blocks entry
    };

    /// <summary>Minimum required GammaHedged/GrossGamma ratio before a new short-premium leg is opened.</summary>
    public Dictionary<MarketRegime, decimal> MinHedgeCoverageRatio { get; init; } = new()
    {
        [MarketRegime.VelocityLowVolCompression]      = 0.20m,
        [MarketRegime.VelocityChoppyMeanReversion]    = 0.25m,
        [MarketRegime.VelocityPostPanicNormalization] = 0.30m,
        [MarketRegime.VelocityHighVolExpansion]        = 0.40m,
        [MarketRegime.VelocityPanic]                   = 0.60m,
    };

    /// <summary>Minimum sessions required in recovery mode before stepping up size.</summary>
    public Dictionary<MarketRegime, int> RecoveryWindowMinSessions { get; init; } = new()
    {
        [MarketRegime.VelocityLowVolCompression]      = 40,
        [MarketRegime.VelocityChoppyMeanReversion]    = 30,
        [MarketRegime.VelocityPostPanicNormalization] = 20,
        [MarketRegime.VelocityHighVolExpansion]        = 10,
        [MarketRegime.VelocityPanic]                   = 0,
    };

    /// <summary>Maximum sessions in a recovery window before forcing a position review.</summary>
    public Dictionary<MarketRegime, int> RecoveryWindowMaxSessions { get; init; } = new()
    {
        [MarketRegime.VelocityLowVolCompression]      = 60,
        [MarketRegime.VelocityChoppyMeanReversion]    = 40,
        [MarketRegime.VelocityPostPanicNormalization] = 30,
        [MarketRegime.VelocityHighVolExpansion]        = 15,
        [MarketRegime.VelocityPanic]                   = 0,
    };

    /// <summary>Fraction of max premium credit at which the position is closed for a profit.</summary>
    public Dictionary<MarketRegime, decimal> ProfitTargetFraction { get; init; } = new()
    {
        [MarketRegime.VelocityLowVolCompression]      = 0.65m,
        [MarketRegime.VelocityChoppyMeanReversion]    = 0.60m,
        [MarketRegime.VelocityPostPanicNormalization] = 0.55m,
        [MarketRegime.VelocityHighVolExpansion]        = 0.45m,
        [MarketRegime.VelocityPanic]                   = 0.00m,
    };

    /// <summary>Lower bound of the recovery size multiplier for each regime.</summary>
    public Dictionary<MarketRegime, decimal> RecoveryMultiplierMin { get; init; } = new()
    {
        [MarketRegime.VelocityLowVolCompression]      = 1.00m,
        [MarketRegime.VelocityChoppyMeanReversion]    = 0.90m,
        [MarketRegime.VelocityPostPanicNormalization] = 0.80m,
        [MarketRegime.VelocityHighVolExpansion]        = 0.60m,
        [MarketRegime.VelocityPanic]                   = 0.50m,
    };

    /// <summary>Upper bound of the recovery size multiplier for each regime.</summary>
    public Dictionary<MarketRegime, decimal> RecoveryMultiplierMax { get; init; } = new()
    {
        [MarketRegime.VelocityLowVolCompression]      = 1.25m,
        [MarketRegime.VelocityChoppyMeanReversion]    = 1.10m,
        [MarketRegime.VelocityPostPanicNormalization] = 1.00m,
        [MarketRegime.VelocityHighVolExpansion]        = 0.80m,
        [MarketRegime.VelocityPanic]                   = 0.50m,
    };

    /// <summary>
    /// 6 DTE-bucket weights per regime (must sum to 1.0; each ≤ 0.35).
    /// Buckets (index 0–5): 0–3d, 4–7d, 8–14d, 15–21d, 22–35d, 36–45d.
    /// </summary>
    public Dictionary<MarketRegime, decimal[]> DtePreferenceWeights { get; init; } = new()
    {
        [MarketRegime.VelocityLowVolCompression]      = [0.05m, 0.10m, 0.25m, 0.30m, 0.20m, 0.10m],
        [MarketRegime.VelocityChoppyMeanReversion]    = [0.05m, 0.15m, 0.30m, 0.25m, 0.20m, 0.05m],
        [MarketRegime.VelocityPostPanicNormalization] = [0.00m, 0.10m, 0.25m, 0.35m, 0.25m, 0.05m],
        [MarketRegime.VelocityHighVolExpansion]        = [0.00m, 0.05m, 0.20m, 0.35m, 0.30m, 0.10m],
        [MarketRegime.VelocityPanic]                   = [0.00m, 0.00m, 0.00m, 0.00m, 0.00m, 0.00m],
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Structure selection
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>VelocityScore below this value blocks any new entry.</summary>
    public decimal MinVelocityScoreForEntry { get; init; } = 55.0m;

    /// <summary>OpportunityDensity above this value unlocks the maximum aggression multiplier.</summary>
    public decimal OdThresholdForMaxAggression { get; init; } = 85.0m;

    // ─────────────────────────────────────────────────────────────────────────
    // Velocity / OD score weights (arrays of 5, must sum to 1.0)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Weights for the 5 Velocity Score sub-components:
    /// [IV mean-reversion, IV skew, term structure, realised-vol spread, breadth].
    /// </summary>
    public decimal[] VelocityScoreWeights { get; init; } = [0.30m, 0.20m, 0.20m, 0.20m, 0.10m];

    /// <summary>
    /// Weights for the 5 Opportunity Density sub-components:
    /// [PCR alignment, OI concentration, skew steepness, term-structure carry, event calendar].
    /// </summary>
    public decimal[] OpportunityDensityWeights { get; init; } = [0.25m, 0.25m, 0.20m, 0.20m, 0.10m];

    // ─────────────────────────────────────────────────────────────────────────
    // Aggression
    // ─────────────────────────────────────────────────────────────────────────

    public decimal AggressionMultiplierMin { get; init; } = 0.80m;
    public decimal AggressionMultiplierMax { get; init; } = 1.50m;

    /// <summary>Hedge coverage ratio required before the maximum aggression multiplier is allowed.</summary>
    public decimal MinHedgeCoverageForMaxAggression { get; init; } = 0.40m;

    // ─────────────────────────────────────────────────────────────────────────
    // Hedge parameters
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Hedge-leg notional as a fraction of the matched short-leg notional (1.0 = dollar-matched).</summary>
    public decimal HedgeSizeAsPctOfShortLeg { get; init; } = 1.0m;

    /// <summary>Maximum allowable hedge cost as a fraction of daily theta collected.</summary>
    public decimal MaxHedgeCostPctOfDailyTheta { get; init; } = 0.30m;

    /// <summary>Net delta (absolute) above which a delta-hedge leg is placed.</summary>
    public decimal DeltaHedgeTriggerThreshold { get; init; } = 50.0m;

    /// <summary>Notional fraction allocated to deep-OTM tail-risk put protection.</summary>
    public decimal TailRiskHedgePctOfNotional { get; init; } = 0.005m;

    /// <summary>Minimum DTE for the tail-risk hedge leg at entry.</summary>
    public int TailRiskHedgeMinDte { get; init; } = 7;

    /// <summary>Roll the tail-risk hedge when DTE falls to or below this threshold.</summary>
    public int TailRiskHedgeRollDte { get; init; } = 7;

    /// <summary>Per-position gamma-per-theta ratio that triggers a conditional hedge.</summary>
    public decimal ConditionalHedgeGammaPerThetaTrigger { get; init; } = 1.2m;

    /// <summary>DTE threshold that triggers a conditional hedge (gamma-pin proximity).</summary>
    public int ConditionalHedgeDteTrigger { get; init; } = 3;

    /// <summary>Number of strikes between the short leg and the hedge wing.</summary>
    public int HedgeWingWidth { get; init; } = 2;

    /// <summary>Vega hedge notional as a fraction of total book vega.</summary>
    public decimal VegaHedgeSizeAsPctOfBookVega { get; init; } = 0.20m;

    // ─────────────────────────────────────────────────────────────────────────
    // Circuit breaker
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Daily loss fraction that triggers the soft stop (reduce new-position size 50%).</summary>
    public decimal SoftStopLossPct { get; init; } = 0.015m;

    /// <summary>Daily loss fraction that triggers the hard stop (no new positions this session).</summary>
    public decimal HardStopLossPct { get; init; } = 0.025m;

    // ─────────────────────────────────────────────────────────────────────────
    // Margin
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Net shocked margin utilisation hard cap (positions are trimmed above this level).</summary>
    public decimal ShockedUtilizationHardCap { get; init; } = 0.95m;

    /// <summary>Target utilisation after TrimToFit is applied.</summary>
    public decimal TrimToFitTargetPercent { get; init; } = 0.87m;

    /// <summary>Maximum age (minutes) of a margin snapshot before it is considered stale.</summary>
    public int MarginFreshnessMaxAgeMinutes { get; init; } = 20;

    /// <summary>How often the margin refresh job runs (minutes).</summary>
    public int MarginRefreshIntervalMinutes { get; init; } = 60;

    // ─────────────────────────────────────────────────────────────────────────
    // Jump risk
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Rolling window size (minutes) for computing the vol-ratio baseline.</summary>
    public int JumpRiskRollingWindowMinutes { get; init; } = 30;

    /// <summary>Vol-ratio spike threshold: mean + N × std-dev triggers a soft stop.</summary>
    public decimal JumpRiskSpikeStdDevMultiplier { get; init; } = 2.0m;

    /// <summary>Vol-ratio must drop below mean + N × std-dev for the soft stop to clear.</summary>
    public decimal JumpRiskClearStdDevMultiplier { get; init; } = 1.0m;

    /// <summary>Underlying symbols monitored for jump-risk (real-time vol-ratio).</summary>
    public string[] UnderlyingSymbols { get; init; } = ["NIFTY", "BANKNIFTY"];

    // ─────────────────────────────────────────────────────────────────────────
    // Gamma-pin exit
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Minutes before market close at which gamma-pin positions are exited.</summary>
    public int GammaPinExitMinutesBeforeClose { get; init; } = 60;

    /// <summary>Minutes before close at which a gamma-pin soft-kill warning is emitted.</summary>
    public int GammaPinSoftKillMinutesBeforeClose { get; init; } = 120;

    /// <summary>
    /// Gamma/theta ratio above which a position is flagged as gamma-pin risk.
    /// Default 0.667 = 2/3 (position pays 0.667 units of gamma per unit of theta).
    /// </summary>
    public decimal GammaPinSoftKillGammaRatio { get; init; } = 0.667m;

    // ─────────────────────────────────────────────────────────────────────────
    // Stop-loss
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Position stop-loss = EntryPremium × StopLossMultiplier (i.e. 2× premium collected).</summary>
    public decimal StopLossMultiplier { get; init; } = 2.0m;

    /// <summary>Minimum Sharpe ratio required at recovery step 3 before full size is reinstated.</summary>
    public decimal RecoveryStep3SharpeMin { get; init; } = 0.8m;

    // ─────────────────────────────────────────────────────────────────────────
    // Reinvestment
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Maximum fraction of account NAV allocated to the SPV layer.</summary>
    public decimal MaxVelocityLayerPctOfAccount { get; init; } = 0.875m;

    /// <summary>Friction buffer kept in cash as a % of NAV to cover fees and slippage.</summary>
    public decimal FrictionBufferPercent { get; init; } = 0.12m;

    /// <summary>Friction leakage alert threshold: log a warning when fees+slippage/NAV exceeds this %.</summary>
    public decimal FrictionLeakageAlertPercent { get; init; } = 0.0175m;

    // ─────────────────────────────────────────────────────────────────────────
    // Synthetic pricer
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Minimum ATM IV (%) used when chain data is unavailable — floor for BSM re-pricing.</summary>
    public decimal AtmIvGuardMinPct { get; init; } = 10.0m;

    public decimal RiskFreeRate { get; init; } = 0.065m;

    /// <summary>Volatility skew slope per strike away from ATM (negative for equity smile).</summary>
    public decimal SkewSlopePerStrike { get; init; } = -0.002m;

    /// <summary>Base slippage fraction applied to all simulated fills.</summary>
    public decimal SlippageFraction { get; init; } = 0.002m;

    /// <summary>Multiplier applied to SlippageFraction in the afternoon session (wider spreads).</summary>
    public decimal AfternoonSlippageMultiplier { get; init; } = 1.25m;

    /// <summary>Hedge-leg slippage is lower (better liquidity) — multiplied by this factor.</summary>
    public decimal HedgeLegSlippageMultiplier { get; init; } = 0.50m;

    public int AfternoonSlippageStartHour   { get; init; } = 14;
    public int AfternoonSlippageStartMinute { get; init; } = 30;

    // ─────────────────────────────────────────────────────────────────────────
    // Stress overlays (named scenarios applied in order when VIX matches)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Named stress scenarios for backtesting. Key = scenario label (e.g. "Covid2020").</summary>
    public Dictionary<string, StressOverlayConfig> StressOverlays { get; init; } = new()
    {
        ["BaseStress"] = new(DurationSessions: 5,  VixStart: 15m, VixPeak: 28m, VixEnd: 20m, SpotDrawdownPct: 0.05m, RecoverySessions: 10),
        ["SevereStress"] = new(DurationSessions: 15, VixStart: 18m, VixPeak: 40m, VixEnd: 22m, SpotDrawdownPct: 0.12m, RecoverySessions: 25),
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Cluster / concentration limits
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Maximum fraction of portfolio notional in any single underlying (cluster hard cap).</summary>
    public decimal ClusterHardCap { get; init; } = 0.30m;

    /// <summary>
    /// Multiplier applied to ClusterHardCap during results season.
    /// 0.50 × 0.30 = 0.15 effective cap — tighter concentration limits during earnings.
    /// </summary>
    public decimal ResultsSeasonClusterCapMultiplier { get; init; } = 0.50m;

    // ─────────────────────────────────────────────────────────────────────────
    // Factory + schema
    // ─────────────────────────────────────────────────────────────────────────

    public static ShortPremiumVelocityConfig FromJson(string json)
        => JsonSerializer.Deserialize<ShortPremiumVelocityConfig>(json, JsonOpts)
           ?? new ShortPremiumVelocityConfig();

    public static IReadOnlyList<StrategyParamDef> GetSchema() =>
    [
        // ── Structure selection ────────────────────────────────────────────
        new("MinVelocityScoreForEntry",       "Min Velocity Score for Entry",           "decimal", 55.0m,  Min: 30m,   Max: 80m,  Step: 1m,    Section: "Structure"),
        new("OdThresholdForMaxAggression",    "OD Threshold for Max Aggression",        "decimal", 85.0m,  Min: 60m,   Max: 100m, Step: 1m,    Section: "Structure"),

        // ── Aggression ─────────────────────────────────────────────────────
        new("AggressionMultiplierMin",        "Aggression Multiplier Min",              "decimal", 0.80m,  Min: 0.50m, Max: 1.0m, Step: 0.05m, Section: "Aggression"),
        new("AggressionMultiplierMax",        "Aggression Multiplier Max",              "decimal", 1.50m,  Min: 1.0m,  Max: 2.0m, Step: 0.05m, Section: "Aggression"),
        new("MinHedgeCoverageForMaxAggression","Min Hedge Coverage for Max Aggression", "decimal", 0.40m,  Min: 0.10m, Max: 1.0m, Step: 0.05m, Section: "Aggression"),

        // ── Hedge ──────────────────────────────────────────────────────────
        new("HedgeSizeAsPctOfShortLeg",        "Hedge Size as % of Short Leg",          "decimal", 1.0m,   Min: 0.25m, Max: 2.0m, Step: 0.25m, Section: "Hedge"),
        new("MaxHedgeCostPctOfDailyTheta",     "Max Hedge Cost % of Daily Theta",       "decimal", 0.30m,  Min: 0.05m, Max: 0.80m, Step: 0.05m, Section: "Hedge"),
        new("DeltaHedgeTriggerThreshold",      "Delta Hedge Trigger (delta units)",     "decimal", 50.0m,  Min: 10m,   Max: 150m, Step: 5m,    Section: "Hedge"),
        new("TailRiskHedgePctOfNotional",      "Tail Risk Hedge % of Notional",         "decimal", 0.005m, Min: 0.001m,Max: 0.02m,Step: 0.001m,Section: "Hedge"),
        new("TailRiskHedgeMinDte",             "Tail Risk Hedge Min DTE",               "int",     7,      Min: 3,     Max: 30,              Section: "Hedge"),
        new("TailRiskHedgeRollDte",            "Tail Risk Hedge Roll DTE",              "int",     7,      Min: 1,     Max: 21,              Section: "Hedge"),
        new("ConditionalHedgeGammaPerThetaTrigger","Conditional Hedge Gamma/Theta Trigger","decimal",1.2m, Min: 0.5m,  Max: 3.0m, Step: 0.1m, Section: "Hedge"),
        new("ConditionalHedgeDteTrigger",      "Conditional Hedge DTE Trigger",         "int",     3,      Min: 1,     Max: 10,              Section: "Hedge"),
        new("HedgeWingWidth",                  "Hedge Wing Width (strikes)",            "int",     2,      Min: 1,     Max: 10,              Section: "Hedge"),
        new("VegaHedgeSizeAsPctOfBookVega",    "Vega Hedge Size % of Book Vega",        "decimal", 0.20m,  Min: 0.05m, Max: 0.50m, Step: 0.05m, Section: "Hedge"),

        // ── Circuit breaker ────────────────────────────────────────────────
        new("SoftStopLossPct",                 "Soft Stop Loss %",                      "decimal", 0.015m, Min: 0.005m,Max: 0.05m,Step: 0.005m,Section: "CircuitBreaker"),
        new("HardStopLossPct",                 "Hard Stop Loss %",                      "decimal", 0.025m, Min: 0.010m,Max: 0.08m,Step: 0.005m,Section: "CircuitBreaker"),

        // ── Margin ─────────────────────────────────────────────────────────
        new("ShockedUtilizationHardCap",       "Shocked Utilisation Hard Cap",          "decimal", 0.95m,  Min: 0.70m, Max: 1.0m, Step: 0.01m, Section: "Margin"),
        new("TrimToFitTargetPercent",          "Trim-to-Fit Target %",                  "decimal", 0.87m,  Min: 0.60m, Max: 0.95m, Step: 0.01m, Section: "Margin"),
        new("MarginFreshnessMaxAgeMinutes",    "Margin Freshness Max Age (min)",        "int",     20,     Min: 5,     Max: 60,              Section: "Margin"),
        new("MarginRefreshIntervalMinutes",    "Margin Refresh Interval (min)",         "int",     60,     Min: 15,    Max: 240,             Section: "Margin"),

        // ── Jump risk ──────────────────────────────────────────────────────
        new("JumpRiskRollingWindowMinutes",    "Jump Risk Rolling Window (min)",        "int",     30,     Min: 5,     Max: 120,             Section: "JumpRisk"),
        new("JumpRiskSpikeStdDevMultiplier",   "Jump Risk Spike Std-Dev Multiplier",    "decimal", 2.0m,   Min: 1.0m,  Max: 5.0m, Step: 0.5m,  Section: "JumpRisk"),
        new("JumpRiskClearStdDevMultiplier",   "Jump Risk Clear Std-Dev Multiplier",    "decimal", 1.0m,   Min: 0.5m,  Max: 3.0m, Step: 0.5m,  Section: "JumpRisk"),

        // ── Gamma-pin exit ─────────────────────────────────────────────────
        new("GammaPinExitMinutesBeforeClose",     "Gamma-Pin Exit (min before close)",     "int",  60,     Min: 15,    Max: 120,             Section: "GammaPin"),
        new("GammaPinSoftKillMinutesBeforeClose", "Gamma-Pin Soft Kill (min before close)","int",  120,    Min: 30,    Max: 240,             Section: "GammaPin"),
        new("GammaPinSoftKillGammaRatio",         "Gamma-Pin Soft Kill Gamma Ratio",       "decimal",0.667m,Min:0.10m, Max: 2.0m, Step:0.05m,Section: "GammaPin"),

        // ── Stop-loss ──────────────────────────────────────────────────────
        new("StopLossMultiplier",              "Stop Loss Multiplier (× premium)",      "decimal", 2.0m,   Min: 1.0m,  Max: 5.0m, Step: 0.5m, Section: "StopLoss"),
        new("RecoveryStep3SharpeMin",          "Recovery Step 3 Min Sharpe",            "decimal", 0.8m,   Min: 0.2m,  Max: 2.0m, Step: 0.1m, Section: "StopLoss"),

        // ── Reinvestment ───────────────────────────────────────────────────
        new("MaxVelocityLayerPctOfAccount",    "Max SPV Layer % of Account",            "decimal", 0.875m, Min: 0.50m, Max: 1.0m, Step: 0.025m,Section: "Reinvestment"),
        new("FrictionBufferPercent",           "Friction Buffer %",                     "decimal", 0.12m,  Min: 0.02m, Max: 0.25m, Step: 0.01m, Section: "Reinvestment"),
        new("FrictionLeakageAlertPercent",     "Friction Leakage Alert %",              "decimal", 0.0175m,Min: 0.005m,Max: 0.05m, Step:0.005m, Section: "Reinvestment"),

        // ── Synthetic pricer ───────────────────────────────────────────────
        new("RiskFreeRate",                    "Risk-Free Rate",                        "decimal", 0.065m, Min: 0.02m, Max: 0.15m, Step: 0.005m,Section: "Pricer"),
        new("SkewSlopePerStrike",              "Skew Slope per Strike",                 "decimal", -0.002m,Min:-0.01m, Max: 0m,   Step: 0.001m,Section: "Pricer"),
        new("SlippageFraction",                "Slippage Fraction",                     "decimal", 0.002m, Min: 0m,    Max: 0.02m, Step: 0.001m,Section: "Pricer"),
        new("AfternoonSlippageMultiplier",     "Afternoon Slippage Multiplier",         "decimal", 1.25m,  Min: 1.0m,  Max: 2.5m,  Step: 0.05m, Section: "Pricer"),
        new("HedgeLegSlippageMultiplier",      "Hedge Leg Slippage Multiplier",         "decimal", 0.50m,  Min: 0.10m, Max: 1.0m,  Step: 0.05m, Section: "Pricer"),
        new("AfternoonSlippageStartHour",      "Afternoon Slippage Start Hour",         "int",     14,     Min: 12,    Max: 15,              Section: "Pricer"),
        new("AfternoonSlippageStartMinute",    "Afternoon Slippage Start Minute",       "int",     30,     Min: 0,     Max: 59,              Section: "Pricer"),

        // ── Cluster / concentration ────────────────────────────────────────
        new("ClusterHardCap",                  "Cluster Hard Cap (fraction of NAV)",    "decimal", 0.30m,  Min: 0.10m, Max: 0.60m, Step: 0.05m, Section: "Cluster"),
        new("ResultsSeasonClusterCapMultiplier","Results Season Cluster Cap Multiplier","decimal", 0.50m,  Min: 0.20m, Max: 1.0m,  Step: 0.05m, Section: "Cluster",
            Hint: "Applied on top of ClusterHardCap during earnings season. Effective cap = ClusterHardCap × multiplier."),

        // ── Per-regime dicts (schema metadata only; values are JSON objects) ─
        new("TailRiskScoreCeiling",            "Tail Risk Score Ceiling (per regime)",  "json", null,   Hint: "Dictionary<MarketRegime, decimal>. Max tail-risk score above which entry is blocked."),
        new("GammaPerThetaCeiling",            "Gamma/Theta Ceiling (per regime)",      "json", null,   Hint: "Dictionary<MarketRegime, decimal>. Max gamma-per-theta ratio at entry."),
        new("LiquiditySurvivalFloor",          "Liquidity Floor (per regime)",          "json", null,   Hint: "Dictionary<MarketRegime, decimal>. Min bid-ask/premium ratio for entry."),
        new("MinHedgeCoverageRatio",           "Min Hedge Coverage (per regime)",       "json", null,   Hint: "Dictionary<MarketRegime, decimal>. Min GammaHedged/GrossGamma before entry."),
        new("RecoveryWindowMinSessions",       "Recovery Window Min Sessions",          "json", null,   Hint: "Dictionary<MarketRegime, int>. Min sessions before recovery step-up."),
        new("RecoveryWindowMaxSessions",       "Recovery Window Max Sessions",          "json", null,   Hint: "Dictionary<MarketRegime, int>. Max sessions before forced review."),
        new("ProfitTargetFraction",            "Profit Target Fraction (per regime)",   "json", null,   Hint: "Dictionary<MarketRegime, decimal>. Fraction of max credit to take profit."),
        new("RecoveryMultiplierMin",           "Recovery Multiplier Min (per regime)",  "json", null,   Hint: "Dictionary<MarketRegime, decimal>. Lower bound of recovery size multiplier."),
        new("RecoveryMultiplierMax",           "Recovery Multiplier Max (per regime)",  "json", null,   Hint: "Dictionary<MarketRegime, decimal>. Upper bound of recovery size multiplier."),
        new("DtePreferenceWeights",            "DTE Preference Weights (per regime)",   "json", null,   Hint: "Dictionary<MarketRegime, decimal[]>. 6 bucket weights (must sum 1.0; each ≤0.35)."),
        new("VelocityScoreWeights",            "Velocity Score Weights",                "json", null,   Hint: "decimal[5] summing to 1.0: [IV mean-rev, skew, term-struct, RV spread, breadth]."),
        new("OpportunityDensityWeights",       "Opportunity Density Weights",           "json", null,   Hint: "decimal[5] summing to 1.0: [PCR, OI, skew steepness, term carry, events]."),
        new("StressOverlays",                  "Stress Overlays",                       "json", null,   Hint: "Dictionary<string, StressOverlayConfig>. Named stress scenarios for backtesting."),
        new("UnderlyingSymbols",               "Underlying Symbols",                    "json", null,   Hint: "string[] e.g. [\"NIFTY\",\"BANKNIFTY\"]. Symbols monitored for jump-risk."),
    ];
}
