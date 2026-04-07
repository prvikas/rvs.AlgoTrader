# PROMPT.md — Precision AI Coding Prompt

> Read this file only when explicitly requested via `docs/PROMPT.md`.
> Delete or stub each entry immediately after implementation is confirmed done.

---

## PROMPT-001 — DONE — Strategy & Scenario Full Domain Model

---

## PROMPT-002 — DONE — Advanced Quant Research Features

**Context:**
PROMPT-001 delivered the base Strategy / Scenario / Deployment / RunResult domain model with full-page
strategy creation, scenario drawers, deployments, results, and compare tabs.

PROMPT-002 extends the platform from "functional form" to "professional quant research tool."
Every addition below is grounded in how a systematic trader actually thinks:
signal → filter → sizing → risk → regime → robustness.

**Core invariants from PROMPT-001 still apply in full. Do not violate them.**
Additionally:
7. Position sizing is a first-class model — not a capital ceiling. It lives on Strategy, not Deployment.
8. Stop loss is a state machine (initial → breakeven → trailing) — not a single value.
9. Signal layers are ordered (Primary → Confirmation → Trigger → Invalidation) — not a flat indicator list.
10. Scenarios are hypothesis containers — they carry research context, not just parameter overrides.
11. Robustness metrics (WFE, parameter sensitivity, Monte Carlo) are mandatory on the Compare tab.
12. Trade distribution analysis (MAE/MFE, P&L histogram, streak analysis) lives on the Results tab.

---

## 1 — Signal Layer Architecture

### Problem
The current flat indicator table with a Role dropdown does not reflect how quant strategies are structured.
A professional strategy has an ordered dependency chain: a trade idea is only valid when all upstream
layers agree.

### New concept: SignalLayer enum

```ts
// Add to frontend/src/types/strategy.ts

export enum SignalLayer {
  HTFBias              = 'HTFBias',
  MTFContext           = 'MTFContext',
  PrimarySignal        = 'PrimarySignal',
  ConfirmationFilter   = 'ConfirmationFilter',
  EntryTrigger         = 'EntryTrigger',
  Invalidation         = 'Invalidation',
}
```

### DB seed additions

```sql
INSERT INTO enum_values (domain, value, label, sort_order) VALUES
  ('signalLayer','HTFBias','HTF Bias',1),
  ('signalLayer','MTFContext','MTF Context',2),
  ('signalLayer','PrimarySignal','Primary Signal',3),
  ('signalLayer','ConfirmationFilter','Confirmation Filter',4),
  ('signalLayer','EntryTrigger','Entry Trigger',5),
  ('signalLayer','Invalidation','Invalidation',6);
```

### Interface change

```ts
// Extend IndicatorConfig in strategy.ts
export interface IndicatorConfig {
  id: string
  type: IndicatorType
  timeframe: Timeframe
  role: IndicatorRole
  signalLayer: SignalLayer          // NEW
  layerOrder: number                // NEW — sort order within the layer (1, 2, 3…)
  baseParams: Record<string, number | string | boolean>
  allowedParamRanges: Record<string, ParamRange | string[]>
}
```

### UI change — Core & Indicators tab

Replace the flat indicators table with a layered signal chain view.
Each layer is a collapsible card group. Render in this order:
HTF Bias → MTF Context → Primary Signal → Confirmation Filters → Entry Trigger → Invalidation.

Each group card shows:
  header: layer label + indicator count
  rows: Indicator name · TF · Role · Base Params · Allowed Ranges · [Edit] [×]
  drag handle for reorder within layer

"+ Add Indicator" prompts layer selection first, then type/TF/role.
The signal layer chain is read-only in the Scenario drawer (structural lock — invariant 3).

### Multi-timeframe waterfall enforcement

In the Rules tab, conditions evaluate in layer order:
HTFBias → MTFContext → PrimarySignal → ConfirmationFilter → EntryTrigger.
If HTFBias conditions fail, downstream layers are not evaluated.
Show this visually in the Rules tab as a waterfall diagram with ✓/✗ per layer.

---

## 2 — Entry Execution Model

### Problem
The current model defines WHEN to enter but not HOW. Two strategies with identical entry conditions
but different execution models produce completely different backtest results.

### New enums and interface

```ts
export enum EntryOrderType {
  MarketNextOpen   = 'MarketNextOpen',
  LimitAtClose     = 'LimitAtClose',
  LimitAtRetest    = 'LimitAtRetest',
  StopEntryOffset  = 'StopEntryOffset',
}

export enum EntryTiming {
  Immediately         = 'immediately',
  WaitForCandleClose  = 'waitForCandleClose',
  FirstNBarsOnly      = 'firstNBarsOnly',
}

export enum ScalingModel {
  FullImmediately   = 'FullImmediately',
  ScaleIn2          = 'ScaleIn2',
  Pyramid           = 'Pyramid',
}

export interface EntryExecutionModel {
  orderType: EntryOrderType
  timing: EntryTiming
  firstNBars?: number
  maxSlippagePoints?: number
  scalingModel: ScalingModel
  pyramidIntervalR?: number
}
```

### DB seed additions

```sql
INSERT INTO enum_values (domain, value, label, sort_order) VALUES
  ('entryOrderType','MarketNextOpen','Market — Next Bar Open',1),
  ('entryOrderType','LimitAtClose','Limit at Signal Bar Close',2),
  ('entryOrderType','LimitAtRetest','Limit at Retest of Level',3),
  ('entryOrderType','StopEntryOffset','Stop Entry with Offset',4),
  ('entryTiming','immediately','Immediately',1),
  ('entryTiming','waitForCandleClose','Wait for Candle Close',2),
  ('entryTiming','firstNBarsOnly','First N Bars of Session Only',3),
  ('scalingModel','FullImmediately','Full Position Immediately',1),
  ('scalingModel','ScaleIn2','Scale In — 50/50',2),
  ('scalingModel','Pyramid','Pyramid into Winners',3);
```

### UI change

Add an Entry Execution block on the Core & Indicators tab, right column, below Risk Controls:
- Order type: enums.entryOrderType dropdown
- Timing: enums.entryTiming dropdown
- First N bars input (shown when timing = FirstNBarsOnly)
- Max slippage points input (optional)
- Scaling: enums.scalingModel dropdown
- Pyramid interval R input (shown when scalingModel = Pyramid)

### Strategy interface extension

```ts
export interface Strategy {
  // ...existing fields...
  entryExecution: EntryExecutionModel   // NEW
}
```

---

## 3 — Position Sizing Model

### Problem
"Allocated Capital" is a ceiling, not a sizing model. Without explicit position sizing,
the backtest engine cannot correctly simulate trade outcomes.

### New enums and interfaces

```ts
export enum SizingMethod {
  FixedRiskPct      = 'FixedRiskPct',
  KellyFraction     = 'KellyFraction',
  VolatilityNorm    = 'VolatilityNorm',
  FixedLots         = 'FixedLots',
  FixedCapital      = 'FixedCapital',
}

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
```

### DB seed additions

```sql
INSERT INTO enum_values (domain, value, label, sort_order) VALUES
  ('sizingMethod','FixedRiskPct','Fixed Risk % per Trade',1),
  ('sizingMethod','KellyFraction','Kelly Fraction',2),
  ('sizingMethod','VolatilityNorm','Volatility Normalized',3),
  ('sizingMethod','FixedLots','Fixed Lots',4),
  ('sizingMethod','FixedCapital','Fixed Capital Amount',5);
```

### UI change

Add a Position Sizing block on Sub-tab 3 (Risk & Regime), left column, above Stop Loss:
- Method: enums.sizingMethod dropdown
- Base risk per trade % input (shown for FixedRiskPct)
- Kelly fraction input (shown for KellyFraction, e.g. 0.25 = quarter Kelly)
- Fixed lots / Fixed capital input (shown for respective methods)
- Max position size % of capital input
- Liquidity cap % of ADV input (optional)
- Drawdown scaling toggle: reduceSizeAtDrawdownPct + minSizeMultiplier
- Regime scaling toggle: indicatorId selector + condition builder + increaseSizeMultiplier

### Strategy interface extension

```ts
export interface Strategy {
  // ...existing fields...
  positionSizing: PositionSizingModel   // NEW
}
```

---

## 4 — Stop Loss as State Machine

### Problem
The current stop model is a single initial placement value.
A professional stop evolves through defined state transitions over the trade lifetime.

### New interfaces

```ts
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
```

### UI change — Risk & Regime tab, Stop Loss block

Replace the simple Stop Loss block with a state machine timeline.
Each state card shows:
  label input + stopType dropdown + value input
  activation condition: triggerR / triggerBars / triggerPct (whichever is set)
  moveStopTo dropdown (optional)
  drag handle for reorder

State 1 always labelled "Initial" with no activation condition (active at entry).
Subsequent states show "Activate when: profit reaches [X] R" etc.
"+ Add Stop State" button appends a new state.

---

## 5 — Advanced Condition Builder

### Problem
The current left/operator/right condition row cannot express professional quant logic:
lookback-aware, absence, percentile rank, or session-state conditions.

### Extend ConditionOperand

```ts
export type ConditionOperand =
  | { kind: 'indicator';    indicatorId: string; field?: string }
  | { kind: 'window';       expr: WindowExpression }
  | { kind: 'value';        value: number }
  | { kind: 'absence';      indicatorId: string; lookbackBars: number }       // NEW
  | { kind: 'percentile';   indicatorId: string; lookbackBars: number; pct: number } // NEW
  | { kind: 'slope';        indicatorId: string; lookbackBars: number }       // NEW
  | { kind: 'sessionState'; property: SessionStateProperty }                  // NEW

export enum SessionStateProperty {
  IsFirstSignalOfSession = 'IsFirstSignalOfSession',
  BarsSinceSessionOpen   = 'BarsSinceSessionOpen',
  PreviousTradeWasLoss   = 'PreviousTradeWasLoss',
  OpenPositionCount      = 'OpenPositionCount',
}
```

### DB seed additions

```sql
INSERT INTO enum_values (domain, value, label, sort_order) VALUES
  ('conditionOperandKind','indicator','Indicator Value',1),
  ('conditionOperandKind','window','Window Expression',2),
  ('conditionOperandKind','value','Fixed Value',3),
  ('conditionOperandKind','absence','Absence (N bars)',4),
  ('conditionOperandKind','percentile','Percentile Rank',5),
  ('conditionOperandKind','slope','Slope Direction',6),
  ('conditionOperandKind','sessionState','Session State',7),
  ('sessionStateProperty','IsFirstSignalOfSession','First Signal of Session',1),
  ('sessionStateProperty','BarsSinceSessionOpen','Bars Since Session Open',2),
  ('sessionStateProperty','PreviousTradeWasLoss','Previous Trade Was Loss',3),
  ('sessionStateProperty','OpenPositionCount','Open Position Count',4);
```

### UI change — ConditionRow component

Expand left operand type selector to all 7 kinds:
- absence: indicator picker + lookback bars, label "Has NOT appeared in last N bars"
- percentile: indicator picker + lookback bars + percentile input
  e.g. "ATR is above 70th percentile of last 50 bars"
- slope: indicator picker + lookback bars
  e.g. "EMA(20) slope positive over last 3 bars"
- sessionState: SessionStateProperty dropdown, no right operand for boolean properties

---

## 6 — Scenario Hypothesis + Parameter Sweep

### Problem
Scenarios are nameless parameter bags. A quant designs scenarios as testable hypotheses
and generates parameter families systematically.

### Interface extensions

```ts
// Extend Scenario
export interface Scenario {
  // ...existing fields...
  hypothesis?: string
  hypothesisTag?: string
  isBaseline?: boolean
  sweepGroupId?: string
  promotionNotes?: string
}

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
```

### UI changes

**Scenarios tab — row additions:**
- Hypothesis column (truncated at 60 chars, full on hover tooltip)
- "BASE" chip next to scenario name when isBaseline = true
- Sweep group rows: collapsible group header
  e.g. "▶ ema-period-sensitivity (10 variants)" → expands to show all 10 scenario rows

**Scenarios tab — "+ Parameter Sweep" button** beside "+ New Scenario":
Opens a 520px right drawer with:
- Hypothesis textarea
- Tag input
- Parameter selector: indicator dropdown → param key dropdown
- Sweep from / to / step inputs
- Preview: "Will generate N scenarios"
- Other overrides section (same UI as ScenarioDrawer overrides)
- Footer: Cancel | Generate N Scenarios

Clicking Generate creates (to - from) / step + 1 scenarios with:
- Sequential names e.g. "EMA Period 10", "EMA Period 12" … "EMA Period 30"
- Same sweepGroupId on all
- Validates each override against allowedParamRanges — rejects out-of-range with error
- Batch-submits for backtest

**ScenarioDrawer — add at top above Inherited Indicators:**
- Hypothesis textarea (optional)
- Tag input
- "Mark as baseline" checkbox

---

## 7 — Robustness Metrics on Compare Tab

### Problem
Performance metrics tell you how a strategy performed.
Robustness metrics tell you whether the performance is real or curve-fitted.

### Extend RunMetrics

```ts
export interface RunMetrics {
  // ...existing fields...
  walkForwardEfficiency?: number
  parameterStabilityScore?: number
  monteCarloMedianReturn?: number
  monteCarloPct5Return?: number
  overfitScore?: number
  degreesFreedomRatio?: number
  regimeBreakdown?: Record<string, RunMetrics>
}
```

### Threshold rules (used in UI chips and colour coding)

WalkForwardEfficiency:
  ≥ 0.65 → C.green chip ✓
  0.45–0.65 → C.amber chip ⚠
  < 0.45 → C.red chip ✗

ParameterStabilityScore:
  ≥ 0.60 → ✓  |  0.40–0.60 → ⚠  |  < 0.40 → ✗

OverfitScore (lower = better):
  ≤ 0.10 → ✓  |  0.10–0.20 → ⚠  |  > 0.20 → ✗

DegreesFreedomRatio:
  ≥ 15 → ✓  |  8–15 → ⚠  |  < 8 → ✗

### UI changes — Compare tab

**Robustness section** below existing metric cards:
Table: metric label | value per column | Δ delta | threshold chip
Metrics: WalkForwardEfficiency | ParameterStabilityScore | MonteCarloMedianReturn
         | MonteCarloPct5Return | OverfitScore | DegreesFreedomRatio
All values: F.mono, tabular-nums, right-aligned.
Best value per row: C.greenBg. Failing value: C.redBg.

**Research funnel panel** in Compare tab left pane below run selectors:
Checkbox filter list with defaults:
  Return > 0%
  Profit Factor > 1.3
  Max DD < 20%
  Trade count >= 30
  WFE >= 0.65
  DoF Ratio >= 10
[Apply Filters] button
Survivors count: "3 / 12 scenarios"
Scenarios failing any filter: greyed out in table with "FILTERED" chip.

**Regime breakdown accordion** below robustness section:
Expandable per-regime performance table keyed by regimeBreakdown labels.
Monte Carlo metrics are backend-computed async — show spinner while computing.

---

## 8 — Trade Distribution Analysis on Results Tab

### Problem
MAE/MFE analysis reveals whether stops and targets are correctly placed.
P&L distribution reveals fat tails. Streak analysis reveals worst-case sequences.

### New API endpoint

```
GET /api/strategies/{id}/scenarios/{sid}/trades?runId={runId}
```

### New interface

```ts
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
  exitReason: 'StopHit' | 'TargetHit' | 'TrailingStop' | 'SessionEnd' | 'Manual'
  regime?: string
}
```

### UI changes — Results tab

Add Trade Analysis section below runs table (shown when a run row is expanded/selected).
Five sub-components, all using existing charting library:

**MAEMFEScatterChart:**
  X axis: MAE (adverse excursion). Y axis: MFE (favourable excursion).
  Dot per trade: C.green = winner, C.red = loser.
  Vertical reference line at stop distance, horizontal at target distance.

**PnLHistogram:**
  X axis: P&L in R multiples (0.25R bins). Y axis: trade count.
  Reference line at 0. Normal distribution overlay curve.

**TimeOfDayHeatmap:**
  X axis: entry hour 9AM–3PM in 30-min buckets. Y axis: day of week Mon–Fri.
  Cell colour: avg P&L gradient from C.greenBg to C.redBg.

**StreakAnalysis (stat cards):**
  Consecutive losses: Max | Avg | Current
  Consecutive wins: Max | Avg | Current
  Expected worst streak at 95% CI

**ExitReasonDonut:**
  Segments: StopHit | TargetHit | TrailingStop | SessionEnd | Manual
  Percentage labels per segment.

---

## 9 — Rules Tab — Signal Waterfall Panel

### Problem
The Rules tab currently shows four bare checkboxes with no structure.

### UI change

Replace the four checkboxes with a signal waterfall panel for each side (Long | Short).

For each entry block, render layers top-to-bottom:

  Layer N: [layer label] [TF] indicator name — condition summary    [✓ PASS | ✗ FAIL | — BLOCKED]
  ↓ (only evaluated if layer above passes)

In edit mode: expand layer to show RuleGroupEditor (same condition builder as before,
grouped by signalLayer). In backtest review mode (trade selected): show live ✓/✗ per layer
for the selected trade based on its entry evaluation snapshot.

---

## 10 — Promotion Checklist Modal

### Problem
Promoting to Forward Test is a significant research decision.
The current single button has no ceremony or record-keeping.

### UI change

When user clicks "→ Fwd Test" on a scenario, open a Promotion Checklist modal before executing.

Modal contents:
  Title: "Promote to Forward Test — {Scenario Name}"

  Auto-checked where metric is satisfied, manual for judgement items:
    ☑ Backtest covers >= 6 months of data
    ☑ Trade count >= 30
    ☑ WFE >= 0.65                 shows current value + ✓/✗
    ☑ Overfit score <= 0.10       shows current value + ✓/✗
    ☑ Max drawdown acceptable     shows current value
    ☑ Parameter stability reviewed
    ☑ MAE/MFE analysis reviewed

  Research notes textarea (required, min 20 chars):
    Cannot promote with blank notes — show inline error if empty on submit.

  Footer: [Cancel] [Promote to Forward Test →]

Promotion notes stored as promotionNotes: string on Scenario.
Display promotionNotes as a tooltip on the scenario card status chip in the Scenarios tab.

---

## File checklist — additions to PROMPT-001 base

```
DB migration:
  enum_values — signalLayer, entryOrderType, entryTiming, scalingModel,
                 sizingMethod, conditionOperandKind, sessionStateProperty       ADD rows

Backend:
  backend/src/routes/tradesRoute.ts            CREATE — GET /strategies/{id}/scenarios/{sid}/trades

Frontend types (frontend/src/types/strategy.ts):
  SignalLayer enum                             ADD
  EntryOrderType, EntryTiming, ScalingModel    ADD
  SizingMethod enum                            ADD
  SessionStateProperty enum                    ADD
  ConditionOperand — absence/percentile/slope/sessionState kinds  EXTEND
  EntryExecutionModel interface                ADD
  PositionSizingModel, DrawdownScaling, RegimeScaling  ADD
  StopState, StopStateMachine interfaces       ADD
  ParameterSweep interface                     ADD
  TradeRecord interface                        ADD
  RunMetrics — robustness fields              EXTEND
  Scenario — hypothesis/tag/baseline/sweep/promotionNotes  EXTEND
  Strategy — entryExecution, positionSizing, stopStateMachine  EXTEND

Frontend components:
  Core & Indicators tab — signal layer card groups          MODIFY
  Core & Indicators tab — Entry Execution block             ADD
  Risk & Regime tab — Position Sizing block                 ADD
  Risk & Regime tab — Stop State Machine timeline           MODIFY (replaces simple SL)
  Risk & Regime tab — Regime Filters block                  ADD
  Rules tab — signal waterfall panel                        MODIFY
  ConditionRow — absence/percentile/slope/sessionState kinds  EXTEND
  ScenariosTab — hypothesis column, sweep group rows        MODIFY
  ScenariosTab — "+ Parameter Sweep" drawer                 ADD
  ScenarioDrawer — hypothesis/tag/baseline fields           MODIFY
  CompareTab — Robustness metrics section                   ADD
  CompareTab — Research funnel filter panel                 ADD
  CompareTab — Regime breakdown accordion                   ADD
  ResultsTab — Trade Analysis section                       ADD
    MAEMFEScatterChart                                       CREATE
    PnLHistogram                                             CREATE
    TimeOfDayHeatmap                                         CREATE
    StreakAnalysis                                            CREATE
    ExitReasonDonut                                          CREATE
  PromotionChecklistModal                                    CREATE
```

---

## Constraints (all PROMPT-001 constraints apply plus)

- SignalLayer ordering enforced in rule evaluation: HTFBias always before EntryTrigger
- ParameterSweep validates each override against allowedParamRanges before creating
- Trade analysis charts require a runId selection — show empty state if none selected
- Promotion notes stored and shown as tooltip on scenario status chip
- Robustness metrics are backend-computed and stored on RunResult — not frontend-calculated
- Monte Carlo is async backend job — show progress indicator while computing
- All new chart components use the existing charting library already in the project
- Zero raw hex — C.* tokens only (AP-020)
- All new dropdowns use useEnums() — no hardcoded arrays
- TypeScript strict — no any, no implicit undefined
- npx tsc --noEmit must pass with zero errors
- npm run dev must start with zero console errors

---

## Definition of done

- [ ] SignalLayer enum seeded and rendered as layered card groups in Core & Indicators
- [ ] Entry Execution block present with all fields using useEnums() dropdowns
- [ ] Position Sizing block present with drawdown scaling and regime scaling toggles
- [ ] Stop Loss rendered as state machine timeline with addable states
- [ ] Regime Filters block present on Risk & Regime tab with condition builder
- [ ] Rules tab shows signal waterfall panel with layer-level pass/fail
- [ ] ConditionRow supports absence/percentile/slope/sessionState operand kinds
- [ ] Parameter Sweep drawer generates correct number of scenarios with sweepGroupId
- [ ] Scenarios tab shows hypothesis column and sweep group collapsible rows
- [ ] ScenarioDrawer shows hypothesis/tag/baseline fields
- [ ] Compare tab shows Robustness metrics section with threshold chips
- [ ] Compare tab left pane shows research funnel with survivor count
- [ ] Results tab shows Trade Analysis section with all 5 sub-charts when run selected
- [ ] Promotion checklist modal fires on "→ Fwd Test" with required notes enforcement
- [ ] npx tsc --noEmit passes with zero errors
- [ ] npm run dev starts with zero console errors

**After implementation confirmed:** replace with:
`## PROMPT-002 — DONE — Advanced Quant Research Features`
