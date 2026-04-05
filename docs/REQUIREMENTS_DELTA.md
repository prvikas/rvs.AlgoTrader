## Rule

Record only changed or new requirements.

Do not restate stable architecture.


## 2026-04-05

### Strategy / Scenario / Deployment Model Redesign

**Owner vision confirmed (quant research workflow):**
- Strategy = fixed indicator stack + default parameters. No symbol. No broker.
- Scenario = parameter overrides on existing strategy. Cannot add/remove indicators.
- Deployment = symbol + timeframe + broker + capital + schedule bound to a scenario.
- Compare = first-class research tab showing backtest vs forward test metrics side-by-side.

**UI changes required:**
- StrategiesPage: 4-tab layout per strategy (Definition | Scenarios | Deployments | Compare)
- Strategy Definition Drawer: name, description, strategy type, default parameters only
- Scenario Drawer: locked parameter list from parent strategy — override values only
- Deployment Drawer: replaces current "New Strategy Instance" modal
- Compare Tab: metric table with Sharpe, Max DD, Win%, Profit Factor, BT→FT ratio

**Precision AI prompt for implementation:** see `docs/PROMPT.md` → PROMPT-001

**Status chips standardised:** Draft | Running | Backtested | Fwd Testing | Scheduled | Live

**Hard rules added:**
- AP-023: Scenario drawer must never allow adding indicators — parameter list is structurally locked to parent strategy
- AP-024: Strategy creation form must not include symbol, broker, capital, or schedule fields
- AP-025: Compare tab is mandatory on Strategies page — not optional or secondary


## 2026-03-30

### Critical Bugs Found

**Negative Returns in All Strategies**
- Position sizing ignores entry price scale (tight-stop strategies over-leveraged 10–100×)
- Transaction costs applied only on exit, not entry (₹20 actual, ₹10 deducted → equity inflation)
- No parameter validation after deserialization (e.g., SMA200Period=0 on empty `{}`)

**Strategy Creation UX Broken for Novices**
- Parameter editor blank after strategy selection (no schema fetch, no defaults shown)
- User must guess parameter meanings with zero guidance
- Empty `strategyParams = {}` passes validation but strategy behaves with unintended defaults

### Frontend Changes Needed
1. Fetch schema on strategy type change
2. Populate editor with defaults + descriptions
3. Validate before submit

### Backtest Engine Fixes Needed
1. Position size = risk / (stop distance × entry price) [or ATR-based]
2. Deduct entry costs on trade open, exit costs on close
3. Strategy config validation in FromJson() — no zero parameters

## 2026-03-25



\### Product focus

\- prioritize backtesting, forward testing, and live deployment

\- ignore DB table redesign unless explicitly requested

\- build on existing code already present in repo

\- optimize docs for Claude token efficiency



\### Claude behavior

\- inspect code before proposing changes

\- update requirements based on prompt deltas

\- learn through explicit written memory and status docs

\- prefer small safe changes over large rewrites



\### Strategy roadmap

\- implement VCP swing strategy

\- implement Fibonacci hedged option spread strategy

\- implement intraday PCR/OI/VWAP/gamma strategy



\### Future scope

\- screener

\- news

\- events

\- analytics



