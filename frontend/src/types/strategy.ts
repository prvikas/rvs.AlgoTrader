// ─── Enums — compile-time contract ───────────────────────────────────────────
// These mirror the enum_values DB table exactly at deploy time.
// When a new value is added to the DB, add it here too in the next release.
// UI dropdowns do NOT use these directly — they use useEnums() which reads the DB.

export enum Timeframe {
  M1  = '1m',
  M3  = '3m',
  M5  = '5m',
  M15 = '15m',
  M30 = '30m',
  H1  = '60m',   // MStock + validator both use '60m' for hourly
  H4  = '4h',
  D1  = '1d',    // lowercase — matches backend validator and all broker clients
}

export enum TradingStyle {
  Scalping   = 'Scalping',
  Intraday   = 'Intraday',
  Swing      = 'Swing',
  Positional = 'Positional',
  Custom     = 'Custom',
}

export enum IndicatorType {
  EMA              = 'EMA',
  SMA              = 'SMA',
  HullMA           = 'HullMA',
  DonchianChannel  = 'DonchianChannel',
  SuperTrend       = 'SuperTrend',
  RSI              = 'RSI',
  CCI              = 'CCI',
  Stochastics      = 'Stochastics',
  MACD             = 'MACD',
  ATR              = 'ATR',
  BollingerBands   = 'BollingerBands',
  RangePercentile  = 'RangePercentile',
  Volume           = 'Volume',
  VolumeSpike      = 'VolumeSpike',
  VWAP             = 'VWAP',
  SwingHighLow     = 'SwingHighLow',
  InsideBar        = 'InsideBar',
  Engulfing        = 'Engulfing',
  PrevHighLowBreak = 'PrevHighLowBreak',
  SessionFilter    = 'SessionFilter',
  DayOfWeekFilter  = 'DayOfWeekFilter',
  ADX              = 'ADX',
  ATRPercentile    = 'ATRPercentile',
  // Options market-context indicators (read from StrategyContext, not computed from candles)
  IVRank           = 'IVRank',
  IVPercentile     = 'IVPercentile',
  PCR              = 'PCR',
  PCRChange        = 'PCRChange',
  AtmIV            = 'AtmIV',
  MaxPain          = 'MaxPain',
}

export enum IndicatorRole {
  EntryTrigger  = 'EntryTrigger',
  EntryFilter   = 'EntryFilter',
  Exit          = 'Exit',
  StopLoss      = 'StopLoss',
  TakeProfit    = 'TakeProfit',
  TrailingStop  = 'TrailingStop',
  RiskModel     = 'RiskModel',
  RegimeFilter  = 'RegimeFilter',
  InfoOnly      = 'InfoOnly',
}

export enum ConditionOperator {
  Gt           = '>',
  Lt           = '<',
  Gte          = '>=',
  Lte          = '<=',
  Eq           = '==',
  Neq          = '!=',
  CrossesAbove = 'crossesAbove',
  CrossesBelow = 'crossesBelow',
}

export enum AggregationType {
  Avg        = 'avg',
  Max        = 'max',
  Min        = 'min',
  Percentile = 'percentile',
  Highest    = 'highest',
  Lowest     = 'lowest',
}

export enum StopType {
  FixedPoints      = 'FixedPoints',
  ATRMultiple      = 'ATRMultiple',
  StructureLowHigh = 'StructureLowHigh',
}

export enum TrailingType {
  FixedPoints     = 'FixedPoints',
  ATRMultiple     = 'ATRMultiple',
  MovingAverage   = 'MovingAverage',
  DonchianChannel = 'DonchianChannel',
}

export enum ScenarioStatus {
  Draft         = 'Draft',
  Running       = 'Running',
  Backtested    = 'Backtested',
  FwdTesting    = 'Fwd Testing',
  LiveCandidate = 'Live Candidate',
  Live          = 'Live',
  Archived      = 'Archived',
}

export enum StrategyStatus {
  Draft      = 'Draft',
  Backtested = 'Backtested',
  FwdTesting = 'Fwd Testing',
  Live       = 'Live',
  Archived   = 'Archived',
}

export enum DeploymentStatus {
  Draft         = 'Draft',
  Running       = 'Running',
  Backtested    = 'Backtested',
  FwdTesting    = 'Fwd Testing',
  LiveCandidate = 'Live Candidate',
  Live          = 'Live',
  Archived      = 'Archived',
}

export enum RunMode {
  Backtest    = 'Backtest',
  ForwardTest = 'ForwardTest',
}

export enum ExitCombineLogic {
  AND = 'AND',
  OR  = 'OR',
}

export enum DayOfWeek {
  Mon = 'Mon',
  Tue = 'Tue',
  Wed = 'Wed',
  Thu = 'Thu',
  Fri = 'Fri',
  Sat = 'Sat',
  Sun = 'Sun',
}

export enum MoveStopTo {
  Breakeven     = 'breakeven',
  PreviousLevel = 'previousLevel',
  Custom        = 'custom',
}

export enum SignalLayer {
  HTFBias            = 'HTFBias',
  MTFContext         = 'MTFContext',
  PrimarySignal      = 'PrimarySignal',
  ConfirmationFilter = 'ConfirmationFilter',
  EntryTrigger       = 'EntryTrigger',
  Invalidation       = 'Invalidation',
}

export enum EntryOrderType {
  MarketNextOpen  = 'MarketNextOpen',
  LimitAtClose    = 'LimitAtClose',
  LimitAtRetest   = 'LimitAtRetest',
  StopEntryOffset = 'StopEntryOffset',
}

export enum EntryTiming {
  Immediately        = 'immediately',
  WaitForCandleClose = 'waitForCandleClose',
  FirstNBarsOnly     = 'firstNBarsOnly',
}

export enum ScalingModel {
  FullImmediately = 'FullImmediately',
  ScaleIn2        = 'ScaleIn2',
  Pyramid         = 'Pyramid',
}

export enum SizingMethod {
  FixedRiskPct   = 'FixedRiskPct',
  KellyFraction  = 'KellyFraction',
  VolatilityNorm = 'VolatilityNorm',
  FixedLots      = 'FixedLots',
  FixedCapital   = 'FixedCapital',
}

export enum SessionStateProperty {
  IsFirstSignalOfSession = 'IsFirstSignalOfSession',
  BarsSinceSessionOpen   = 'BarsSinceSessionOpen',
  PreviousTradeWasLoss   = 'PreviousTradeWasLoss',
  OpenPositionCount      = 'OpenPositionCount',
}

export enum StartCondition {
  Immediately         = 'immediately',
  AfterKR             = 'afterKR',
  AfterAbsoluteProfit = 'afterAbsoluteProfit',
}

export enum DeploymentMode {
  Backtest    = 'Backtest',
  ForwardTest = 'Forward Test',
  Live        = 'Live',
}

export enum Broker {
  MStock  = 'MStock',
  Zerodha = 'Zerodha',
  Upstox  = 'Upstox',
}

export enum OverrideSection {
  Indicator     = 'indicator',
  StopLoss      = 'stopLoss',
  ProfitTarget  = 'profitTarget',
  Trailing      = 'trailing',
  ScaleOut      = 'scaleOut',
  ExitBehaviour = 'exitBehaviour',
  RR            = 'rr',
}

// ─── Options spread types ─────────────────────────────────────────────────────

export enum SpreadType {
  BullCallSpread = 'BullCallSpread',
  BearPutSpread  = 'BearPutSpread',
  BullPutSpread  = 'BullPutSpread',
  BearCallSpread = 'BearCallSpread',
  IronCondor     = 'IronCondor',
  ShortStraddle  = 'ShortStraddle',
  ShortStrangle  = 'ShortStrangle',
  CalendarSpread = 'CalendarSpread',
  LongCall       = 'LongCall',
  LongPut        = 'LongPut',
}

export enum OptionLegType   { CE = 'CE', PE = 'PE' }
export enum LegDirection    { Buy = 'Buy', Sell = 'Sell' }
export enum StrikeSelection { Atm = 'Atm', OtmByStrike = 'OtmByStrike', OtmByPct = 'OtmByPct', ByDelta = 'ByDelta' }

export interface SpreadLegDef {
  optionType:    OptionLegType
  direction:     LegDirection
  selectionMode: StrikeSelection
  otmStrikes:    number
  otmPct:        number
  targetDelta:   number
  quantity:      number
  nearestWeekly: boolean
}

export interface OptionsConfig {
  enabled:          boolean
  spreadType:       SpreadType
  legs:             SpreadLegDef[]
  expiryPreference: 'Weekly' | 'Monthly'
  stopLossPct:      number
  profitTargetPct:  number
  minIvRank?:       number
  maxIvRank?:       number
  minAtmIv?:        number
  maxAtmIv?:        number
  minPcr?:          number
  maxPcr?:          number
}

// ─── Indicators ───────────────────────────────────────────────────────────────

export interface ParamRange {
  min: number
  max: number
}

export interface IndicatorConfig {
  id: string
  /** Human-readable name shown in condition dropdowns (e.g. "EMA 9", "EMA 21").
   *  Falls back to "{type} ({timeframe})" when absent. */
  label?: string
  type: IndicatorType
  timeframe: Timeframe
  role: IndicatorRole
  signalLayer: SignalLayer
  layerOrder: number
  baseParams: Record<string, number | string | boolean>
  allowedParamRanges: Record<string, ParamRange | string[]>
}

// ─── Rule builder ─────────────────────────────────────────────────────────────

export interface WindowExpression {
  sourceIndicatorId: string
  lookbackBars: number
  aggregationType: AggregationType
  multiplier?: number
}

export type ConditionOperand =
  | { kind: 'indicator';    indicatorId: string; field?: string }
  | { kind: 'window';       expr: WindowExpression }
  | { kind: 'value';        value: number }
  | { kind: 'absence';      indicatorId: string; lookbackBars: number }
  | { kind: 'percentile';   indicatorId: string; lookbackBars: number; pct: number }
  | { kind: 'slope';        indicatorId: string; lookbackBars: number }
  | { kind: 'sessionState'; property: SessionStateProperty }

export interface Condition {
  id: string
  left: ConditionOperand
  operator: ConditionOperator
  right: ConditionOperand
}

export interface RuleGroup {
  id: string
  label: string
  logicalOperator: ExitCombineLogic
  conditions: Condition[]
}

export interface EntryExitBlock {
  enabled: boolean
  groupOperator: ExitCombineLogic
  groups: RuleGroup[]
}

// ─── Exit behaviour ───────────────────────────────────────────────────────────

export interface ExitBehaviour {
  exitEndOfSession: boolean
  exitAfterNBars: number | null
  exitAtStopOrTargetOnly: boolean
  combineLogic: ExitCombineLogic
  tradableDays: DayOfWeek[]
  sessionStart?: string
  sessionEnd?: string
}

// ─── Stops, targets, trailing, scale-out ─────────────────────────────────────

export interface StopTargetConfig {
  type: StopType
  baseValue: number
  allowedRange: ParamRange
}

export interface PartialExit {
  triggerR: number
  percentToClose: number
  moveStopTo: MoveStopTo
  customLevel?: number
}

export interface TrailingConfig {
  enabled: boolean
  trailingType: TrailingType
  trailingParams: Record<string, number>
  startCondition: StartCondition
  startConditionValue?: number
  partialExits: PartialExit[]
}

export interface RRConfig {
  rrMin: number
  rrMax: number
  deriveTargetFromSL: boolean
}

// ─── Entry Execution ─────────────────────────────────────────────────────────

export interface EntryExecutionModel {
  orderType: EntryOrderType
  timing: EntryTiming
  firstNBars?: number
  maxSlippagePoints?: number
  scalingModel: ScalingModel
  pyramidIntervalR?: number
}

// ─── Position Sizing ──────────────────────────────────────────────────────────

export interface DrawdownScaling {
  enabled: boolean
  reduceSizeAtDrawdownPct: number
  minSizeMultiplier: number
}

export interface RegimeScaling {
  enabled: boolean
  indicatorId: string
  conditionForIncrease: Condition
  increaseSizeMultiplier: number
}

export interface PositionSizingModel {
  method: SizingMethod
  baseRiskPct?: number
  kellyFraction?: number
  fixedLots?: number
  fixedCapital?: number
  maxPositionPct: number
  liquidityCapPctADV?: number
  drawdownScaling: DrawdownScaling
  regimeScaling?: RegimeScaling
}

// ─── Stop State Machine ───────────────────────────────────────────────────────

export interface StopState {
  id: string
  label: string
  stopType: StopType
  value: number
  activationCondition?: {
    triggerR?: number
    triggerBars?: number
    triggerPct?: number
  }
  moveStopTo?: MoveStopTo
  customLevel?: number
}

export interface StopStateMachine {
  states: StopState[]
  allowedRanges: Record<string, ParamRange>
}

// ─── Profit Booking Rule ──────────────────────────────────────────────────────
// Book a % of the position at a target R and optionally trail the remainder.

export interface ProfitBookingRule {
  enabled: boolean
  triggerR: number          // book when profit reaches this R multiple
  bookPct: number           // % of position to close at trigger (1–99)
  continueTrailing: boolean // trail remaining position after booking
  trailType?: TrailingType  // which trailing method for the remainder
  trailMultiplier?: number  // ATR multiplier or points for trail
}

// ─── Parameter Sweep ──────────────────────────────────────────────────────────

export interface ParameterSweep {
  id: string
  strategyId: string
  label: string
  hypothesis: string
  paramKey: string
  indicatorId?: string
  section: OverrideSection
  from: number
  to: number
  step: number
  otherOverrides: ParameterOverride[]
  generatedScenarioIds: string[]
}

// ─── Trade Record ─────────────────────────────────────────────────────────────

export interface TradeRecord {
  id: string
  scenarioId: string
  runId: string
  entryTime: string
  exitTime: string
  direction: 'Long' | 'Short'
  entryPrice: number
  exitPrice: number
  stopPrice: number
  targetPrice: number
  mae: number
  mfe: number
  pnlR: number
  pnlAbsolute: number
  barsHeld: number
  exitReason: 'StopHit' | 'TargetHit' | 'TrailingStop' | 'SessionEnd' | 'Manual' | 'Strategy'
  regime?: string
}

// ─── Risk controls ────────────────────────────────────────────────────────────

export interface RiskControls {
  maxRiskPerTradePercent: number
  maxTradesPerDay: number
  maxOpenPositions?: number
}

export interface KillSwitch {
  dailyLossLimitR?: number
  maxIntradayDrawdownPercent?: number
  cooldownBarsAfterHit?: number
}

// ─── Regime ───────────────────────────────────────────────────────────────────

export interface RegimeDefinition {
  id: string
  label: string
  conditions: Condition[]
}

// ─── Strategy ─────────────────────────────────────────────────────────────────

export interface Strategy {
  id: string
  name: string
  description?: string
  primaryTimeframe: Timeframe
  instruments: string[]
  tradingStyle: TradingStyle
  status: StrategyStatus
  indicators: IndicatorConfig[]
  longEntry: EntryExitBlock
  shortEntry: EntryExitBlock
  longExit: EntryExitBlock
  shortExit: EntryExitBlock
  exitBehaviour: ExitBehaviour
  stopLoss: StopTargetConfig
  profitTarget: StopTargetConfig
  rrConfig: RRConfig
  trailing: TrailingConfig
  riskControls: RiskControls
  killSwitch?: KillSwitch
  regimeDefinitions?: RegimeDefinition[]
  allowedRegimes?: string[]
  // PROMPT-002 additions
  entryExecution?: EntryExecutionModel
  positionSizing?: PositionSizingModel
  stopStateMachine?: StopStateMachine
  profitBooking?: ProfitBookingRule
  // Options spread config — when set, GenericRulesStrategy fires SpreadEntry signals
  optionsConfig?: OptionsConfig
  createdAt: string
  updatedAt: string
}

// ─── Scenario ─────────────────────────────────────────────────────────────────

export interface ParameterOverride {
  section: OverrideSection
  indicatorId?: string
  paramKey: string
  baseValue: number | boolean
  overrideValue: number | boolean
}

export interface Scenario {
  id: string
  strategyId: string
  name: string
  description?: string
  capital: number
  brokerAccount: Broker
  backtestRange: { from: string; to: string }
  parameterOverrides: ParameterOverride[]
  status: ScenarioStatus
  lastRunAt?: string
  lastMetrics?: RunMetrics
  // PROMPT-002 additions
  hypothesis?: string
  hypothesisTag?: string
  isBaseline?: boolean
  sweepGroupId?: string
  promotionNotes?: string
}

// ─── Deployment ───────────────────────────────────────────────────────────────

export interface Deployment {
  id: string
  strategyId: string
  scenarioId: string
  name: string
  symbol: string
  timeframe: Timeframe
  mode: DeploymentMode
  broker: Broker
  allocatedCapital: number
  schedule: {
    days: DayOfWeek[]
    startTime?: string
    endTime?: string
  }
  status: DeploymentStatus
}

// ─── RunResult ────────────────────────────────────────────────────────────────

export interface RunMetrics {
  returnPct: number
  maxDrawdownPct: number
  sharpe: number
  winRate: number
  profitFactor: number
  tradeCount: number
  avgRPerTrade: number
  expectancy: number
  avgRealisedRR: number
  longWinRate?: number
  shortWinRate?: number
  btToFtDegradationRatio?: number
  drawdownDurationBars?: number
  // Robustness metrics (PROMPT-002)
  walkForwardEfficiency?: number
  parameterStabilityScore?: number
  monteCarloMedianReturn?: number
  monteCarloPct5Return?: number
  overfitScore?: number
  degreesFreedomRatio?: number
  regimeBreakdown?: Record<string, RunMetrics>
}

export interface RunResult {
  id: string
  scenarioId: string
  strategyId: string
  mode: RunMode
  dateRange: { from: string; to: string }
  engineVersion: string
  dataVersion: string
  metrics: RunMetrics
  completedAt: string
}

// ─── ENUM_VALUES — compile-time fallback + API response validator ─────────────
// UI components: use useEnums() instead. This map exists for:
//   1. Validating /api/enums response values against known enum members
//   2. Offline/error fallback inside EnumsContext only
//   3. Unit tests

export const ENUM_VALUES = {
  timeframe:              Object.values(Timeframe),
  tradingStyle:           Object.values(TradingStyle),
  indicatorType:          Object.values(IndicatorType),
  indicatorRole:          Object.values(IndicatorRole),
  conditionOp:            Object.values(ConditionOperator),
  aggregationType:        Object.values(AggregationType),
  stopType:               Object.values(StopType),
  trailingType:           Object.values(TrailingType),
  scenarioStatus:         Object.values(ScenarioStatus),
  strategyStatus:         Object.values(StrategyStatus),
  deploymentStatus:       Object.values(DeploymentStatus),
  runMode:                Object.values(RunMode),
  exitCombineLogic:       Object.values(ExitCombineLogic),
  dayOfWeek:              Object.values(DayOfWeek),
  moveStopTo:             Object.values(MoveStopTo),
  startCondition:         Object.values(StartCondition),
  deploymentMode:         Object.values(DeploymentMode),
  broker:                 Object.values(Broker),
  overrideSection:        Object.values(OverrideSection),
  // PROMPT-002
  signalLayer:            Object.values(SignalLayer),
  entryOrderType:         Object.values(EntryOrderType),
  entryTiming:            Object.values(EntryTiming),
  scalingModel:           Object.values(ScalingModel),
  sizingMethod:           Object.values(SizingMethod),
  sessionStateProperty:   Object.values(SessionStateProperty),
  // Options
  spreadType:             Object.values(SpreadType),
  optionLegType:          Object.values(OptionLegType),
  legDirection:           Object.values(LegDirection),
  strikeSelection:        Object.values(StrikeSelection),
} as const

// validateEnumResponse: called inside EnumsContext after fetch.
// Logs a warning for any value returned by the API that is not a known enum member.
// Unknown values are kept (so new DB rows work without a frontend deploy) but warned.
export function validateEnumResponse(
  apiResponse: Record<string, { value: string; label: string }[]>
): void {
  for (const [domain, options] of Object.entries(apiResponse)) {
    const known = (ENUM_VALUES as Record<string, readonly string[]>)[domain]
    if (!known) {
      console.warn(`[EnumsContext] Unknown domain "${domain}" returned by /api/enums`)
      continue
    }
    for (const opt of options) {
      if (!known.includes(opt.value)) {
        console.warn(
          `[EnumsContext] Domain "${domain}" value "${opt.value}" is not in TypeScript enum. ` +
          `Add it to strategy.ts enum to restore type safety.`
        )
      }
    }
  }
}
