# PROMPT.md — Precision AI Coding Prompt

> Read this file only when explicitly requested via `docs/PROMPT.md`.
> Delete or stub each entry immediately after implementation is confirmed done.

---

## PROMPT-001 — Strategy & Scenario Model Redesign
**Status:** PENDING

**Context:**
The Strategies page conflates strategy definition, deployment config, and scenario variants into
overlapping modals. This prompt separates them into four clean concepts:

1. **Strategy** — fixed indicator stack + entry/exit logic. No symbol, broker, or capital.
2. **Scenario** — parameter overrides only. Cannot add/remove indicators.
3. **Deployment** — binds a strategy to symbol, timeframe, broker, capital, schedule.
4. **Run** — a backtest or forward-test execution result.

Compare tab is a first-class research surface, not a secondary button.

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
| Live | `C.green` | |

---

### Shared TypeScript interfaces
Create `frontend/src/types/strategy.ts` with these interfaces. All components import from here.

```ts
export interface StrategyParam {
  key: string
  label: string
  description?: string
  type: 'int' | 'decimal' | 'bool'
  defaultValue: number | boolean
}

export interface Strategy {
  id: string
  name: string
  description?: string
  strategyType: string        // e.g. "AlertCandleShort"
  strategyTypeLabel: string
  defaultParameters: Record<string, number | boolean>
  paramSchema: StrategyParam[]
  status: 'Draft' | 'Backtested' | 'Fwd Testing' | 'Live'
  createdAt: string           // ISO string — never Date object
}

export interface Scenario {
  id: string
  strategyId: string
  name: string
  description?: string
  paramOverrides: Record<string, number | boolean>  // only overridden keys
  status: 'Draft' | 'Running' | 'Backtested' | 'Fwd Testing' | 'Live'
  lastRunAt?: string
  metrics?: RunMetrics
}

export interface Deployment {
  id: string
  strategyId: string
  scenarioId: string
  name: string
  symbol: string              // e.g. "NSE:AXISBANK"
  timeframe: '1m'|'3m'|'5m'|'15m'|'30m'|'1h'|'1d'
  mode: 'Backtest'|'Forward Test'|'Live'
  broker: 'MStock'|'Zerodha'|'Upstox'
  allocatedCapital: number
  schedule: { days: string[]; startTime?: string; endTime?: string }
  status: 'Draft' | 'Running' | 'Backtested' | 'Fwd Testing' | 'Live'
}

export interface RunMetrics {
  returnPct: number
  sharpe: number
  maxDrawdownPct: number
  winRate: number
  profitFactor: number
  tradeCount: number
  avgExpectancy: number
  btToFtRatio?: number        // forwardReturnPct / backtestReturnPct — undefined if no fwd run
}
```

---

### API endpoints reference

| Method | Path | Used in |
|---|---|---|
| GET | `/api/strategy-types` | Task 2 dropdown |
| GET | `/api/strategy-types/{type}/schema` | Task 2 param schema |
| POST | `/api/strategies` | Task 2 create |
| PUT | `/api/strategies/{id}` | Task 2 edit |
| GET | `/api/strategies/{id}/scenarios` | Task 3, 6 |
| POST | `/api/strategies/{id}/scenarios` | Task 3 create |
| PUT | `/api/strategies/{id}/scenarios/{sid}` | Task 3 edit |
| GET | `/api/strategies/{id}/deployments` | Task 4 |
| POST | `/api/strategies/{id}/deployments` | Task 4 create |
| DELETE | `/api/strategies/{id}/deployments/{did}` | Task 4 delete |
| GET | `/api/strategies/{id}/runs` | Task 5 compare |

Mock shape for missing endpoints must exactly match the interfaces in `strategy.ts` above.

---

### Loading and error states (required on ALL data-fetching components)
- Show `<SkeletonRow />` (or equivalent) while fetching — mirror the real layout shape
- On error: inline `<ErrorMessage text="Failed to load. Retry?" onRetry={refetch} />` — no raw alert()
- Empty state: descriptive message + primary action button — never just blank space

---

### Task 1 — StrategiesPage layout

Modify `frontend/src/pages/StrategiesPage.tsx`.

**Left panel:**
- Strategy card shows: name, strategyTypeLabel, status chip
- Remove symbol · timeframe subtitle (belongs to Deployment)
- `+ NEW` opens Strategy Definition drawer

**Centre panel — 4 tabs:**
`Definition | Scenarios | Deployments | Compare` — default tab: `Scenarios`

Remove inline "Promote to Forward Test" stepper — move to Deployments tab actions.

**Drawer container:**
One shared `<RightDrawer isOpen={bool} title={string} onClose={fn}>` component, 520px width,
`position: fixed`, right 0, top `NAV_HEIGHT`px, full height, `z-index: 100`.
All three drawers (Tasks 2–4) render inside this single container — only content differs.

---

### Task 2 — StrategyDefinitionDrawer

**File:** `frontend/src/components/strategies/StrategyDefinitionDrawer.tsx` (CREATE)

Props: `{ strategyId?: string; onClose: () => void }`
(strategyId present = edit mode, absent = create mode)

Fields:
- Strategy Name * — text input
- Description — textarea (optional)
- Strategy Type * — dropdown from `GET /api/strategy-types`; show type description below
- Parameters — auto-rendered from `GET /api/strategy-types/{type}/schema`
  - Each row: label | editable default value | type badge (int / decimal / bool)
  - bool → checkbox; int/decimal → number input with type badge right-aligned

Style:
- Section label "PARAMETERS" via `<SectionLabel>` component
- Row: font `12px`, padding `TABLE_CELL`, border-bottom `C.border`
- Type badge: `C.textMuted`, `10px`, uppercase

Submit: create → `POST /api/strategies`, edit → `PUT /api/strategies/{id}`
No symbol, timeframe, broker, capital, or schedule fields here.

---

### Task 3 — ScenarioDrawer

**File:** `frontend/src/components/strategies/ScenarioDrawer.tsx` (CREATE)

Props: `{ strategy: Strategy; scenarioId?: string; onClose: () => void }`

Read-only inheritance header (above overrides):
```
INHERITED FROM: {strategy.name} · {strategy.strategyTypeLabel}
Indicators fixed. Only parameter values may differ.
```
Style: color `C.textMuted`, font-style italic, fontSize `10px`

Parameter override rows (one per param in `strategy.paramSchema`):
- Checkbox — checked = override active
- Param label
- `Base: {strategy.defaultParameters[key]}` — color `C.textMuted`, read-only
- Override value input — enabled only when checkbox checked
- Type badge

Rules:
- Param list is structurally locked — no add/remove UI exists
- Only checked params sent in `paramOverrides`
- Unchecked params inherit strategy defaults (not sent in payload)

Submit: create → `POST /api/strategies/{strategyId}/scenarios`
Edit → `PUT /api/strategies/{strategyId}/scenarios/{scenarioId}`

---

### Task 4 — DeploymentsTab

**File:** `frontend/src/components/strategies/DeploymentsTab.tsx` (CREATE)

Props: `{ strategy: Strategy }`

Deployment drawer fields:
- Deployment Name *
- Scenario * — dropdown of `strategy.scenarios`
- Symbol * — text input (e.g. `NSE:AXISBANK`)
- Timeframe * — dropdown: `1m 3m 5m 15m 30m 1h 1d`
- Mode * — dropdown: `Backtest | Forward Test | Live`
- Broker * — dropdown: `MStock | Zerodha | Upstox`
- Allocated Capital (₹) * — number input
- Schedule — Mon–Fri checkboxes + optional start/end time (HH:MM)

Deployments table columns:
`Name | Scenario | Symbol | TF | Mode | Broker | Capital | Status | Actions`

Actions per row: `Run Backtest` | `→ Fwd Test` | `Delete`
Existing strategy instances (old model): render as Deployments — map `instanceSymbol → symbol`,
`instanceTimeframe → timeframe`. Do not error on missing scenarioId — show `—` in Scenario column.

---

### Task 5 — CompareTab

**File:** `frontend/src/components/strategies/CompareTab.tsx` (CREATE)

Props: `{ strategyId: string }`

Data: `GET /api/strategies/{id}/runs` → array of `{ scenario: Scenario, metrics: RunMetrics, runType: 'backtest'|'forward' }`

Controls:
- `[+ Add Run]` — opens scenario dropdown to add column (max 5)
- Toggle: `Backtest | Forward Test | Both`

Metrics table — rows fixed, columns = selected runs:

| Metric | Key | Format |
|---|---|---|
| Return | `returnPct` | `+12.4%` |
| Sharpe | `sharpe` | `1.82` |
| Max DD | `maxDrawdownPct` | `-8.2%` |
| Win% | `winRate` | `54%` |
| PF | `profitFactor` | `1.9` |
| Trades | `tradeCount` | `142` |
| Avg Exp | `avgExpectancy` | `₹840` |
| BT→FT | `btToFtRatio` | `0.92` |

Rules:
- Delta column (Δ) = col1 value minus col2 value — shown only when ≥ 2 columns selected
- `btToFtRatio < 0.7` → cell background `C.redBg`, color `C.red`
- Best value per row → cell background `C.greenBg`
- All values: `fontFamily: F.mono`, `textAlign: 'right'`, `fontVariantNumeric: 'tabular-nums'`
- Empty state: "No scenarios have been run yet. Run a backtest to start comparing."

---

### Task 6 — ScenariosTab

**File:** `frontend/src/components/strategies/ScenariosTab.tsx` (CREATE — folder does not exist yet)

Props: `{ strategy: Strategy }`

Table columns:
`Name | Parameters (JSON summary) | Status | Last Run | Return | Sharpe | DD | Actions`

- Parameters cell: truncated JSON of `paramOverrides`, max 60 chars, monospace `10px`
- Return/Sharpe/DD: `F.mono`, right-aligned, green if positive / red if negative
- Status chips: use canonical set from token reference above

Actions: `Run` | `Edit` | `→ Fwd Test` | `Delete`

---

### File checklist

```
frontend/src/types/strategy.ts                 CREATE — shared interfaces
frontend/src/pages/StrategiesPage.tsx          MODIFY — 4-tab layout
frontend/src/components/strategies/
  StrategyDefinitionDrawer.tsx                 CREATE
  ScenarioDrawer.tsx                           CREATE
  DeploymentsTab.tsx                           CREATE
  CompareTab.tsx                               CREATE
  ScenariosTab.tsx                             CREATE
frontend/src/components/ui/RightDrawer.tsx     CREATE if not exists — 520px fixed drawer shell
```

Note: `frontend/src/components/strategies/` does not exist — Claude must create the directory.
`StrategyCard` may be inline in `StrategiesPage.tsx` — check before extracting.

---

### Constraints
- No raw hex anywhere — `C.*` tokens only (AP-020)
- No inline form expansion — right drawers only (AP-022)
- No `DateTime.Now` on backend; frontend receives ISO strings only
- No `"New Strategy Instance"` modal — replaced by Deployment drawer
- No indicator add/remove in Scenario drawer — param list locked to parent strategy
- Table padding `TABLE_CELL`, font `12px`
- Metric values: `F.mono`, `tabular-nums`, right-aligned
- TypeScript strict — no `any`, no implicit undefined
- `cd frontend && npx tsc --noEmit` must pass with zero errors
- `npm run dev` must start with zero console errors

---

### Definition of done
- [ ] `frontend/src/types/strategy.ts` created with all interfaces
- [ ] Strategy creation: name + description + type + default params only
- [ ] Scenario creation: parent indicators read-only, param overrides only
- [ ] Deployment creation: symbol + timeframe + broker + capital + schedule + scenario link
- [ ] Compare tab: metric table with ≥ 2 columns, delta column, BT→FT highlight
- [ ] All 4 tabs render without console errors
- [ ] All forms use `<RightDrawer>` at 520px
- [ ] Status chips consistent across all components
- [ ] Zero raw hex in any new file
- [ ] `npx tsc --noEmit` passes
- [ ] `npm run dev` starts clean

**After implementation confirmed:** replace this entry with a one-line stub:
`## PROMPT-001 — DONE — Strategy & Scenario Model Redesign`
