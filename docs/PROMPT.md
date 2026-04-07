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
  ScenariosTab — "+" Parameter Sweep" drawer                ADD
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

---

## PROMPT-003 — DB Integrity + Backtest Engine Fixes + Data Services (P7)

> Phase coverage: DB fixes roadmap (#188–#213), backtest engine critical bugs, P7 data services.
> Implement in the order listed. Each section is self-contained — stop and confirm before the next.

---

### A — DB Migrations 020–023 (Critical → Cleanup)

**Context:** `docs/DB_FIXES_ROADMAP.md` describes 25 issues in 4 migration phases.
All migration files go in `src/rvs.AlgoTrader.Infrastructure/Persistence/Migrations/` numbered in sequence
(current last applied: 027 — next new file starts at 028 or whichever is next available).

#### A1 — Critical financial integrity (migration 028)

```sql
-- #209: FX Rates
ALTER TABLE fx_rates ADD CONSTRAINT chk_fx_rates_rate_positive CHECK (rate > 0);

-- #205: Instruments
ALTER TABLE instruments ADD CONSTRAINT chk_instruments_price_multiplier CHECK (price_multiplier > 0);
ALTER TABLE instruments ADD CONSTRAINT chk_instruments_tick_size CHECK (tick_size > 0);
ALTER TABLE instruments ADD CONSTRAINT chk_instruments_lot_size CHECK (lot_size > 0);

-- #210: Capital reservation
ALTER TABLE strategy_instances
  ADD CONSTRAINT chk_capital_reservation
  CHECK (reserved_capital >= 0 AND reserved_capital <= allocated_capital);

-- #211: Risk profile percentages
ALTER TABLE risk_profiles
  ADD CONSTRAINT chk_risk_max_position_pct CHECK (max_position_size_pct > 0 AND max_position_size_pct <= 1);

-- #212: Spread positions correlation_id
ALTER TABLE spread_positions ALTER COLUMN correlation_id DROP DEFAULT;
ALTER TABLE spread_positions ALTER COLUMN correlation_id TYPE UUID USING NULLIF(correlation_id,'')::UUID;

-- #213: Alert type validation
ALTER TABLE alert_log
  ADD CONSTRAINT chk_alert_type CHECK (alert_type IN (
    'KillSwitch','DailyLossLimit','DrawdownThreshold','OrderRejected',
    'BrokerDisconnect','DataFeedStale','MarginBreach','CapitalBreach'));

-- #204: Status/enum column constraints (9 columns across 7 tables)
ALTER TABLE orders
  ADD CONSTRAINT chk_orders_status CHECK (status IN ('Pending','Open','Filled','Cancelled','Rejected','PartialFill'));
ALTER TABLE strategy_instances
  ADD CONSTRAINT chk_strategy_instances_status CHECK (status IN ('Active','Paused','Stopped','Draft','Error'));
ALTER TABLE backtest_runs
  ADD CONSTRAINT chk_backtest_runs_status CHECK (status IN ('Queued','Running','Completed','Failed','Cancelled'));
ALTER TABLE forward_test_runs
  ADD CONSTRAINT chk_forward_test_runs_status CHECK (status IN ('Active','Paused','Stopped','Completed'));
ALTER TABLE strategy_approvals
  ADD CONSTRAINT chk_strategy_approvals_status CHECK (status IN ('Pending','Approved','Revoked','Expired'));
ALTER TABLE positions
  ADD CONSTRAINT chk_positions_side CHECK (side IN ('Long','Short'));
ALTER TABLE forward_test_trades
  ADD CONSTRAINT chk_ftt_exit_reason CHECK (exit_reason IN ('StopHit','TargetHit','TrailingStop','SessionEnd','Manual') OR exit_reason IS NULL);
```

#### A2 — Referential integrity (migration 029)

```sql
-- #201: internal_symbol nullable for watchlist mode
ALTER TABLE strategy_instances ALTER COLUMN internal_symbol DROP NOT NULL;
ALTER TABLE strategy_instances
  ADD CONSTRAINT chk_strategy_symbol_or_watchlist
  CHECK (internal_symbol IS NOT NULL OR watchlist_id IS NOT NULL);

-- #197: Missing FK backtest_runs → strategy_instances
ALTER TABLE backtest_runs
  ADD CONSTRAINT fk_backtest_runs_strategy_instance
  FOREIGN KEY (strategy_instance_id) REFERENCES strategy_instances(id) ON DELETE CASCADE;

-- #192: 14 missing FK relationships (add only those with confirmed matching columns)
-- orders → strategy_instances
ALTER TABLE orders
  ADD CONSTRAINT fk_orders_strategy_instance
  FOREIGN KEY (strategy_instance_id) REFERENCES strategy_instances(id) ON DELETE CASCADE;
-- positions → strategy_instances
ALTER TABLE positions
  ADD CONSTRAINT fk_positions_strategy_instance
  FOREIGN KEY (strategy_instance_id) REFERENCES strategy_instances(id) ON DELETE CASCADE;
-- forward_test_trades → forward_test_runs
ALTER TABLE forward_test_trades
  ADD CONSTRAINT fk_ftt_forward_test_run
  FOREIGN KEY (forward_test_run_id) REFERENCES forward_test_runs(id) ON DELETE CASCADE;
-- alert_log → strategy_instances (nullable)
ALTER TABLE alert_log
  ADD CONSTRAINT fk_alert_log_strategy_instance
  FOREIGN KEY (strategy_instance_id) REFERENCES strategy_instances(id) ON DELETE SET NULL;
```

#### A3 — Uniqueness & performance (migration 030)

```sql
-- #208: Broker session expiry
ALTER TABLE broker_sessions
  ADD CONSTRAINT chk_broker_session_expiry CHECK (expires_at > stored_at);
CREATE INDEX IF NOT EXISTS idx_broker_sessions_expires_at ON broker_sessions(expires_at);

-- #207: Scenario name uniqueness per strategy instance
CREATE UNIQUE INDEX IF NOT EXISTS idx_scenarios_instance_name
  ON strategy_scenarios(strategy_instance_id, name);

-- #206: Backtest run deduplication
CREATE UNIQUE INDEX IF NOT EXISTS idx_backtest_runs_scenario_hash
  ON backtest_runs(scenario_id, data_hash)
  WHERE data_hash IS NOT NULL;
```

#### A4 — Schema cleanup (migration 031)

```sql
-- #191: Drop orphaned UUID column from candles
ALTER TABLE candles DROP COLUMN IF EXISTS "Id";

-- #190: Rename PascalCase columns to snake_case
ALTER TABLE strategy_instances RENAME COLUMN "WatchlistId" TO watchlist_id_legacy;
-- (verify column existence before running; skip if already snake_case)

-- #189: Drop duplicate trailing stop columns
ALTER TABLE forward_test_trades DROP COLUMN IF EXISTS "TrailingSl";
ALTER TABLE forward_test_trades DROP COLUMN IF EXISTS "TrailingTp";

-- #188: Drop PascalCase duplicate instrument columns
ALTER TABLE instruments DROP COLUMN IF EXISTS "Underlying";
ALTER TABLE instruments DROP COLUMN IF EXISTS "StrikePrice";
ALTER TABLE instruments DROP COLUMN IF EXISTS "OptionType";
ALTER TABLE instruments DROP COLUMN IF EXISTS "Expiry";

-- #200: Drop overlapping unique constraints on idempotency_key (keep one canonical)
DROP INDEX IF EXISTS idx_orders_idempotency_key_2;
DROP INDEX IF EXISTS idx_orders_idempotency_key_3;

-- #199: Drop duplicate candles index
DROP INDEX IF EXISTS idx_candles_symbol_tf;

-- #193: Drop 13 pairs of duplicate indexes — run after confirming canonical names
-- Verify with: SELECT indexname FROM pg_indexes WHERE tablename = '...' ORDER BY indexname;
-- Pattern: keep idx_{table}_{column(s)}, drop all secondary equivalents.
```

**Constraints on A1–A4:**
- Each migration file is a plain `.sql` file, numeric prefix only, auto-discovered by `DatabaseMigrationRunner`.
- Never modify an already-applied migration — create a new numbered file.
- Run `dotnet run --project src/rvs.AlgoTrader.API` after each migration file to verify startup.
- For #203 (overlapping PnL columns) and #202 (price column ambiguity): document findings in `docs/DB_FIXES_ROADMAP.md` under a "Manual Review" section — do not rename columns without a full code audit first.
- For #195 (redundant timestamp columns) and #194 (table consolidation): also document only — these require data migration and code audit before execution.

---

### B — Backtest Engine Critical Bugs (from REQUIREMENTS_DELTA.md 2026-03-30)

Three confirmed bugs causing negative returns across all strategies:

#### B1 — Position sizing ignores entry price scale

**File:** `src/rvs.AlgoTrader.Backtesting/BacktestExecutionEngine.cs`
(also `src/rvs.AlgoTrader.Application/Services/ForwardTestEngine.cs` or equivalent)

**Bug:** Position size calculated as `risk / stopDistance` omitting entry price.
For a ₹5000 stock with 1% stop (50 pts), this produces 20× too many shares.

**Fix:** Change position size formula:
```csharp
// WRONG (current)
var quantity = (decimal)riskAmount / stopDistancePoints;

// CORRECT
var quantity = (decimal)riskAmount / (stopDistancePoints * entryPrice);
// For lot-based instruments: quantity = floor(riskAmount / (stopDistancePoints * lotSize))
```

Apply the same fix in `PositionSizingEngine` (all 5 models that involve stop distance).
After fix: run existing unit tests, verify quantities are in valid lot-size multiples.

#### B2 — Transaction costs applied only on exit

**File:** `src/rvs.AlgoTrader.Backtesting/BacktestExecutionEngine.cs`

**Bug:** `IndianMarketCommissionModel` called only at trade close.
Entry commission (~₹10–20 for equity, higher for options) inflates equity during the trade.

**Fix:**
```csharp
// On trade ENTRY
var entryCommission = _commissionModel.Calculate(entryPrice, quantity, instrument);
capital -= entryCommission;
trade.EntryCommission = entryCommission;

// On trade EXIT (already exists — keep)
var exitCommission = _commissionModel.Calculate(exitPrice, quantity, instrument);
capital -= exitCommission;
trade.ExitCommission = exitCommission;

// PnL = (exitPrice - entryPrice) * quantity * side - entryCommission - exitCommission
trade.NetPnl = trade.GrossPnl - trade.EntryCommission - trade.ExitCommission;
```

Update `BacktestTradeDto` and `BacktestResultDto` to expose `EntryCommission` and `ExitCommission`.

#### B3 — No parameter validation in FromJson()

**File:** Each strategy's `FromJson()` static factory method (VcpStrategy, FibonacciStrategy, PcrStrategy, etc.)

**Bug:** `strategyParams = {}` passes validation and strategy runs with zero/default values that break logic
(e.g., `SMA200Period=0` causes division by zero in indicator computation).

**Fix pattern for each strategy:**
```csharp
public static VcpStrategy FromJson(string json)
{
    var p = JsonSerializer.Deserialize<VcpParams>(json) ?? new VcpParams();

    // Validate — throw ArgumentException with field name for any invalid param
    if (p.SmaPeriod <= 0)     throw new ArgumentException("SmaPeriod must be > 0",     nameof(p.SmaPeriod));
    if (p.Sma200Period <= 0)  throw new ArgumentException("Sma200Period must be > 0",  nameof(p.Sma200Period));
    if (p.VcpContraction <= 0) throw new ArgumentException("VcpContraction must be > 0", nameof(p.VcpContraction));
    // ... validate all numeric params ...

    return new VcpStrategy(p);
}
```

Apply the same pattern to: `FibonacciStrategy.FromJson()`, `PcrStrategy.FromJson()`, and all other strategy classes.
Backtest service must catch `ArgumentException` from `FromJson()` and return HTTP 422 with the field name.

---

### C — Frontend UX Fixes (from REQUIREMENTS_DELTA.md 2026-03-30)

Three confirmed UX breakages on strategy creation:

#### C1 — Schema not fetched on strategy type change

**File:** `frontend/src/pages/StrategyDefinitionPage.tsx` (or wherever strategy type dropdown lives)

**Bug:** Selecting a strategy type does not trigger `GET /api/strategies/schema?type={type}`.
The parameter editor shows blank fields.

**Fix:**
```tsx
// On strategy type change
const handleStrategyTypeChange = async (type: StrategyType) => {
  setStrategyType(type)
  const schema = await api.get<StrategySchema>(`/strategies/schema?type=${type}`)
  setSchema(schema)
  // Pre-populate form with schema defaults
  setParams(
    Object.fromEntries(
      Object.entries(schema.parameters).map(([k, v]) => [k, v.defaultValue])
    )
  )
}
```

#### C2 — Parameter editor shows no defaults or descriptions

**Fix:** When schema is loaded, render each parameter with:
- Label from `schema.parameters[key].label`
- Description/tooltip from `schema.parameters[key].description`
- Input pre-filled with `schema.parameters[key].defaultValue`
- Min/max hints from `schema.parameters[key].allowedRange`

Use `HelpTooltip` (already in project) on every field with the description text.

#### C3 — Empty `strategyParams = {}` passes backend validation

**Fix in frontend:** Before submitting, verify all required parameters have non-zero values:
```tsx
const requiredParams = Object.entries(schema.parameters)
  .filter(([, v]) => v.required)
  .map(([k]) => k)

const missing = requiredParams.filter(k => !params[k] || params[k] === 0)
if (missing.length > 0) {
  setErrors(missing.map(k => `${k} is required`))
  return
}
```

**Fix in backend:** `BacktestService.StartAsync()` (or `CreateStrategyCommand` handler):
- Call `strategy.FromJson(paramsJson)` inside a try-catch before queuing the job.
- On `ArgumentException`: return `ValidationProblem` HTTP 422 with the field name.

---

### D — P7 Data Services

**Context:** `docs/PLAN.md` Phase P7 — status: TODO.
These are the live data feeds required for STRAT-001, STRAT-002, STRAT-003 to work in production.

#### D1 — BreadthService via NSE Bhavcopy

**Interface already exists:** `IMarketBreadthService` (DONE per IMPLEMENTATION_STATUS.md).
**Gap:** The service needs a real data source — NSE Bhavcopy CSV download.

**File to create:** `src/rvs.AlgoTrader.Infrastructure/Services/NseBhavcopyCandleSource.cs`

```csharp
// Downloads: https://nsearchives.nseindia.com/products/content/sec_bhavdata_full_{DDMMYYYY}.csv
// Parses: SYMBOL,SERIES,OPEN,HIGH,LOW,CLOSE,LAST,PREVCLOSE,TOTTRDQTY,TOTTRDVAL,...
// Filters: SERIES == "EQ"
// Stores: batch upsert into candles table for timeframe=Daily

public class NseBhavcopyCandleSource : INseBhavcopyCandleSource
{
    // Use IHttpClientFactory with Polly retry (AP-010)
    // URL pattern: https://nsearchives.nseindia.com/products/content/sec_bhavdata_full_{date:ddMMyyyy}.csv
    // Headers required: Referer: https://www.nseindia.com, User-Agent, Accept
    // On 404 (holiday/weekend): log and skip, do not throw
    // Parse CSV → CandleEntity list → BulkInsertAsync
}
```

Register as scoped; wire into `BreadthCalculatorJob` in `HangfireJobRegistry`.
Add download URL to `docs/DATA_SOURCES.md`.

#### D2 — EventCalendarService via NSE Corporate Calendar

**Interface already exists:** `IEventCalendarService` (DONE).
**Gap:** Live seeding from NSE corporate actions API.

**File to create:** `src/rvs.AlgoTrader.Infrastructure/Services/NseEventCalendarImporter.cs`

```csharp
// NSE corporate actions:
// GET https://www.nseindia.com/api/corporates-corporateActions?index=equities&from_date=...&to_date=...
// Requires cookie-based session (NSE blocks direct API calls) — use Playwright or mStock proxy
// Fields: symbol, purpose (dividend/bonus/split/results), exDate, recordDate
// Map purpose → MarketEventType enum
// Upsert into market_events table (idempotent on symbol+date+type)
```

**Alternative (simpler):** Accept CSV upload via `POST /api/events/import` (UI already has DataManagerController pattern — replicate).
Document both approaches in `docs/DATA_SOURCES.md`.

#### D3 — IVHistoryService for IVP computation

**Interface already exists:** `IOptionIvRankService` with `IvRankSnapshot` (DONE).
**Gap:** Historical IV data needed for percentile rank computation.

**File to create:** `src/rvs.AlgoTrader.Infrastructure/Services/IvHistoryService.cs`

```csharp
// Source: mStock option chain API — store daily IV snapshots
// Table: iv_history (internal_symbol, date, iv_close, iv_rank_20d, iv_rank_52w, iv_percentile_52w)
// IVP = percentile rank of current IV vs past 252 trading days
// Computation: SELECT PERCENT_RANK() OVER (ORDER BY iv_close) FROM iv_history WHERE ...

public class IvHistoryService : IIvHistoryService
{
    // IvRankSnapshot IvRankService.GetSnapshot(string symbol, DateOnly asOf)
    // Calls SELECT iv_close, iv_rank_52w, iv_percentile_52w FROM iv_history WHERE internal_symbol=... ORDER BY date DESC LIMIT 1
}
```

Migration for `iv_history` table: add as migration 032 (or next available number).

#### D4 — Verify mStock option chain IV/Greeks live

**File:** `src/rvs.AlgoTrader.Brokers.MStock/MStockOptionChainService.cs` (or equivalent)

Confirm the following fields are populated from the mStock API response and mapped to `OptionLegSpec`:
- `iv` (implied volatility, decimal, annualised)
- `delta`, `gamma`, `theta`, `vega` (Greeks)
- `openInterest`, `changeInOI`
- `lastPrice`, `bidPrice`, `askPrice`

If any field is missing: add a TODO comment with the exact mStock API field name and log a warning at startup.
Update `docs/DATA_SOURCES.md` with confirmed field mappings.

---

### E — Broker Integration Gaps (IMPLEMENTATION_STATUS: PARTIAL)

#### E1 — Zerodha broker implementation (stub → working)

**File:** `src/rvs.AlgoTrader.Brokers.Zerodha/`

Current status: assembly exists but HTTP calls are stubbed.

Priority tasks:
1. Implement `ZerodhaTokenStore` using `ITokenStore` + `ISecretsProvider` (match MStock pattern).
2. Implement `ZerodhaOrderService.PlaceOrderAsync()` using Kite Connect REST API.
3. Add Polly retry + circuit breaker (same config as MStock — AP-010).
4. Register in DI under `BrokerNames.Zerodha` constant.
5. Write unit tests for order placement and token refresh.

**Do not implement live trading** — mark `ZerodhaExecutionEngine` as `NotImplementedException` until forward-tested.

#### E2 — Upstox broker stub (assembly alignment)

Same pattern as E1 for `src/rvs.AlgoTrader.Brokers.Upstox/`.
At minimum: ensure the project builds and `BrokerNames.Upstox` is wired into DI with a `NotImplementedException` stub.

---

### F — Test Coverage Gaps (IMPLEMENTATION_STATUS: PARTIAL)

#### F1 — Unit tests for backtest engine fixes

After implementing B1–B3:
- Add `PositionSizingTests.cs` with cases for each of the 5 sizing models verifying quantity formula.
- Add `CommissionModelTests.cs` verifying entry + exit commission deduction and net PnL calculation.
- Add `StrategyFromJsonTests.cs` with invalid-param cases that should throw `ArgumentException`.

**Test project:** `tests/rvs.AlgoTrader.Tests.Unit`
**Run command:** `./run-tests.sh unit`

#### F2 — Architecture tests (verify anti-pattern enforcement)

**File:** `tests/rvs.AlgoTrader.Tests.Architecture/`

Ensure the following architecture rules are tested:
- Domain layer has no reference to Infrastructure or API
- No `DateTime.Now` usage anywhere (must use `IClock`)
- No hardcoded secrets or connection strings (regex scan)
- All broker HTTP clients use Polly (verify `AddPolicyHandler` in DI registration)

**Run command:** `./run-tests.sh arch`

---

### G — P8 MCP Integration (Placeholder → Design)

**Context:** `docs/PLAN.md` Phase P8 — status: PLACEHOLDER.

Do not implement yet. Document the design in `docs/PROMPT.md` under a new sub-section.

Design goals:
- Expose `GET /mcp/strategy-status` returning active strategies + P&L summary
- Expose `GET /mcp/backtest-results/{id}` returning latest run metrics
- Expose `POST /mcp/kill-switch` for emergency halt
- Authentication: same JWT as main API
- Reference implementation: https://github.com/marketcalls/openalgo-mcp

Design document to create: `docs/MCP_DESIGN.md` (500 words max, API shapes only).

---

### H — P9 Expansion Features (Placeholder → Scoped)

**Context:** `docs/PLAN.md` Phase P9 — status: TODO.

Do not implement yet. Add the following to `docs/PLAN.md` under P9 with status SCOPED:

**Screener:**
- `GET /api/screener/run?strategyId={id}` — runs strategy signal scan across instrument universe
- Returns top N instruments by signal strength
- UI: ScreenerPage with filterable results table

**News:**
- Integrate NSE/BSE announcements via RSS or existing NSE API
- `INewsService` interface, `NewsEntity`, `news` table
- UI: NewsPanel in sidebar (collapsible) showing latest 20 items

**Events:**
- Extend existing `IEventCalendarService` with earnings calendar
- Source: NSE results calendar (`/api/corporates-corporateActions?purpose=Results`)
- UI: EventCalendarPage with monthly calendar view

**Analytics:**
- Portfolio-level P&L dashboard (already started in TradeJournalPage)
- Add strategy correlation heatmap (IStrategyCorrelationAnalyser already done)
- Add drawdown timeline chart per strategy

---

## Definition of done for PROMPT-003

- [ ] Migrations 028–031 applied without errors; `dotnet run` succeeds after each
- [ ] B1 position sizing fix verified by unit test with known entry price + stop distance
- [ ] B2 entry commission deducted; BacktestTradeDto exposes EntryCommission + ExitCommission
- [ ] B3 FromJson() throws ArgumentException for zero/invalid params; BacktestService returns HTTP 422
- [ ] C1–C3 frontend fixes: schema loaded on type change, defaults shown, empty params blocked
- [ ] D1 NseBhavcopyCandleSource downloads and parses Bhavcopy CSV; BreadthCalculatorJob wired
- [ ] D2 EventCalendarService has at least CSV import path working
- [ ] D3 IvHistoryService and iv_history migration created
- [ ] D4 mStock option chain field mappings documented in DATA_SOURCES.md
- [ ] E1 Zerodha builds with Polly and token store (no live trading)
- [ ] F1 unit tests for B1–B3 pass (`./run-tests.sh unit`)
- [ ] F2 architecture tests pass (`./run-tests.sh arch`)
- [ ] G: docs/MCP_DESIGN.md created
- [ ] H: PLAN.md P9 updated with scoped items
- [ ] `dotnet run` starts clean with zero migration errors
- [ ] `npx tsc --noEmit` zero errors
- [ ] `./run-tests.sh unit` zero failures

**After all items confirmed:** replace this block with:
`## PROMPT-003 — DONE — DB Integrity + Backtest Engine Fixes + Data Services`
