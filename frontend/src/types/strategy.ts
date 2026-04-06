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
  H1  = '1h',
  H4  = '4h',
  D1  = '1D',
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

// ─── Indicators ───────────────────────────────────────────────────────────────

export interface ParamRange {
  min: number
  max: number
}

export interface IndicatorConfig {
  id: string
  type: IndicatorType
  timeframe: Timeframe
  role: IndicatorRole
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
  | { kind: 'indicator'; indicatorId: string; field?: string }
  | { kind: 'window';    expr: WindowExpression }
  | { kind: 'value';     value: number }

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
  timeframe:        Object.values(Timeframe),
  tradingStyle:     Object.values(TradingStyle),
  indicatorType:    Object.values(IndicatorType),
  indicatorRole:    Object.values(IndicatorRole),
  conditionOp:      Object.values(ConditionOperator),
  aggregationType:  Object.values(AggregationType),
  stopType:         Object.values(StopType),
  trailingType:     Object.values(TrailingType),
  scenarioStatus:   Object.values(ScenarioStatus),
  strategyStatus:   Object.values(StrategyStatus),
  deploymentStatus: Object.values(DeploymentStatus),
  runMode:          Object.values(RunMode),
  exitCombineLogic: Object.values(ExitCombineLogic),
  dayOfWeek:        Object.values(DayOfWeek),
  moveStopTo:       Object.values(MoveStopTo),
  startCondition:   Object.values(StartCondition),
  deploymentMode:   Object.values(DeploymentMode),
  broker:           Object.values(Broker),
  overrideSection:  Object.values(OverrideSection),
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
