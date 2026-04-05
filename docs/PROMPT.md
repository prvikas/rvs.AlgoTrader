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
- Five UI screens with precise layout requirements

**Core invariants (never violate):**
1. Strategy defines structure: indicators, rules, exit logic, risk limits. It does NOT hold symbol, broker, capital.
2. Scenario inherits everything structural. It can ONLY override numeric parameters within allowedParamRanges.
3. Scenario cannot add, remove, or replace indicators. Cannot change rule structure or indicator roles/timeframes.
4. Deployment binds a Scenario to an execution context: symbol, timeframe, broker, capital, schedule.
5. RunResult ties a Scenario to a specific backtest or forward-test execution with full metrics.
6. **All primitive domain values are TypeScript enums** — single source of truth in `strategy.ts`.
   UI dropdowns, chips, and labels must derive their option lists from `ENUM_VALUES.*`, never hardcode strings.

---

### Scope
Frontend only (`frontend/src/`). Do not change DB schema or API contracts.
Missing endpoints → add `// TODO: API` comment + use mock data shaped to the DTOs below.

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

### Shared TypeScript interfaces
Create `frontend/src/types/strategy.ts`. All components import from here. No `any`, no implicit undefined.

**Single source of truth rule:** Every domain value that appears in a dropdown, chip, badge, or
table cell MUST be an enum member from this file. UI components call `Object.values(EnumName)` or
use the `ENUM_VALUES` map below to build their option lists. No component may hardcode `['EMA', 'SMA', ...]`.

```ts
// ─── Enums — single source of truth for ALL domain primitives ──────────────────
// Rule: interfaces use enum types; UI derives option arrays from ENUM_VALUES

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
  // Trend
  EMA              = 'EMA',
  SMA              = 'SMA',
  HullMA           = 'HullMA',
  DonchianChannel  = 'DonchianChannel',
  SuperTrend       = 'SuperTrend',
  // Momentum / Oscillator
  RSI              = 'RSI',
  CCI              = 'CCI',
  Stochastics      = 'Stochastics',
  MACD             = 'MACD',
  // Volatility
  ATR              = 'ATR',
  BollingerBands   = 'BollingerBands',
  RangePercentile  = 'RangePercentile',
  // Volume / Flow
  Volume           = 'Volume',
  VolumeSpike      = 'VolumeSpike',
  VWAP             = 'VWAP',
  // Price Action / Structure
  SwingHighLow     = 'SwingHighLow',
  InsideBar        = 'InsideBar',
  Engulfing        = 'Engulfing',
  PrevHighLowBreak = 'PrevHighLowBreak',
  // Time / Session
  SessionFilter    = 'SessionFilter',
  DayOfWeekFilter  = 'DayOfWeekFilter',
  // Regime helpers
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
  Gt            = '>',
  Lt            = '<',
  Gte           = '>=',
  Lte           = '<=',
  Eq            = '==',
  Neq           = '!=',
  CrossesAbove  = 'crossesAbove',
  CrossesBelow  = 'crossesBelow',
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
  FixedPoints      = 'FixedPoints',
  ATRMultiple      = 'ATRMultiple',
  MovingAverage    = 'MovingAverage',
  DonchianChannel  = 'DonchianChannel',
}

export enum ScenarioStatus {
  Draft          = 'Draft',
  Running        = 'Running',
  Backtested     = 'Backtested',
  FwdTesting     = 'Fwd Testing',
  LiveCandidate  = 'Live Candidate',
  Live           = 'Live',
  Archived       = 'Archived',
}

export enum StrategyStatus {
  Draft      = 'Draft',
  Backtested = 'Backtested',
  FwdTesting = 'Fwd Testing',
  Live       = 'Live',
  Archived   = 'Archived',
}

export enum DeploymentStatus {
  Draft          = 'Draft',
  Running        = 'Running',
  Backtested     = 'Backtested',
  FwdTesting     = 'Fwd Testing',
  LiveCandidate  = 'Live Candidate',
  Live           = 'Live',
  Archived       = 'Archived',
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
  Breakeven      = 'breakeven',
  PreviousLevel  = 'previousLevel',
  Custom         = 'custom',
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
  MStock = 'MStock',
  Zerodha = 'Zerodha',
  Upstox = 'Upstox',
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

// ─── ENUM_VALUES — UI option arrays derived from enums (do NOT hardcode elsewhere) ───────
// Usage: Object.values(ENUM_VALUES.timeframe) gives ['1m','3m','5m',...]
// Every dropdown, chip list, and filter in the UI must use this map.

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

// ─── Indicators ──────────────────────────────────────────────────────────────

export interface ParamRange {
  min: number
  max: number
}

export interface IndicatorConfig {
  id: string                                          // unique within strategy, e.g. "ema_15m"
  type: IndicatorType
  timeframe: Timeframe                               // always explicit — never inherited
  role: IndicatorRole
  baseParams: Record<string, number | string | boolean>
  allowedParamRanges: Record<string, ParamRange | string[]> // numeric → ParamRange, categorical → string[]
}

// ─── Rule builder ─────────────────────────────────────────────────────────────

export interface WindowExpression {
  sourceIndicatorId: string                          // e.g. "volume_5m", "close_5m"
  lookbackBars: number
  aggregationType: AggregationType
  multiplier?: number                                // optional: currentVal > multiplier × aggregated
}

export type ConditionOperand =
  | { kind: 'indicator'; indicatorId: string; field?: string }   // field: close, high, low, hl2 …
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
  label: string                                      // e.g. "Long Entry Group 1"
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
  exitAfterNBars: number | null                      // null = disabled
  exitAtStopOrTargetOnly: boolean
  combineLogic: ExitCombineLogic
  // tradableDays: backtest-level day filter only.
  // For live/forward deployments, Deployment.schedule.days takes precedence.
  tradableDays: DayOfWeek[]
  sessionStart?: string                              // "09:15" IST
  sessionEnd?: string                                // "15:20" IST
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
  customLevel?: number                               // only when moveStopTo === MoveStopTo.Custom
}

export interface TrailingConfig {
  enabled: boolean
  trailingType: TrailingType
  trailingParams: Record<string, number>             // e.g. { atrPeriod: 14, atrMult: 2 }
  startCondition: StartCondition
  startConditionValue?: number                       // k for AfterKR, amount for AfterAbsoluteProfit
  partialExits: PartialExit[]
}

export interface RRConfig {
  rrMin: number
  rrMax: number
  deriveTargetFromSL: boolean                        // if true, PT = SL × rrMin
}

// ─── Risk controls ────────────────────────────────────────────────────────────

export interface RiskControls {
  maxRiskPerTradePercent: number                     // 0.25–2.0, mandatory
  maxTradesPerDay: number                            // mandatory
  maxOpenPositions?: number
}

export interface KillSwitch {
  dailyLossLimitR?: number                           // stop new entries if loss ≥ X R
  maxIntradayDrawdownPercent?: number                // pause if equity DD from day high ≥ Y%
  cooldownBarsAfterHit?: number                      // skip entries for Z bars after breach
}

// ─── Regime ───────────────────────────────────────────────────────────────────

export interface RegimeDefinition {
  id: string
  label: string                                      // e.g. "HighVol", "Trending"
  conditions: Condition[]
}

// ─── Strategy (root entity) ───────────────────────────────────────────────────

export interface Strategy {
  id: string
  name: string
  description?: string
  primaryTimeframe: Timeframe
  instruments: string[]                              // e.g. ["NSE:AXISBANK", "NSE:NIFTY"]
  tradingStyle: TradingStyle
  status: StrategyStatus
  indicators: IndicatorConfig[]
  longEntry: EntryExitBlock
  shortEntry: EntryExitBlock
  longExit: EntryExitBlock
  shortExit: EntryExitBlock
  exitBehaviour: ExitBehaviour                      // mandatory; at least one rule must be enabled
  stopLoss: StopTargetConfig
  profitTarget: StopTargetConfig
  rrConfig: RRConfig
  trailing: TrailingConfig
  riskControls: RiskControls                         // mandatory
  killSwitch?: KillSwitch
  regimeDefinitions?: RegimeDefinition[]
  allowedRegimes?: string[]                          // regime ids
  createdAt: string                                  // ISO string — never Date object
  updatedAt: string
}

// ─── Scenario ─────────────────────────────────────────────────────────────────

export interface ParameterOverride {
  section: OverrideSection
  indicatorId?: string                               // required when section === OverrideSection.Indicator
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
  symbol: string                                     // e.g. "NSE:AXISBANK"
  timeframe: Timeframe
  mode: DeploymentMode
  broker: Broker
  allocatedCapital: number
  // schedule.days overrides ExitBehaviour.tradableDays for live/forward deployments.
  // ExitBehaviour.tradableDays applies only during backtesting.
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
  // undefined when no forward run has been executed yet.
  // UI rule: if undefined → render '—' with no color applied.
  // Only apply C.redBg when value is present AND < 0.7.
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
  completedAt: string                                // ISO string
}
```

---

### API endpoints reference

| Method | Path | Used in |
|---|---|---|
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

### Sub-tab 1 — Core & Indicators

**Left column — Basic Details block**
- Strategy Name * — text input
- Primary Timeframe * — dropdown: `ENUM_VALUES.timeframe`
- Instruments * — multi-value tag input (e.g. `NSE:AXISBANK`)
- Trading Style * — dropdown: `ENUM_VALUES.tradingStyle`
- Description — textarea (optional)

**Left column — Exit Behaviour block** (mandatory; user must enable at least one)
```
☑ Exit at end of session
☑ Exit after [__] bars
☑ Exit at stop/target only
Combine rules with: [ENUM_VALUES.exitCombineLogic ▾]

Session filters:
Tradable days: ENUM_VALUES.dayOfWeek checkboxes
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
- Group operator selector: `ENUM_VALUES.exitCombineLogic`
- List of RuleGroups, each collapsed by default showing: `label · operator · N conditions`
- Expand group → show conditions list

Each RuleGroup:
- Label input (editable, e.g. "Long Entry Group 1")
- Group logic: `ENUM_VALUES.exitCombineLogic` toggle
- Conditions list:
  - Left operand: `[Indicator ▾] [field ▾]` or `[Window ▾]`
  - Operator: `ENUM_VALUES.conditionOp` dropdown
  - Right operand: `[Value input]` or `[Indicator ▾] [field ▾]` or `[Window ▾]`
  - Delete condition button
- Buttons: `+ Condition` | `Duplicate group` | `Delete group`

WindowExpression inline editor (shown when operand = Window):
- Source indicator selector (from strategy's indicators)
- Lookback bars input
- Aggregation type: `ENUM_VALUES.aggregationType` dropdown
- Optional multiplier input

### Sub-tab 3 — Risk & Regime

**Stops & Targets block**
Stop Loss:
- Type: `ENUM_VALUES.stopType` dropdown
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
  - When enabled: `ENUM_VALUES.trailingType` dropdown, params (dynamic per type)
  - Start condition: `ENUM_VALUES.startCondition` dropdown
  - Start condition value input (shown for non-Immediately)
- Partial exits table:
  - Columns: `Trigger R | % to Close | Move Stop To (ENUM_VALUES.moveStopTo) | Delete`
  - `+ Add partial exit` button

**Regime filters block** (optional; hidden behind `Enable Regime Filtering` toggle)
- Regime definitions: label + condition builder (same condition UI as Rules tab)
- Allowed regimes: multi-select of defined regime labels

### IndicatorModal
Props: `{ indicatorId?: string; strategyIndicators: IndicatorConfig[]; onSave: (i: IndicatorConfig) => void }`

Step 1: Type (`ENUM_VALUES.indicatorType`) + Timeframe (`ENUM_VALUES.timeframe`) + Role (`ENUM_VALUES.indicatorRole`)
Step 2: Configure baseParams (fields rendered dynamically per type) + allowedParamRanges (min/max per numeric param)

No hard-coded TF restrictions — all `ENUM_VALUES.timeframe` options are valid.

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
- Broker account — `ENUM_VALUES.broker` dropdown
- Backtest from / to — date inputs

Footer: `Cancel` | `Save Scenario` | `Save & Run Backtest`

---

## Task 4 — Deployments tab + DeploymentDrawer

**File:** `frontend/src/components/strategies/DeploymentsTab.tsx` (CREATE)

Props: `{ strategy: Strategy }`

Deployment drawer fields:
- Deployment Name *
- Scenario * — dropdown of strategy's scenarios
- Symbol * — text input (`NSE:AXISBANK`)
- Timeframe * — `ENUM_VALUES.timeframe` dropdown
- Mode * — `ENUM_VALUES.deploymentMode` dropdown
- Broker * — `ENUM_VALUES.broker` dropdown
- Allocated Capital (₹) * — number input
- Schedule — `ENUM_VALUES.dayOfWeek` checkboxes + start/end time (HH:MM)

Table columns: `Name | Scenario | Symbol | TF | Mode | Broker | Capital | Status | Actions`
Actions: `Run Backtest` | `→ Fwd Test` | `Delete`

Existing instances (old model): map `instanceSymbol → symbol`, `instanceTimeframe → timeframe`.
Show `—` in Scenario column if `scenarioId` missing.

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
  - Use lightweight charting library (already in project) or recharts
- **Notes** (bottom section): freetext textarea, `C.border` border, `12px` font

Metric table rules:
- Rows fixed (metrics), columns = selected runs
- Delta column (Δ) shown only when ≥ 2 columns
- `btToFtDegradationRatio` rendering:
  - `undefined` (no forward run) → render `'—'`, no bg color
  - defined AND `< 0.7` → cell bg `C.redBg`, color `C.red`
  - defined AND `>= 0.7` → normal cell (best-value highlight still applies via `C.greenBg`)
- Best value per row → cell bg `C.greenBg`
- All values: `F.mono`, `tabular-nums`, right-aligned

Metric table columns:

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

Empty state: "No scenarios have been run yet. Run a backtest to start comparing."

---

## Task 6 — Results tab

**File:** `frontend/src/components/strategies/ResultsTab.tsx` (CREATE)

Props: `{ strategyId: string }`

Filters bar:
- Scenario selector — multi-select dropdown
- Mode toggle: `RunMode.Backtest | RunMode.ForwardTest | All`
- Date range: from/to inputs

Table columns: `Scenario | Mode | Date Range | Return | Max DD | Sharpe | Win% | PF | Trades | Details`

- Details link: `View →` opens run detail panel or route
- Return: green if positive, red if negative
- All numeric metrics: `F.mono`, right-aligned, `tabular-nums`

---

## File checklist

```
frontend/src/types/strategy.ts                          CREATE — all enums + ENUM_VALUES + interfaces
frontend/src/pages/StrategyDefinitionPage.tsx           CREATE — full-page 3-sub-tab form
frontend/src/pages/StrategiesPage.tsx                   MODIFY — 5-tab layout, new sidebar card
frontend/src/components/strategies/
  ScenariosTab.tsx                                      CREATE
  ScenarioDrawer.tsx                                    CREATE
  DeploymentsTab.tsx                                    CREATE
  ResultsTab.tsx                                        CREATE
  CompareTab.tsx                                        CREATE
  IndicatorModal.tsx                                    CREATE
  RuleGroupEditor.tsx                                   CREATE — reusable rule group UI
  ConditionRow.tsx                                      CREATE — single condition row
  WindowExpressionEditor.tsx                            CREATE — inline window expr editor
frontend/src/components/ui/RightDrawer.tsx              CREATE if not exists — 520px fixed drawer
```

Note: `frontend/src/components/strategies/` does not exist — Claude must create the directory.

---

## Constraints
- No raw hex anywhere — `C.*` tokens only (AP-020)
- **No hardcoded string arrays in UI** — all dropdown option lists must use `ENUM_VALUES.*` (single source of truth)
- **No string literals for enum fields** — always use enum member (e.g. `Timeframe.M5`, not `'5m'`)
- Scenario drawer only — no inline form expansion for scenarios (AP-022)
- Strategy definition is full-page — NOT a 520px drawer
- No `DateTime.Now` on backend; frontend receives ISO strings only
- No `"New Strategy Instance"` modal — replaced by Deployment drawer (Task 4)
- No indicator add/remove/role-change in Scenario drawer — param list structurally locked
- Scenario cannot change rule structure — no adding/removing conditions or groups
- Table padding `TABLE_CELL`, font `12px`
- Metric values: `F.mono`, `tabular-nums`, right-aligned
- TypeScript strict — no `any`, no implicit undefined
- `cd frontend && npx tsc --noEmit` must pass with zero errors
- `npm run dev` must start with zero console errors

---

## Definition of done
- [ ] `frontend/src/types/strategy.ts` created with ALL enums, `ENUM_VALUES`, and interfaces
- [ ] Every UI dropdown/chip derives its options from `ENUM_VALUES.*` — zero hardcoded string arrays
- [ ] Strategy creation: Name + primaryTimeframe + instruments + tradingStyle + indicators + rules + exits + risk (full form, 3 sub-tabs)
- [ ] Scenario creation: inherited indicators read-only, parameter overrides only within allowedParamRanges, run config
- [ ] Deployment creation: symbol + timeframe + broker + capital + schedule + scenario link
- [ ] Results tab: table with filters (scenario, mode, date range)
- [ ] Compare tab: 3 comparison modes, metric table + delta column + equity/drawdown charts
- [ ] All 5 tabs render without console errors
- [ ] Status chips consistent across all components using `ScenarioStatus` / `DeploymentStatus` enum values
- [ ] IndicatorModal: type + TF + role from `ENUM_VALUES.*`, no TF restrictions
- [ ] RuleGroupEditor: condition builder with IndicatorRef, WindowExpression, numeric operands
- [ ] Zero raw hex in any new file
- [ ] `npx tsc --noEmit` passes
- [ ] `npm run dev` starts clean

**After implementation confirmed:** replace this entry with a one-line stub:
`## PROMPT-001 — DONE — Strategy & Scenario Full Domain Model`
