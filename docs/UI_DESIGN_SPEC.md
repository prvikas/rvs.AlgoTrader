# UI Design Spec — Professional Trader Dashboard

> Target: Bloomberg / Zerodha Kite Pro aesthetic.
> Dense, dark, numeric-first. Every pixel earns its place.

---

## 1. Design Principles

| Principle | Constraint |
|---|---|
| Data density | Max content per viewport — no decorative whitespace |
| Numbers first | Tabular numerics, monospace values, right-aligned P&L |
| Dark terminal | `#090910` base, not `#0f0f1a` — deeper black |
| Minimal chrome | No emoji icons in nav, no section headers with h2 + 20px margin |
| Micro interactions | Hover row highlight, subtle transitions ≤ 150ms only |
| Color semantics | Green `#00d07a` / Red `#ff4757` / Muted `#4a5568` — never pastels |

---

## 2. Layout Blueprint

```
┌──────────────────────────────────────────────────────────────────┐
│ TOPBAR  [Logo · Nav tabs]          [Market Status · Broker · Clk] │ h=36px
├──────────────────────────────────────────────────────────────────┤
│ KILL / COLD RESTART BANNER (only when active)                    │ h=32px
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  CONTENT AREA  (scrolls vertically)                              │
│  padding: 12px 16px                                              │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

**No left sidebar.** Replace with a compact horizontal top-nav with text tabs.
This frees ~200px of horizontal real estate for data.

---

## 3. Top Navigation Bar

```tsx
// Single row, 36px tall
<header style={{ height: 36, background: '#0d0d17', borderBottom: '1px solid #1a1a2e',
  display: 'flex', alignItems: 'center', padding: '0 16px', gap: 0 }}>
  
  {/* Brand */}
  <span style={{ fontSize: 13, fontWeight: 800, color: '#e2e8f0', letterSpacing: '0.05em', marginRight: 24 }}>
    RVS
  </span>

  {/* Nav tabs */}
  {NAV_TABS.map(tab => (
    <NavTab key={tab.id} ... />
  ))}

  {/* Right cluster */}
  <div style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: 12 }}>
    <MarketClock />      {/* IST HH:MM:SS, ticks every second */}
    <MarketStatus />     {/* ● OPEN / ○ CLOSED */}
    <BrokerStatusBar />  {/* compact */}
    <SignalRDot />       {/* ● live / ○ disc */}
    <LogoutBtn />
  </div>
</header>
```

**NavTab style:**
```tsx
const isActive = activePage === tab.id
return (
  <button style={{
    height: 36, padding: '0 14px', background: 'transparent',
    color: isActive ? '#e2e8f0' : '#4a5568',
    borderBottom: isActive ? '2px solid #3b82f6' : '2px solid transparent',
    border: 'none', borderRadius: 0, cursor: 'pointer',
    fontSize: 12, fontWeight: isActive ? 700 : 500,
    letterSpacing: '0.03em', textTransform: 'uppercase',
    transition: 'color 0.1s',
  }}>
    {tab.label}
  </button>
)
```

Nav tabs (left → right):
`Portfolio | Strategies | Orders | Lab | Backtest | Fwd Test | Instruments | Universe | Inst. Types | Master Data | Settings`

---

## 4. Content Area

```tsx
<main style={{ flex: 1, overflowY: 'auto', padding: '12px 16px', background: '#090910' }}>
```

- **Padding:** `12px 16px` (was `20px`)
- **Background:** `#090910` (was `#0f0f1a`)
- **Gap between sections:** `12px` (was `20px`)

---

## 5. Metric Cards — Portfolio

```tsx
// 4-card row, compact height
<div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 8, marginBottom: 12 }}>
  <MetricCard label="P&L Realized" value="+₹12,450" sub="Today" color="#00d07a" />
  ...
</div>

function MetricCard({ label, value, sub, color }) {
  return (
    <div style={{
      background: '#0d0d17', border: '1px solid #1a1a2e',
      borderRadius: 6, padding: '10px 14px',
    }}>
      <div style={{ fontSize: 10, color: '#4a5568', fontWeight: 700,
        textTransform: 'uppercase', letterSpacing: '0.08em', marginBottom: 4 }}>
        {label}
      </div>
      <div style={{ fontSize: 22, fontWeight: 800, color,
        fontFamily: '"JetBrains Mono", "Fira Code", monospace', lineHeight: 1 }}>
        {value}
      </div>
      {sub && <div style={{ fontSize: 10, color: '#374151', marginTop: 3 }}>{sub}</div>}
    </div>
  )
}
```

**Key changes from current:**
- Height from ~82px → ~68px
- Font size from 26px → 22px
- Gap 12px → 8px
- Padding 16px/20px → 10px/14px
- Background `#1e1e2e` → `#0d0d17`
- Number font: monospace (JetBrains Mono → system fallback)

---

## 6. Data Tables

```tsx
// Table wrapper — no outer card padding wasted
<div style={{ border: '1px solid #1a1a2e', borderRadius: 6, overflowX: 'auto' }}>
  <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
    <thead>
      <tr style={{ background: '#0d0d17', borderBottom: '1px solid #1a1a2e' }}>
        <th style={{ padding: '6px 10px', color: '#4a5568', fontWeight: 600,
          textAlign: 'left', fontSize: 10, textTransform: 'uppercase',
          letterSpacing: '0.07em', whiteSpace: 'nowrap' }}>
          {col}
        </th>
      </tr>
    </thead>
    <tbody>
      <tr style={{ borderBottom: '1px solid #0f0f18' }}
        onMouseEnter={e => e.currentTarget.style.background = '#0d0d1a'}
        onMouseLeave={e => e.currentTarget.style.background = 'transparent'}>
        <td style={{ padding: '5px 10px', color: '#e2e8f0' }}>{val}</td>
      </tr>
    </tbody>
  </table>
</div>
```

**Key changes:**
- Row padding: `12px` → `5px 10px`
- Header font: 13px → 10px uppercase
- Row hover: subtle `#0d0d1a` (not `#252538`)
- Separators: `#2d2d3f` → `#0f0f18` (nearly invisible, like Bloomberg)

---

## 7. P&L Number Formatting

All monetary values:
```tsx
// Use tabular-nums for fixed-width columns
const NUM_STYLE: React.CSSProperties = {
  fontFamily: '"JetBrains Mono", "Cascadia Code", "Fira Code", monospace',
  fontVariantNumeric: 'tabular-nums',
}

function PnlCell({ value }: { value: number }) {
  const color = value > 0 ? '#00d07a' : value < 0 ? '#ff4757' : '#4a5568'
  return (
    <span style={{ ...NUM_STYLE, color, fontWeight: 600 }}>
      {value >= 0 ? '+' : ''}{formatInr(value)}
    </span>
  )
}
```

---

## 8. Section Labels (replace SectionHeader)

```tsx
// Instead of <h2> + 20px margin, use a thin label bar
function SectionLabel({ title, action }: { title: string; action?: ReactNode }) {
  return (
    <div style={{
      display: 'flex', justifyContent: 'space-between', alignItems: 'center',
      marginBottom: 8, paddingBottom: 6,
      borderBottom: '1px solid #1a1a2e',
    }}>
      <span style={{ fontSize: 11, fontWeight: 700, color: '#4a5568',
        textTransform: 'uppercase', letterSpacing: '0.08em' }}>
        {title}
      </span>
      {action}
    </div>
  )
}
```

---

## 9. Forms — Slide-in Right Drawer

Replace inline expanded-div forms (Strategy Create, Backtest Run) with a right-side drawer:

```tsx
{showForm && (
  <div style={{
    position: 'fixed', top: 36, right: 0, bottom: 0,
    width: 520, background: '#0d0d17',
    borderLeft: '1px solid #1a1a2e',
    zIndex: 200, overflowY: 'auto',
    padding: '16px 20px',
    boxShadow: '-8px 0 32px rgba(0,0,0,0.6)',
    display: 'flex', flexDirection: 'column', gap: 12,
  }}>
    <DrawerHeader title="New Strategy" onClose={() => setShowForm(false)} />
    {/* form fields */}
  </div>
)}
// Semi-transparent overlay behind drawer
{showForm && (
  <div onClick={() => setShowForm(false)} style={{
    position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.4)', zIndex: 199,
  }} />
)}
```

**Benefits:**
- Main content table stays visible behind drawer
- 520px is enough for all strategy/backtest fields
- Feels like TradingView's right panel

---

## 10. Strategy / Scenario / Deployment Domain Model

> This is the canonical mental model for the Strategies page. Any UI that mixes these layers is incorrect.

```
Strategy (defines logic — no symbol, no broker)
  └── Scenario (parameter overrides only — no new indicators)
        └── Deployment (symbol + timeframe + broker + capital + schedule)
              └── Run (backtest result | forward test result | live status)
```

### Strategy layer
- Fields: Name, Description, StrategyType, DefaultParameters
- No symbol. No broker. No capital.
- Drawer title: "Create Strategy" / "Edit Strategy"

### Scenario layer
- Fields: Name, Description, ParameterOverrides (subset of parent strategy parameters)
- Parameters list is structurally locked — same indicator set as parent strategy
- Cannot add/remove indicators or parameters — only change values
- Each override row shows: `[checkbox] ParamName | Base: {strategyDefault} | → {overrideValue} | type`
- Drawer title: "New Scenario" / "Edit Scenario"
- A read-only inherited block above overrides:
  ```
  INHERITED FROM: {strategyName} · {strategyType}
  Indicators fixed. Only parameter values may differ.
  ```

### Deployment layer
- Fields: Name, Scenario (dropdown), Symbol, Timeframe, Mode, Broker, AllocatedCapital, Schedule
- This replaces the current "New Strategy Instance" modal
- Drawer title: "Create Deployment" / "Edit Deployment"

### Run layer
- Created by: Run Backtest | Promote to Forward Test | Go Live
- Stored as backtest_runs or forward_test_runs records
- Shown in: Scenarios tab (inline result chips) + Compare tab (full metrics table)

---

## 11. Strategies Page — 4-Tab Layout

```
┌─ Strategy Card List (left, fixed) ──────────────────────────────┐
│ [AlertCandleAxis]       Scheduled                               │
│ [EM_VWAP_Axis]          Draft                                   │
│ [test]                  Draft               [+ NEW]            │
└─────────────────────────────────────────────────────────────────┘

┌─ Selected Strategy Detail (centre, scrollable) ─────────────────┐
│ AlertCandleAxis — AlertCandleShort                              │
│                                                                 │
│  [Definition] [Scenarios] [Deployments] [Compare]              │
│  ──────────────────────────────────────────────────            │
│  (tab content)                                                  │
└─────────────────────────────────────────────────────────────────┘
```

**Tab: Definition** — read-only view of strategy type + all default parameters  
**Tab: Scenarios** — list of scenarios, inline result chips, actions (Run / Edit / Fwd Test / Delete)  
**Tab: Deployments** — list of deployments bound to this strategy  
**Tab: Compare** — side-by-side metric table across scenarios and run types  

---

## 12. Compare Tab — Metric Table

Minimum metrics to show per run:

| Metric | Description |
|---|---|
| Net Return | Total % return over period |
| Sharpe Ratio | Risk-adjusted return (annualised) |
| Max Drawdown | Peak-to-trough % loss |
| Win Rate | % of profitable trades |
| Profit Factor | Gross profit / gross loss |
| Trade Count | Number of completed trades |
| Avg Expectancy | Avg ₹ per trade |
| BT→FT Ratio | fwd_return / backtest_return — flag < 0.70 red |

Comparison modes:
- Scenario vs Scenario (same run type)
- Backtest vs Forward Test (same scenario)
- Current vs Previous run (after parameter edit)

Best value per row highlighted: `background: '#0a2218'` (subtle green)  
Worst value: `background: '#1a0a0a'` (subtle red)  
All numbers: monospace, right-aligned, tabular-nums  

---

## 13. Strategy Status Chips (compact)

```tsx
// Consistent status set across Strategy, Scenario, Deployment, Run
const STATUS_CHIP = {
  Draft:          { bg: '#111120', color: '#4a5568', border: '#4a556830' },
  Running:        { bg: '#1a1209', color: '#f59e0b', border: '#f59e0b30' },  // pulse animation
  Backtested:     { bg: '#0a1829', color: '#3b82f6', border: '#3b82f630' },
  'Fwd Testing':  { bg: '#1a1209', color: '#f59e0b', border: '#f59e0b30' },
  Scheduled:      { bg: '#0a1829', color: '#3b82f6', border: '#3b82f630' },
  Live:           { bg: '#0a2218', color: '#00d07a', border: '#00d07a30' },
}
```

---

## 14. Color Tokens

```ts
export const C = {
  bg:        '#090910',  // page background
  surface:   '#0d0d17',  // cards, panels
  surface2:  '#111120',  // table headers, nested sections
  border:    '#1a1a2e',  // dividers
  border2:   '#0f0f18',  // table row separators
  textPrimary:  '#e2e8f0',
  textSecondary:'#94a3b8',
  textMuted:    '#4a5568',
  green:  '#00d07a',
  red:    '#ff4757',
  blue:   '#3b82f6',
  amber:  '#f59e0b',
  accent: '#3b82f6',
}
```

---

## 15. Responsive Behavior

- Minimum supported width: **1280px** (professional traders use wide monitors)
- No mobile breakpoints needed
- Horizontal scrolling tables are acceptable — do not collapse columns

---

## 16. Font Loading

Add to `index.html`:
```html
<link rel="preconnect" href="https://fonts.googleapis.com">
<link href="https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;600;700&family=Inter:wght@400;500;600;700;800&display=swap" rel="stylesheet">
```

Apply globally in `index.html` or `main.tsx`:
```css
* { font-family: 'Inter', system-ui, sans-serif; }
.num, td.num, .metric-value { font-family: 'JetBrains Mono', monospace; }
```

---

## 17. What NOT to Do

- ❌ No `padding: '20px'` on content areas
- ❌ No `fontSize: 26px` on metric values
- ❌ No `gap: 20px` between cards
- ❌ No `marginBottom: 20px` on section headers
- ❌ No emoji icons in navigation
- ❌ No `backgroundColor: '#1e1e2e'` (too light) — use `#0d0d17`
- ❌ No inline expanded forms — use drawers
- ❌ No `height: 100vh` flex sidebar layout — use top nav + full-width content
- ❌ No mixing Strategy creation with Deployment creation in one modal
- ❌ No adding new indicators inside a Scenario drawer
- ❌ No raw hex values in components — always reference tokens from `frontend/src/styles/tokens.ts`

---

## 18. Implementation Order

1. `index.html` — add font imports + CSS reset
2. `main.tsx` — global body background `#090910`
3. `Dashboard.tsx` — replace sidebar + header with top-nav (Section 3)
4. `PortfolioOverview.tsx` — redesign MetricCard + StrategyTable (Sections 5–6)
5. All other pages — apply table row padding + SectionLabel (Sections 6, 8)
6. `StrategiesPage.tsx` — 4-tab layout (Section 11)
7. `StrategyDefinitionDrawer.tsx` — CREATE (strategy logic only, no deployment fields)
8. `ScenarioDrawer.tsx` — CREATE (parameter overrides only, locked indicator set)
9. `DeploymentsTab.tsx` — CREATE (replaces "New Strategy Instance" modal)
10. `CompareTab.tsx` — CREATE (first-class research surface)
11. `ScenariosTab.tsx` — refactor with result chips and new columns
12. Color token file `src/styles/tokens.ts` (Section 14)
