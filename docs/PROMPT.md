# PROMPT.md — Precision AI Coding Prompt

> Read this file only when explicitly requested via `docs/PROMPT.md`.
> Delete or stub each entry immediately after implementation is confirmed done.

---

## PROMPT-001 — Strategy & Scenario Full Domain Model
**Status:** PENDING

**Context:**
This prompt is a full replacement of the v1 model. The v1 separated Strategy / Scenario / Deployment / Run
conceptually — that separation is kept and extended. The new spec adds:
- Rich indicator ownership with multi-timeframe support and allowed parameter ranges
- Structured entry/exit rule builder (RuleGroups, Conditions, WindowExpressions)
- Exit behaviour object, risk controls, kill-switch, regime filters
- Rich stop/target/trailing/scale-out mechanics
- Full TypeScript interfaces for all entities backed by enums
- DB-backed enum values with runtime fetch — single source of truth is the DB
- Five UI screens with precise layout requirements

**Core invariants (never violate):**
1. Strategy defines structure: indicators, rules, exit logic, risk limits. It does NOT hold symbol, broker, capital.
2. Scenario inherits everything structural. It can ONLY override numeric parameters within allowedParamRanges.
3. Scenario cannot add, remove, or replace indicators. Cannot change rule structure or indicator roles/timeframes.
4. Deployment binds a Scenario to an execution context: symbol, timeframe, broker, capital, schedule.
5. RunResult ties a Scenario to a specific backtest or forward-test execution with full metrics.
6. **Enum values are DB-owned at runtime.** TypeScript enums in `strategy.ts` are the compile-time contract
   and validator only. The actual option lists shown in every UI dropdown come from `useEnums()` which
   fetches `GET /api/enums` → sourced from the `enum_values` DB table. No component may hardcode string arrays.

---

### Scope
Frontend + backend enum endpoint (`GET /api/enums`) + DB seed.
All other backend routes: add `// TODO: API` comment + use mock data shaped to DTOs below.

---

### Token reference (AP-020 — no raw hex anywhere)
All colors from `frontend/src/styles/tokens.ts`. Use the `C` object:

| Purpose | Token |
|---|---|
| Card/panel border | `C.border` |
| Table row separator | `C.border2` |
| Prominent divider | `C.border3` |
| Muted label / metadata | `C.textMuted` |
| Placeholder / hint | `C.textDim` |
| Secondary text | `C.textSub` |
| Best-value cell bg | `C.greenBg` |
| Error/loss cell bg | `C.redBg` |
| Forward test accent | `C.blue` |
| Running/success | `C.green` |
| Warning/paused | `C.amber` |

Spacing from `SP.*`, table padding from `TABLE_CELL`, font from `F.mono` / `F.sans`.

---

### Canonical status chips (use EXACTLY these enum values everywhere)

| Enum value | Color token | Notes |
|---|---|---|
| `ScenarioStatus.Draft` | `C.textMuted` | default |
| `ScenarioStatus.Running` | `C.amber` | pulse animation |
| `ScenarioStatus.Backtested` | `C.blue` | |
| `ScenarioStatus.FwdTesting` | `C.blue` + italic | |
| `ScenarioStatus.LiveCandidate` | `C.green` + italic | |
| `ScenarioStatus.Live` | `C.green` | |
| `ScenarioStatus.Archived` | `C.textDim` | |

---

## DB — enum_values table

This table is the **single source of truth** for all domain primitive values shown in the UI.
Adding a new broker, indicator type, or timeframe is a DB INSERT — no frontend code change needed.

```sql
-- Migration: create enum_values lookup table
CREATE TABLE enum_values (
  domain      VARCHAR(64)   NOT NULL,  -- matches ENUM_VALUES key: 'timeframe', 'broker', 'indicatorType' ...
  value       VARCHAR(128)  NOT NULL,  -- the actual stored/API value, e.g. '5m', 'MStock', 'EMA'
  label       VARCHAR(128)  NOT NULL,  -- display label, e.g. '5 min', 'MStock', 'EMA'
  sort_order  INT           NOT NULL DEFAULT 0,
  is_active   BOOLEAN       NOT NULL DEFAULT TRUE,
  PRIMARY KEY (domain, value)
);

-- Seed data — mirrors the TypeScript enums exactly at initial deploy.
-- Add rows here to extend without a frontend redeploy.
INSERT INTO enum_values (domain, value, label, sort_order) VALUES
  -- Timeframe
  ('timeframe','1m','1 min',1), ('timeframe','3m','3 min',2), ('timeframe','5m','5 min',3),
  ('timeframe','15m','15 min',4), ('timeframe','30m','30 min',5),
  ('timeframe','1h','1 Hour',6), ('timeframe','4h','4 Hour',7), ('timeframe','1D','Daily',8),
  -- TradingStyle
  ('tradingStyle','Scalping','Scalping',1), ('tradingStyle','Intraday','Intraday',2),
  ('tradingStyle','Swing','Swing',3), ('tradingStyle','Positional','Positional',4),
  ('tradingStyle','Custom','Custom',5),
  -- IndicatorType — Trend
  ('indicatorType','EMA','EMA',1), ('indicatorType','SMA','SMA',2),
  ('indicatorType','HullMA','Hull MA',3), ('indicatorType','DonchianChannel','Donchian Channel',4),
  ('indicatorType','SuperTrend','SuperTrend',5),
  -- IndicatorType — Momentum
  ('indicatorType','RSI','RSI',10), ('indicatorType','CCI','CCI',11),
  ('indicatorType','Stochastics','Stochastics',12), ('indicatorType','MACD','MACD',13),
  -- IndicatorType — Volatility
  ('indicatorType','ATR','ATR',20), ('indicatorType','BollingerBands','Bollinger Bands',21),
  ('indicatorType','RangePercentile','Range Percentile',22),
  -- IndicatorType — Volume
  ('indicatorType','Volume','Volume',30), ('indicatorType','VolumeSpike','Volume Spike',31),
  ('indicatorType','VWAP','VWAP',32),
  -- IndicatorType — Price Action
  ('indicatorType','SwingHighLow','Swing High/Low',40), ('indicatorType','InsideBar','Inside Bar',41),
  ('indicatorType','Engulfing','Engulfing',42), ('indicatorType','PrevHighLowBreak','Prev High/Low Break',43),
  -- IndicatorType — Session/Time
  ('indicatorType','SessionFilter','Session Filter',50), ('indicatorType','DayOfWeekFilter','Day of Week Filter',51),
  -- IndicatorType — Regime
  ('indicatorType','ADX','ADX',60), ('indicatorType','ATRPercentile','ATR Percentile',61),
  -- IndicatorRole
  ('indicatorRole','EntryTrigger','Entry Trigger',1), ('indicatorRole','EntryFilter','Entry Filter',2),
  ('indicatorRole','Exit','Exit',3), ('indicatorRole','StopLoss','Stop Loss',4),
  ('indicatorRole','TakeProfit','Take Profit',5), ('indicatorRole','TrailingStop','Trailing Stop',6),
  ('indicatorRole','RiskModel','Risk Model',7), ('indicatorRole','RegimeFilter','Regime Filter',8),
  ('indicatorRole','InfoOnly','Info Only',9),
  -- ConditionOperator
  ('conditionOp','>','>',1), ('conditionOp','<','<',2), ('conditionOp','>=','≥',3),
  ('conditionOp','<=','≤',4), ('conditionOp','==','=',5), ('conditionOp','!=','≠',6),
  ('conditionOp','crossesAbove','Crosses Above',7), ('conditionOp','crossesBelow','Crosses Below',8),
  -- AggregationType
  ('aggregationType','avg','Average',1), ('aggregationType','max','Max',2),
  ('aggregationType','min','Min',3), ('aggregationType','percentile','Percentile',4),
  ('aggregationType','highest','Highest',5), ('aggregationType','lowest','Lowest',6),
  -- StopType
  ('stopType','FixedPoints','Fixed Points',1), ('stopType','ATRMultiple','ATR Multiple',2),
  ('stopType','StructureLowHigh','Structure Low/High',3),
  -- TrailingType
  ('trailingType','FixedPoints','Fixed Points',1), ('trailingType','ATRMultiple','ATR Multiple',2),
  ('trailingType','MovingAverage','Moving Average',3), ('trailingType','DonchianChannel','Donchian Channel',4),
  -- ScenarioStatus
  ('scenarioStatus','Draft','Draft',1), ('scenarioStatus','Running','Running',2),
  ('scenarioStatus','Backtested','Backtested',3), ('scenarioStatus','Fwd Testing','Fwd Testing',4),
  ('scenarioStatus','Live Candidate','Live Candidate',5), ('scenarioStatus','Live','Live',6),
  ('scenarioStatus','Archived','Archived',7),
  -- StrategyStatus
  ('strategyStatus','Draft','Draft',1), ('strategyStatus','Backtested','Backtested',2),
  ('strategyStatus','Fwd Testing','Fwd Testing',3), ('strategyStatus','Live','Live',4),
  ('strategyStatus','Archived','Archived',5),
  -- DeploymentStatus (same as ScenarioStatus)
  ('deploymentStatus','Draft','Draft',1), ('deploymentStatus','Running','Running',2),
  ('deploymentStatus','Backtested','Backtested',3), ('deploymentStatus','Fwd Testing','Fwd Testing',4),
  ('deploymentStatus','Live Candidate','Live Candidate',5), ('deploymentStatus','Live','Live',6),
  ('deploymentStatus','Archived','Archived',7),
  -- RunMode
  ('runMode','Backtest','Backtest',1), ('runMode','ForwardTest','Forward Test',2),
  -- ExitCombineLogic
  ('exitCombineLogic','AND','AND',1), ('exitCombineLogic','OR','OR',2),
  -- DayOfWeek
  ('dayOfWeek','Mon','Mon',1), ('dayOfWeek','Tue','Tue',2), ('dayOfWeek','Wed','Wed',3),
  ('dayOfWeek','Thu','Thu',4), ('dayOfWeek','Fri','Fri',5),
  ('dayOfWeek','Sat','Sat',6), ('dayOfWeek','Sun','Sun',7),
  -- MoveStopTo
  ('moveStopTo','breakeven','Breakeven',1), ('moveStopTo','previousLevel','Previous Level',2),
  ('moveStopTo','custom','Custom',3),
  -- StartCondition
  ('startCondition','immediately','Immediately',1), ('startCondition','afterKR','After K×R',2),
  ('startCondition','afterAbsoluteProfit','After Absolute Profit',3),
  -- DeploymentMode
  ('deploymentMode','Backtest','Backtest',1), ('deploymentMode','Forward Test','Forward Test',2),
  ('deploymentMode','Live','Live',3),
  -- Broker
  ('broker','MStock','MStock',1), ('broker','Zerodha','Zerodha',2), ('broker','Upstox','Upstox',3),
  -- OverrideSection
  ('overrideSection','indicator','Indicator',1), ('overrideSection','stopLoss','Stop Loss',2),
  ('overrideSection','profitTarget','Profit Target',3), ('overrideSection','trailing','Trailing',4),
  ('overrideSection','scaleOut','Scale Out',5), ('overrideSection','exitBehaviour','Exit Behaviour',6),
  ('overrideSection','rr','R:R',7);
```

---

## Backend — GET /api/enums

**File:** `backend/src/routes/enumsRoute.ts` (CREATE)

Returns all active enum values grouped by domain, sorted by `sort_order`.
This is the ONLY place in the codebase that supplies option lists to the UI.

```ts
// GET /api/enums
// Response shape: Record<string, EnumOption[]>
// EnumOption: { value: string; label: string }

export interface EnumOption {
  value: string
  label: string
}

export type EnumsResponse = Record<string, EnumOption[]>

// Query:
//   SELECT domain, value, label
//   FROM enum_values
//   WHERE is_active = TRUE
//   ORDER BY domain, sort_order ASC
//
// Group results by domain key → return as JSON object.
// Cache-Control: public, max-age=300  (5-minute browser cache — values rarely change)
```

Example response:
```json
{
  "timeframe":     [{ "value": "1m", "label": "1 min" }, { "value": "5m", "label": "5 min" }, ...],
  "broker":        [{ "value": "MStock", "label": "MStock" }, ...],
  "indicatorType": [{ "value": "EMA", "label": "EMA" }, ...]
}
```

---

## Frontend — EnumsContext + useEnums hook

**File:** `frontend/src/context/EnumsContext.tsx` (CREATE)

Fetches `/api/enums` once on app load. Provides the result via context.
All UI dropdowns, chip lists, and filter selectors consume this — never `ENUM_VALUES` directly.

```ts
import { ENUM_VALUES } from '../types/strategy'   // used as FALLBACK only if fetch fails
import { validateEnumResponse } from '../types/strategy'

interface EnumOption { value: string; label: string }
type EnumsMap = Record<string, EnumOption[]>

interface EnumsContextValue {
  enums: EnumsMap        // live data from DB via /api/enums
  loading: boolean
  error: string | null
}

// On mount: fetch('/api/enums')
//   → on success: validate response with validateEnumResponse(), store in state
//   → on failure: log warning, fall back to ENUM_VALUES converted to EnumOption[]
//                 (fallback ensures UI never breaks on network error)
//
// Cache: store in module-level variable so hot-reload doesn't re-fetch

export const EnumsProvider: React.FC<{ children: React.ReactNode }>
export const useEnums: () => EnumsContextValue
```

Usage in any component:
```tsx
// CORRECT — runtime values from DB
const { enums } = useEnums()
<Select options={enums.timeframe} />           // [{ value:'1m', label:'1 min' }, ...]

// FORBIDDEN — never do this in a component
<Select options={['1m', '3m', '5m']} />        // hardcoded strings
<Select options={ENUM_VALUES.timeframe} />     // bypasses DB, violates invariant 6
```

**Wrap app root:**
```tsx
// frontend/src/main.tsx or App.tsx
<EnumsProvider>
  <App />
</EnumsProvider>
```

---

## Frontend — EnumValidator utility

**File:** `frontend/src/types/strategy.ts` — add at bottom

`ENUM_VALUES` is now a **validator and offline fallback** only. It is never imported by UI components directly.

```ts
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
// Unknown values are kept in the UI list (so new DB rows work without a frontend deploy)
// but warned so devs know to add the corresponding enum member in the next release.
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
```

---

### Shared TypeScript interfaces
Create `frontend/src/types/strategy.ts`. All components import from here. No `any`, no implicit undefined.

**Rule:** interfaces use enum types. UI components use `useEnums()` for runtime option lists.
`ENUM_VALUES` is imported only by `EnumsContext` (fallback) and unit tests.

```ts
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

// ENUM_VALUES and validateEnumResponse defined below interfaces (see EnumValidator section above)

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
```

---

### API endpoints reference

| Method | Path | Used in |
|---|---|---|
| GET | `/api/enums` | EnumsContext — fetched once on app load |
| GET | `/api/strategies` | Task 1 sidebar list |
| POST | `/api/strategies` | Task 2 create |
| PUT | `/api/strategies/{id}` | Task 2 edit |
| DELETE | `/api/strategies/{id}` | Task 1 delete |
| GET | `/api/strategies/{id}/scenarios` | Task 3, 6 |
| POST | `/api/strategies/{id}/scenarios` | Task 3 create |
| PUT | `/api/strategies/{id}/scenarios/{sid}` | Task 3 edit |
| DELETE | `/api/strategies/{id}/scenarios/{sid}` | Task 3 delete |
| POST | `/api/strategies/{id}/scenarios/{sid}/run` | Task 3 run backtest |
| GET | `/api/strategies/{id}/deployments` | Task 4 |
| POST | `/api/strategies/{id}/deployments` | Task 4 create |
| DELETE | `/api/strategies/{id}/deployments/{did}` | Task 4 delete |
| GET | `/api/strategies/{id}/runs` | Task 5, 6 compare/results |

Mock shape for missing endpoints must exactly match the interfaces in `strategy.ts` above.

---

### Loading and error states (required on ALL data-fetching components)
- Show `<SkeletonRow />` (or equivalent) while fetching — mirror the real layout shape
- On error: inline `<ErrorMessage text="Failed to load. Retry?" onRetry={refetch} />` — no raw `alert()`
- Empty state: descriptive message + primary action button — never just blank space

---

## Screen architecture

**Strategy creation/edit is a full-page form** — not a 520px drawer. The drawer format is too
narrow for the rule builder and indicator table. Strategy creation opens at `/strategies/new`
or replaces the centre panel entirely.

**Scenario creation/edit is a right drawer** — 520px. Scenario only overrides parameters;
the form is manageable in a drawer.

**Deployment creation is a right drawer** — 520px. Symbol, timeframe, broker, capital, schedule.

```
Route: /strategies
├── Left sidebar (240px)             Strategy list + search + New button
└── Centre panel (flex-1)
    ├── Strategy header              name · TF · instruments · style · status · actions
    └── Tabs
        ├── Definition               Full-page 3-sub-tab form (see Task 2)
        ├── Scenarios                Table + right drawer (see Task 3)
        ├── Results                  Table + filters (see Task 6)
        ├── Compare                  Split-pane research view (see Task 5)
        └── Deployments              Table + right drawer (see Task 4)
```

---

## Task 1 — StrategiesPage layout

**File:** `frontend/src/pages/StrategiesPage.tsx` — MODIFY

**Left sidebar:**
- Search input at top
- `+ New Strategy` button → navigates to `/strategies/new` (or opens full-panel create mode)
- Strategy cards: name · primaryTimeframe · first instrument · tradingStyle badge · status chip

**Centre panel — 5 tabs:**
`Definition | Scenarios | Results | Compare | Deployments` — default tab: `Scenarios`

**Drawer container:**
One shared `<RightDrawer isOpen={bool} title={string} onClose={fn}>` component, 520px width,
`position: fixed`, right 0, top `NAV_HEIGHT`px, full height, `z-index: 100`.
Scenario drawer and Deployment drawer render inside this container.

---

## Task 2 — Strategy Definition (full-page form, 3 sub-tabs)

**File:** `frontend/src/pages/StrategyDefinitionPage.tsx` (CREATE)
Also used inline as a full-panel replace when editing from the Definition tab.

Props: `{ strategyId?: string }` (absent = create mode)

All dropdowns use `useEnums()` — not `ENUM_VALUES`.

### Sub-tab 1 — Core & Indicators

**Left column — Basic Details block**
- Strategy Name * — text input
- Primary Timeframe * — dropdown: `enums.timeframe`
- Instruments * — multi-value tag input (e.g. `NSE:AXISBANK`)
- Trading Style * — dropdown: `enums.tradingStyle`
- Description — textarea (optional)

**Left column — Exit Behaviour block** (mandatory; user must enable at least one)
```
☑ Exit at end of session
☑ Exit after [__] bars
☑ Exit at stop/target only
Combine rules with: [enums.exitCombineLogic ▾]

Session filters:
Tradable days: enums.dayOfWeek checkboxes
Session start: [09:15]   Session end: [15:20]
```

**Right column — Indicators block**
Table: `Indicator | TF | Role | Base Params | Allowed Ranges | Actions`
Button: `+ Add Indicator` → opens `<IndicatorModal>` (see below)

**Right column — Risk Controls block** (all mandatory fields marked *)
- Max risk per trade % * — number input (0.01–10.00, step 0.01)
- Max trades per day * — integer input

**Right column — Kill-switch block** (optional; toggle to show)
- Daily loss limit (R) — number input
- Max intraday DD % — number input
- Cooldown after breach (bars) — integer input

### Sub-tab 2 — Rules

Two-column layout: Long side (left) | Short side (right)

For each of `longEntry`, `shortEntry`, `longExit`, `shortExit`:
- Enable toggle
- Group operator selector: `enums.exitCombineLogic`
- List of RuleGroups, each collapsed by default showing: `label · operator · N conditions`
- Expand group → show conditions list

Each RuleGroup:
- Label input (editable, e.g. "Long Entry Group 1")
- Group logic: `enums.exitCombineLogic` toggle
- Conditions list:
  - Left operand: `[Indicator ▾] [field ▾]` or `[Window ▾]`
  - Operator: `enums.conditionOp` dropdown
  - Right operand: `[Value input]` or `[Indicator ▾] [field ▾]` or `[Window ▾]`
  - Delete condition button
- Buttons: `+ Condition` | `Duplicate group` | `Delete group`

WindowExpression inline editor (shown when operand = Window):
- Source indicator selector (from strategy's indicators)
- Lookback bars input
- Aggregation type: `enums.aggregationType` dropdown
- Optional multiplier input

### Sub-tab 3 — Risk & Regime

**Stops & Targets block**
Stop Loss:
- Type: `enums.stopType` dropdown
- Base value input
- Allowed range: min / max inputs

Profit Target:
- Same pattern as SL
- Derive from SL via R:R — checkbox; if checked, show `Target = SL × rrMin`

Risk-to-Reward:
- rrMin input
- rrMax input

**Trailing & Scale-out block**
- Enable trailing stop — checkbox
  - When enabled: `enums.trailingType` dropdown, params (dynamic per type)
  - Start condition: `enums.startCondition` dropdown
  - Start condition value input (shown for non-Immediately)
- Partial exits table:
  - Columns: `Trigger R | % to Close | Move Stop To (enums.moveStopTo) | Delete`
  - `+ Add partial exit` button

**Regime filters block** (optional; hidden behind `Enable Regime Filtering` toggle)
- Regime definitions: label + condition builder (same condition UI as Rules tab)
- Allowed regimes: multi-select of defined regime labels

### IndicatorModal
Props: `{ indicatorId?: string; strategyIndicators: IndicatorConfig[]; onSave: (i: IndicatorConfig) => void }`

Step 1: Type (`enums.indicatorType`) + Timeframe (`enums.timeframe`) + Role (`enums.indicatorRole`)
Step 2: Configure baseParams (fields rendered dynamically per type) + allowedParamRanges (min/max per numeric param)

No hard-coded TF restrictions — all `enums.timeframe` options are valid.

### Page footer
`Save Strategy` | `Save & Go to Scenarios`
Validation: name required, tradingStyle required, at least one exit behaviour rule enabled,
riskControls.maxRiskPerTradePercent > 0, riskControls.maxTradesPerDay > 0.

---

## Task 3 — Scenarios tab + ScenarioDrawer

**File:** `frontend/src/components/strategies/ScenariosTab.tsx` (CREATE)

Props: `{ strategy: Strategy }`

**Scenarios table columns:**
`Name | Capital | Backtest Range | Overrides Summary | Return | DD | PF | Fwd Status | Actions`

- Overrides Summary cell: short human-readable text e.g. `EMA(15m) 20→10; ATR mult 2→1.5; R:R 2→3; Exit: 30 bars`
  Truncate at 80 chars, show full on hover tooltip.
- Return/DD/PF: `F.mono`, right-aligned, `C.green` if positive return / `C.red` if negative
- Status chips: use `ScenarioStatus` enum values
- Actions: `Run` | `Edit` | `→ Fwd Test` | `Delete`

**File:** `frontend/src/components/strategies/ScenarioDrawer.tsx` (CREATE)

Props: `{ strategy: Strategy; scenarioId?: string; onClose: () => void }`

**Drawer layout (top to bottom):**

```
INHERITED FROM: {strategy.name}
Indicators, rules, and exit structure are fixed. Only parameter values may be overridden
within the ranges set by the strategy.
```
Style: `C.textMuted`, italic, `10px`

Section: **Inherited Indicators** (read-only)
Table: `Indicator | TF | Role | Base Params`
No edit controls. Label: `INHERITED — READ ONLY`

Section: **Parameter Overrides**
Grouped by `OverrideSection` enum value (Indicator groups first, then SL / PT / Trailing / ScaleOut / ExitBehaviour / RR):

For each group heading e.g. `EMA (15m) — EntryFilter`:
- Rows: `Param label | Base value (read-only, C.textMuted) | Override input | Allowed range hint`
- Override input only shown/active when user explicitly checks the override checkbox
- Input validated against `allowedParamRanges` — show error if out of range
- WindowExpression params (lookbackBars, multiplier, percentile) shown in a `Window Expressions` sub-group

Section: **Run Configuration**
- Capital (₹) — number input
- Broker account — `enums.broker` dropdown
- Backtest from / to — date inputs

Footer: `Cancel` | `Save Scenario` | `Save & Run Backtest`

---

## Task 4 — Deployments tab + DeploymentDrawer

**File:** `frontend/src/components/strategies/DeploymentsTab.tsx` (CREATE)

Props: `{ strategy: Strategy }`

Deployment drawer fields (all use `useEnums()`):
- Deployment Name *
- Scenario * — dropdown of strategy's scenarios
- Symbol * — text input (`NSE:AXISBANK`)
- Timeframe * — `enums.timeframe` dropdown
- Mode * — `enums.deploymentMode` dropdown
- Broker * — `enums.broker` dropdown
- Allocated Capital (₹) * — number input
- Schedule — `enums.dayOfWeek` checkboxes + start/end time (HH:MM)

Table columns: `Name | Scenario | Symbol | TF | Mode | Broker | Capital | Status | Actions`
Actions: `Run Backtest` | `→ Fwd Test` | `Delete`

---

## Task 5 — Compare tab

**File:** `frontend/src/components/strategies/CompareTab.tsx` (CREATE)

Props: `{ strategyId: string }`

**Three comparison modes:**
1. Backtest vs Forward for the same Scenario
2. Scenario A vs Scenario B (same Strategy)
3. Strategy across different instruments / timeframes (different Deployments)

**Layout:**
Left pane (240px):
- Mode selector: radio group (modes 1–3 above)
- Depending on mode: Scenario picker(s) + RunResult picker(s)
- `[+ Add column]` button (max 5 columns)
- Toggle: `RunMode.Backtest | RunMode.ForwardTest | Both`

Right pane (flex-1):
- **Metric cards** (top section): pairs with Δ delta — Return, DD, Sharpe, Win%, PF, Trades, Expectancy, Realised R:R, Degradation Ratio
- **Charts** (middle section):
  - Equity curve(s) — one line per selected run, `C.blue` / `C.green` / `C.amber`
  - Drawdown curve(s) — area chart, `C.redBg` fill
- **Notes** (bottom section): freetext textarea, `C.border` border, `12px` font

Metric table rules:
- `btToFtDegradationRatio` undefined → render `'—'`, no bg color
- `btToFtDegradationRatio` defined AND `< 0.7` → cell bg `C.redBg`, color `C.red`
- Best value per row → cell bg `C.greenBg`
- All values: `F.mono`, `tabular-nums`, right-aligned

| Metric | Key | Format |
|---|---|---|
| Return | `returnPct` | `+12.4%` |
| Max DD | `maxDrawdownPct` | `-8.2%` |
| Sharpe | `sharpe` | `1.82` |
| Win % | `winRate` | `54%` |
| PF | `profitFactor` | `1.9` |
| Trades | `tradeCount` | `142` |
| Avg R | `avgRPerTrade` | `0.42 R` |
| Expectancy | `expectancy` | `₹840` |
| Realised R:R | `avgRealisedRR` | `2.1` |
| BT→FT | `btToFtDegradationRatio` | `0.92` or `—` |

---

## Task 6 — Results tab

**File:** `frontend/src/components/strategies/ResultsTab.tsx` (CREATE)

Props: `{ strategyId: string }`

Filters bar:
- Scenario selector — multi-select dropdown
- Mode toggle: `RunMode.Backtest | RunMode.ForwardTest | All`
- Date range: from/to inputs

Table columns: `Scenario | Mode | Date Range | Return | Max DD | Sharpe | Win% | PF | Trades | Details`

---

## File checklist

```
DB migration:
  enum_values table + seed                              CREATE (see DB section above)

Backend:
  backend/src/routes/enumsRoute.ts                      CREATE — GET /api/enums

Frontend:
  frontend/src/types/strategy.ts                        CREATE — enums + ENUM_VALUES (fallback) + validateEnumResponse + interfaces
  frontend/src/context/EnumsContext.tsx                 CREATE — EnumsProvider + useEnums hook
  frontend/src/pages/StrategyDefinitionPage.tsx         CREATE — full-page 3-sub-tab form
  frontend/src/pages/StrategiesPage.tsx                 MODIFY — 5-tab layout, new sidebar card
  frontend/src/components/strategies/
    ScenariosTab.tsx                                    CREATE
    ScenarioDrawer.tsx                                  CREATE
    DeploymentsTab.tsx                                  CREATE
    ResultsTab.tsx                                      CREATE
    CompareTab.tsx                                      CREATE
    IndicatorModal.tsx                                  CREATE
    RuleGroupEditor.tsx                                 CREATE
    ConditionRow.tsx                                    CREATE
    WindowExpressionEditor.tsx                          CREATE
  frontend/src/components/ui/RightDrawer.tsx            CREATE if not exists
```

---

## Constraints
- No raw hex anywhere — `C.*` tokens only (AP-020)
- **UI dropdowns use `useEnums()` only** — never `ENUM_VALUES`, never hardcoded string arrays
- **`ENUM_VALUES` imported only by** `EnumsContext` (fallback) and unit tests — nowhere else
- **No string literals for enum fields** — always use enum member (e.g. `Timeframe.M5`, not `'5m'`)
- Scenario drawer only — no inline form expansion for scenarios (AP-022)
- Strategy definition is full-page — NOT a 520px drawer
- No `DateTime.Now` on backend; frontend receives ISO strings only
- No `"New Strategy Instance"` modal — replaced by Deployment drawer (Task 4)
- No indicator add/remove/role-change in Scenario drawer — param list structurally locked
- Table padding `TABLE_CELL`, font `12px`
- Metric values: `F.mono`, `tabular-nums`, right-aligned
- TypeScript strict — no `any`, no implicit undefined
- `cd frontend && npx tsc --noEmit` must pass with zero errors
- `npm run dev` must start with zero console errors

---

## Definition of done
- [ ] `enum_values` DB table created and seeded
- [ ] `GET /api/enums` returns all active values grouped by domain
- [ ] `EnumsContext` fetches `/api/enums` on mount; falls back to `ENUM_VALUES` on error
- [ ] `frontend/src/types/strategy.ts` created with all enums, `ENUM_VALUES` (fallback only), `validateEnumResponse`, and interfaces
- [ ] Every UI dropdown uses `useEnums()` — grep for `ENUM_VALUES` in component files returns zero results
- [ ] Strategy creation: Name + primaryTimeframe + instruments + tradingStyle + indicators + rules + exits + risk (full form, 3 sub-tabs)
- [ ] Scenario creation: inherited indicators read-only, parameter overrides only within allowedParamRanges, run config
- [ ] Deployment creation: symbol + timeframe + broker + capital + schedule + scenario link
- [ ] Results tab: table with filters (scenario, mode, date range)
- [ ] Compare tab: 3 comparison modes, metric table + delta column + equity/drawdown charts
- [ ] All 5 tabs render without console errors
- [ ] Status chips consistent across all components using `ScenarioStatus` / `DeploymentStatus` enum values
- [ ] Zero raw hex in any new file
- [ ] `npx tsc --noEmit` passes
- [ ] `npm run dev` starts clean

**After implementation confirmed:** replace this entry with a one-line stub:
`## PROMPT-001 — DONE — Strategy & Scenario Full Domain Model`
