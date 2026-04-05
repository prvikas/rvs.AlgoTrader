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
- Full TypeScript interfaces for all entities
- Five UI screens with precise layout requirements

**Core invariants (never violate):**
1. Strategy defines structure: indicators, rules, exit logic, risk limits. It does NOT hold symbol, broker, capital.
2. Scenario inherits everything structural. It can ONLY override numeric parameters within allowedParamRanges.
3. Scenario cannot add, remove, or replace indicators. Cannot change rule structure or indicator roles/timeframes.
4. Deployment binds a Scenario to an execution context: symbol, timeframe, broker, capital, schedule.
5. RunResult ties a Scenario to a specific backtest or forward-test execution with full metrics.

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

### Canonical status chips (use EXACTLY these strings everywhere)

| Status | Color token | Notes |
|---|---|---|
| Draft | `C.textMuted` | default |
| Running | `C.amber` | pulse animation |
| Backtested | `C.blue` | |
| Fwd Testing | `C.blue` + italic | |
| Live Candidate | `C.green` + italic | |
| Live | `C.green` | |
| Archived | `C.textDim` | |

---

### Shared TypeScript interfaces
Create `frontend/src/types/strategy.ts`. All components import from here. No `any`, no implicit undefined.

```ts
// ─── Primitives ──────────────────────────────────────────────────────────────

export type Timeframe = '1m' | '3m' | '5m' | '15m' | '30m' | '1h' | '4h' | '1D'

export type TradingStyle = 'Scalping' | 'Intraday' | 'Swing' | 'Positional' | 'Custom'

export type IndicatorType =
  // Trend
  | 'EMA' | 'SMA' | 'HullMA' | 'DonchianChannel' | 'SuperTrend'
  // Momentum/Oscillator
  | 'RSI' | 'CCI' | 'Stochastics' | 'MACD'
  // Volatility
  | 'ATR' | 'BollingerBands' | 'RangePercentile'
  // Volume/Flow
  | 'Volume' | 'VolumeSpike' | 'VWAP'
  // Price Action / Structure
  | 'SwingHighLow' | 'InsideBar' | 'Engulfing' | 'PrevHighLowBreak'
  // Time/Session
  | 'SessionFilter' | 'DayOfWeekFilter'
  // Regime helpers
  | 'ADX' | 'ATRPercentile'

export type IndicatorRole =
  | 'EntryTrigger' | 'EntryFilter' | 'Exit'
  | 'StopLoss' | 'TakeProfit' | 'TrailingStop'
  | 'RiskModel' | 'RegimeFilter' | 'InfoOnly'

export type ConditionOperator =
  | '>' | '<' | '>=' | '<=' | '==' | '!='
  | 'crossesAbove' | 'crossesBelow'

export type AggregationType = 'avg' | 'max' | 'min' | 'percentile' | 'highest' | 'lowest'

export type StopType = 'FixedPoints' | 'ATRMultiple' | 'StructureLowHigh'

export type TrailingType = 'FixedPoints' | 'ATRMultiple' | 'MovingAverage' | 'DonchianChannel'

export type ScenarioStatus =
  | 'Draft' | 'Running' | 'Backtested' | 'Fwd Testing'
  | 'Live Candidate' | 'Live' | 'Archived'

// FIX-1: Added 'Archived' — was missing from StrategyStatus despite being in the canonical chip set
export type StrategyStatus = 'Draft' | 'Backtested' | 'Fwd Testing' | 'Live' | 'Archived'

// FIX-2: Dedicated DeploymentStatus — Deployment.status was incorrectly typed as StrategyStatus,
// which lacks 'Running' and 'Live Candidate'. Deployments can be in those states.
export type DeploymentStatus =
  | 'Draft' | 'Running' | 'Backtested' | 'Fwd Testing'
  | 'Live Candidate' | 'Live' | 'Archived'

export type RunMode = 'Backtest' | 'ForwardTest'

export type ExitCombineLogic = 'AND' | 'OR'

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
  logicalOperator: 'AND' | 'OR'
  conditions: Condition[]
}

export interface EntryExitBlock {
  enabled: boolean
  groupOperator: 'AND' | 'OR'                        // how multiple groups combine
  groups: RuleGroup[]
}

// ─── Exit behaviour ───────────────────────────────────────────────────────────

export interface ExitBehaviour {
  exitEndOfSession: boolean
  exitAfterNBars: number | null                      // null = disabled
  exitAtStopOrTargetOnly: boolean
  combineLogic: ExitCombineLogic                     // how the above three rules combine
  // FIX-4: tradableDays here is the backtest-level day filter.
  // For live/forward deployments, Deployment.schedule.days takes precedence and overrides this field.
  // During backtesting only, this field controls which days are simulated.
  tradableDays: ('Mon' | 'Tue' | 'Wed' | 'Thu' | 'Fri' | 'Sat' | 'Sun')[]
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
  moveStopTo: 'breakeven' | 'previousLevel' | 'custom'
  customLevel?: number
}

export interface TrailingConfig {
  enabled: boolean
  trailingType: TrailingType
  trailingParams: Record<string, number>             // e.g. { atrPeriod: 14, atrMult: 2 }
  startCondition: 'immediately' | 'afterKR' | 'afterAbsoluteProfit'
  startConditionValue?: number                       // k for afterKR, amount for afterAbsoluteProfit
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
  conditions: Condition[]                            // e.g. ATRPercentile > 70
}

// ─── Strategy (root entity) ───────────────────────────────────────────────────

export interface Strategy {
  id: string
  name: string
  description?: string
  primaryTimeframe: Timeframe                        // strategy's primary TF
  instruments: string[]                              // e.g. ["NSE:AXISBANK", "NSE:NIFTY"]
  tradingStyle: TradingStyle                         // mandatory; influences exit/holding defaults
  status: StrategyStatus
  // Indicator set — owned by strategy; scenarios cannot modify structure
  indicators: IndicatorConfig[]
  // Rule blocks
  longEntry: EntryExitBlock
  shortEntry: EntryExitBlock
  longExit: EntryExitBlock
  shortExit: EntryExitBlock
  // Exit behaviour — mandatory; at least one rule must be enabled
  exitBehaviour: ExitBehaviour
  // Stops / targets / trailing
  stopLoss: StopTargetConfig
  profitTarget: StopTargetConfig
  rrConfig: RRConfig
  trailing: TrailingConfig
  // Risk
  riskControls: RiskControls                         // mandatory
  killSwitch?: KillSwitch
  // Regime (optional)
  regimeDefinitions?: RegimeDefinition[]
  allowedRegimes?: string[]                          // regime ids
  // Meta
  createdAt: string                                  // ISO string — never Date object
  updatedAt: string
}

// ─── Scenario ─────────────────────────────────────────────────────────────────

export type OverrideSection =
  | 'indicator'
  | 'stopLoss'
  | 'profitTarget'
  | 'trailing'
  | 'scaleOut'
  | 'exitBehaviour'
  | 'rr'

export interface ParameterOverride {
  section: OverrideSection
  indicatorId?: string                               // required when section === 'indicator'
  paramKey: string
  baseValue: number | boolean
  overrideValue: number | boolean
}

export interface Scenario {
  id: string
  strategyId: string
  name: string
  description?: string
  // Execution context
  capital: number
  brokerAccount: string
  backtestRange: { from: string; to: string }
  // Overrides — only numeric/boolean params within allowedParamRanges
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
  mode: 'Backtest' | 'Forward Test' | 'Live'
  broker: 'MStock' | 'Zerodha' | 'Upstox'
  allocatedCapital: number
  // FIX-4: schedule.days overrides ExitBehaviour.tradableDays for live/forward deployments.
  // ExitBehaviour.tradableDays applies only during backtesting.
  schedule: {
    days: ('Mon' | 'Tue' | 'Wed' | 'Thu' | 'Fri' | 'Sat' | 'Sun')[]
    startTime?: string
    endTime?: string
  }
  // FIX-2: Changed from StrategyStatus to DeploymentStatus — deployments can be
  // 'Running' or 'Live Candidate' which were absent from StrategyStatus.
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
  // FIX-3: undefined when no forward run has been executed yet.
  // UI rule: if undefined → render '—' with no color applied (do NOT show red).
  // Only apply C.redBg when the value is present AND < 0.7.
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
- Primary Timeframe * — dropdown: all `Timeframe` values
- Instruments * — multi-value tag input (e.g. `NSE:AXISBANK`)
- Trading Style * — dropdown: `Scalping | Intraday | Swing | Positional | Custom`
- Description — textarea (optional)

**Left column — Exit Behaviour block** (mandatory; user must enable at least one)
```
☑ Exit at end of session
☑ Exit after [__] bars
☑ Exit at stop/target only
Combine rules with: [OR ▾]

Session filters:
Tradable days: [Mon] [Tue] [Wed] [Thu] [Fri] [ Sat] [ Sun]
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
- Group operator selector (how groups combine): `AND | OR`
- List of RuleGroups, each collapsed by default showing: `label · operator · N conditions`
- Expand group → show conditions list

Each RuleGroup:
- Label input (editable, e.g. "Long Entry Group 1")
- Group logic: `AND | OR` toggle
- Conditions list:
  - Left operand: `[Indicator ▾] [field ▾]` or `[Window ▾]`
  - Operator: dropdown of `ConditionOperator` values
  - Right operand: `[Value input]` or `[Indicator ▾] [field ▾]` or `[Window ▾]`
  - Delete condition button
- Buttons: `+ Condition` | `Duplicate group` | `Delete group`

WindowExpression inline editor (shown when operand = Window):
- Source indicator selector (from strategy's indicators)
- Lookback bars input
- Aggregation type dropdown
- Optional multiplier input

### Sub-tab 3 — Risk & Regime

**Stops & Targets block**
Stop Loss:
- Type: `FixedPoints | ATRMultiple | StructureLowHigh`
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
  - When enabled: trailing type dropdown, params (dynamic per type), start condition selector
  - Start condition: `Immediately | After price reaches k × SL | After absolute profit`
  - Start condition value input (shown for non-immediate)
- Partial exits table:
  - Columns: `Trigger R | % to Close | Move Stop To | Delete`
  - `+ Add partial exit` button

**Regime filters block** (optional; hidden behind `Enable Regime Filtering` toggle)
- Regime definitions: label + condition builder (same condition UI as Rules tab)
- Allowed regimes: multi-select of defined regime labels

### IndicatorModal
Props: `{ indicatorId?: string; strategyIndicators: IndicatorConfig[]; onSave: (i: IndicatorConfig) => void }`

Step 1: Pick type + timeframe + role
Step 2: Configure baseParams (fields rendered dynamically per type) + allowedParamRanges (min/max per numeric param)

Timeframe field has no hard-coded restrictions — any `Timeframe` value is valid regardless of strategy's primaryTimeframe.

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
- Status chips: use canonical set
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
Grouped by indicator, then by risk section (SL / PT / Trailing / ScaleOut / ExitBehaviour / RR):

For each group heading e.g. `EMA (15m) — Entry Filter`:
- Rows: `Param label | Base value (read-only, C.textMuted) | Override input | Allowed range hint`
- Override input only shown/active when user explicitly checks the override checkbox
- Input validated against `allowedParamRanges` — show error if out of range
- WindowExpression params (lookbackBars, multiplier, percentile) shown in a `Window Expressions` sub-group

Section: **Run Configuration**
- Capital (₹) — number input, default from strategy context
- Broker account — dropdown: `MStock | Zerodha | Upstox`
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
- Timeframe * — dropdown: all `Timeframe` values
- Mode * — `Backtest | Forward Test | Live`
- Broker * — `MStock | Zerodha | Upstox`
- Allocated Capital (₹) * — number input
- Schedule — day checkboxes + start/end time (HH:MM)

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
- Toggle: `Backtest | Forward | Both`

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
- FIX-3: `btToFtDegradationRatio` rendering rules:
  - If `undefined` (no forward run yet) → render `'—'`, no background color applied
  - If defined AND `< 0.7` → cell bg `C.redBg`, color `C.red`
  - If defined AND `>= 0.7` → normal cell (best-value highlight still applies via `C.greenBg`)
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
- Mode toggle: `Backtest | Forward | All`
- Date range: from/to inputs

Table columns: `Scenario | Mode | Date Range | Return | Max DD | Sharpe | Win% | PF | Trades | Details`

- Details link: `View →` opens run detail panel or route
- Return: green if positive, red if negative
- All numeric metrics: `F.mono`, right-aligned, `tabular-nums`

---

## File checklist

```
frontend/src/types/strategy.ts                          CREATE — all interfaces
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
- [ ] `frontend/src/types/strategy.ts` created with ALL interfaces above
- [ ] Strategy creation: Name + primaryTimeframe + instruments + tradingStyle + indicators + rules + exits + risk (full form, 3 sub-tabs)
- [ ] Scenario creation: inherited indicators read-only, parameter overrides only within allowedParamRanges, run config
- [ ] Deployment creation: symbol + timeframe + broker + capital + schedule + scenario link
- [ ] Results tab: table with filters (scenario, mode, date range)
- [ ] Compare tab: 3 comparison modes, metric table + delta column + equity/drawdown charts
- [ ] All 5 tabs render without console errors
- [ ] Status chips consistent across all components using canonical set
- [ ] IndicatorModal: type + TF + role + baseParams + allowedParamRanges, no TF restrictions
- [ ] RuleGroupEditor: condition builder with IndicatorRef, WindowExpression, numeric operands
- [ ] Zero raw hex in any new file
- [ ] `npx tsc --noEmit` passes
- [ ] `npm run dev` starts clean

**After implementation confirmed:** replace this entry with a one-line stub:
`## PROMPT-001 — DONE — Strategy & Scenario Full Domain Model`
