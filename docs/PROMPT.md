# PROMPT.md — Precision AI Coding Prompt

> Read this file only when explicitly requested via `docs/PROMPT.md`.
> This file contains ready-to-paste, copy-exact prompts for Claude/Copilot agents.

---

## PROMPT-001 — Strategy & Scenario Model Redesign

**Context:**  
The Strategies page currently conflates three distinct concepts — strategy type definition, deployment configuration (symbol/broker/capital/schedule), and scenario parameter variants — into overlapping modals. A professional quant workflow must separate them clearly. The owner's vision is:

1. A **Strategy** defines a fixed indicator stack and entry/exit logic. No trading symbol. No broker. No capital.
2. A **Scenario** is a set of parameter overrides on an existing strategy. It cannot add or remove indicators — only change parameter values of indicators already declared in that strategy.
3. A **Deployment** (currently called "Strategy Instance") binds a strategy to a symbol, timeframe, broker, capital, and schedule.
4. **Backtest and Forward Test comparison** must be a first-class research surface — not a secondary button.

---

### Scope of changes

This prompt covers **frontend only** (React/TypeScript under `frontend/src/`).
Do not change DB schema, API contracts, or backend logic unless explicitly noted.
All API calls must use existing endpoints. If an endpoint is missing, add a `// TODO: API` comment and use mock data.

---

### Task 1 — Strategies Page layout

Refactor `frontend/src/pages/StrategiesPage.tsx` (or equivalent):

**Left panel (strategy list):**
- Each card shows: strategy name, strategy type, status chip (Draft / Backtested / Forward Testing / Live)
- Remove the "symbol · timeframe" subtitle from the strategy card — that belongs to deployments
- Add a `+ NEW` button that opens the Strategy Definition drawer (Task 2)

**Centre panel (strategy detail):**
- When a strategy is selected, show four tabs: `Definition | Scenarios | Deployments | Compare`
- Default to `Scenarios` tab
- Remove the current "Promote to Forward Test" inline stepper from the centre; move that action into the Deployments tab

**Right drawer:**
- 520px width, fixed right, top offset 36px (below topbar), full height — matches AP-022
- Reuse for Strategy Definition, Scenario, and Deployment drawers (different content, same container)

---

### Task 2 — Strategy Definition Drawer

Create `frontend/src/components/strategies/StrategyDefinitionDrawer.tsx`.

Fields:
```
Strategy Name *          text input
Description              textarea (optional)
Strategy Type *          dropdown — fetched from GET /api/strategy-types
                         Each type shows its description below the selection
Parameters               auto-rendered from strategy type schema
                         Show each parameter with: label | base value | type (int/decimal/bool)
                         All parameters visible and editable — these become strategy defaults
```

Rules:
- On strategy type change, fetch parameter schema from `GET /api/strategy-types/{type}/schema`
  (if endpoint missing: mock from existing `StrategyType` objects already in codebase)
- Each parameter row must show: name, description, default value (editable), data type badge
- No symbol, timeframe, broker, capital, or schedule fields here
- Submit button: "Create Strategy" → `POST /api/strategies`
- Edit mode: "Save Strategy" → `PUT /api/strategies/{id}`

Style:
- Section header "PARAMETERS" using `SectionLabel` component
- Parameter rows: `12px` font, `5px 10px` padding, border-bottom `#1a1a2e`
- Checkbox for bool params; number input for int/decimal with type badge on right

---

### Task 3 — Scenario Drawer

Create `frontend/src/components/strategies/ScenarioDrawer.tsx`.

Fields:
```
Scenario Name *          text input
Description              textarea (optional)
Parameter Overrides      checkbox list — same parameters as parent strategy
                         Each row shows:
                           [x] checked = override active
                           Parameter name
                           "Base: {strategy default value}"  (read-only, grey)
                           Override value input (enabled only when checked)
                           Data type badge (int / decimal / bool)
```

Rules:
- Parameters list is **read-only in structure** — no add/remove allowed
- Only parameters where the checkbox is checked are sent as overrides
- Unchecked parameters inherit strategy defaults silently
- A read-only header block above overrides:
  ```
  INHERITED FROM: {strategyName} · {strategyType}
  Indicators fixed. Only parameter values may differ.
  ```
- Submit: "Create Scenario" → `POST /api/strategies/{strategyId}/scenarios`
- Edit: "Save Scenario" → `PUT /api/strategies/{strategyId}/scenarios/{scenarioId}`

Style: matches Task 2 style rules. Inherited block uses `color: #4a5568`, italic, 10px.

---

### Task 4 — Deployments Tab

Create `frontend/src/components/strategies/DeploymentsTab.tsx`.

Replaces the current "New Strategy Instance" modal for broker/symbol/capital/schedule binding.

Deployment drawer fields:
```
Deployment Name *        text input
Scenario *               dropdown — scenarios of this strategy
Symbol *                 text (e.g. NSE:AXISBANK)
Timeframe *              dropdown (1m / 3m / 5m / 15m / 30m / 1h / 1d)
Mode *                   dropdown (Backtest / Forward Test / Live)
Broker *                 dropdown (MStock / Zerodha / Upstox)
Allocated Capital (₹) *  number input
Trading Schedule         weekday checkboxes Mon–Fri + optional start/end time
```

Table in tab shows all deployments for the strategy with columns:
`Name | Scenario | Symbol | TF | Mode | Broker | Capital | Status | Actions`

Actions per row: Run Backtest | Promote to FwdTest | Delete

---

### Task 5 — Compare Tab (first-class research surface)

Create `frontend/src/components/strategies/CompareTab.tsx`.

Layout:
```
┌──────────────────────────────────────────────────────────────────┐
│  COMPARE              [+ Add Run]   [Backtest vs Fwd Test toggle] │
├─────────┬────────────┬──────────────────────────────────────────┤
│ Metric  │ Scenario A │ Scenario B  │ Scenario C  │ Δ A→B        │
├─────────┼────────────┼─────────────┼─────────────┼──────────────┤
│ Return  │ +12.4%     │ +9.8%       │ +15.1%      │ -2.6pp       │
│ Sharpe  │ 1.82       │ 1.41        │ 2.10        │ -0.41        │
│ Max DD  │ -8.2%      │ -11.4%      │ -6.8%       │ -3.2pp       │
│ Win%    │ 54%        │ 49%         │ 57%         │ -5pp         │
│ PF      │ 1.9        │ 1.6         │ 2.1         │ -0.3         │
│ Trades  │ 142        │ 138         │ 151         │ -4           │
│ AvgExp  │ ₹840       │ ₹690        │ ₹980        │ -₹150        │
│ BT→FT   │ 0.92       │ 0.71        │ 0.88        │ —            │
└─────────┴────────────┴─────────────┴─────────────┴──────────────┘
```

Rules:
- Columns are scenarios or runs (user selects from dropdown, up to 5 at once)
- "BT→FT" row = forward_return / backtest_return ratio; highlight < 0.7 in red
- Delta column shows difference from first selected to second selected
- Toggle "Backtest / Forward Test / Both" filters which run type is shown
- Best value in each metric row highlighted with subtle green cell bg `#0a2218`
- All numbers monospace, right-aligned
- If no runs exist: empty state — "No scenarios have been run yet. Run a backtest to start comparing."

---

### Task 6 — Scenarios Tab (list + run)

Refactor or create `frontend/src/components/strategies/ScenariosTab.tsx`.

Preserve existing behavior but update columns:
```
Name | Parameters (JSON summary) | Status | Last Run | Return | Sharpe | DD | Actions
```

Actions: Run | Edit | → Fwd Test | Delete

Status chips: Draft (grey) | Running (amber pulse) | Backtested (blue) | Fwd Testing (orange) | Live (green)

Return/Sharpe/DD values: monospace, color-coded green/red, right-aligned.

---

### Constraints

- **Never** show "New Strategy Instance" modal for first-time strategy creation — that modal is now the Deployment drawer (Task 4)
- **Never** allow adding new indicators inside a Scenario drawer — the parameter list is structurally locked to the parent strategy
- **Never** use inline form expansion — all forms are right drawers (AP-022)
- All colors from `frontend/src/styles/tokens.ts` — no raw hex inline (AP-020)
- No emoji in nav or section headers
- No `DateTime.Now` — all timestamps via `IClock` on backend; frontend receives ISO strings
- Table row padding `5px 10px`, font `12px`
- Metric values: monospace, right-aligned, `fontVariantNumeric: 'tabular-nums'`

---

### File checklist (create or modify)

```
frontend/src/pages/StrategiesPage.tsx          MODIFY — add 4-tab layout
frontend/src/components/strategies/
  StrategyDefinitionDrawer.tsx                 CREATE
  ScenarioDrawer.tsx                           CREATE
  DeploymentsTab.tsx                           CREATE
  CompareTab.tsx                               CREATE
  ScenariosTab.tsx                             CREATE or MODIFY
  StrategyCard.tsx                             MODIFY — remove symbol/TF subtitle
```

---

### Definition of done

- [ ] Strategy creation collects only name, description, type, and default parameters
- [ ] Scenario creation shows parent strategy's indicators read-only + only allows parameter value overrides
- [ ] Deployment creation collects symbol, timeframe, broker, capital, schedule, and links to a scenario
- [ ] Compare tab renders metric table with ≥ 2 scenarios side-by-side
- [ ] All forms are right-side drawers at 520px width
- [ ] Status chips are consistent: Draft / Backtested / Forward Testing / Live
- [ ] No raw hex values in any new component — token references only
- [ ] TypeScript compiles without errors (`cd frontend && npx tsc --noEmit`)
- [ ] `npm run dev` starts without console errors
