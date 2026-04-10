## Rule

Record only changed or new requirements.

Do not restate stable architecture.


## 2026-04-09

### Audit gaps found in StrategyEvaluationQueue (SEQ-1, SEQ-2)

**SEQ-1** `StrategyEvaluationQueue` sets `OptionChain: null` in the StrategyContext it builds.
IOptionChainService is not injected into the queue. Architecture comment in OptionChainService says
it must be called by the queue before building context (Rule #18) — this is not implemented.
All 6 option strategies (IronCondor, ShortStraddleStrangle, CalendarSpread, FibOptionSpread,
IntradayPcrOptions, VerticalSpread) immediately return Skip in both Live and Forward modes.

**Required change:** Inject optional IOptionChainService into StrategyEvaluationQueue. For each
strategy instance, detect whether it is an option strategy (check StrategyInstance.StrategyType or
presence of SpreadEntry signal on context preview) and fetch near+far chains before building context.

**SEQ-2** `StrategyEvaluationQueue.EvaluateInstanceAsync` gates the call to
`forwardTestEngine.ProcessCandleAsync` on `isActionable` (BUY/SELL from queue's own evaluation).
ForwardTestEngine fetches its own option chains and re-evaluates independently, but it is never
called because the queue's evaluation (without OptionChain) returns Skip/Hold first.

**Required change:** For Forward mode instances, always call ProcessCandleAsync regardless of the
queue's own signal result. The queue's evaluation is useful only for signal journaling and live
execution routing; ForwardTestEngine manages its own state machine.

### Bugs fixed this session (2026-04-09)

- **P&L formula (credit/debit spreads):** `currentValue − |NetCredit|` for debit spreads was wrong.
  Unified to `NetCredit − currentValue` for all spread types (BacktestEngine + ForwardTestEngine).
- **Per-leg TTE in PriceSpreadSim:** CalendarSpread near+far legs were priced with the same TTE.
  Fixed by storing `LegExpiry` per leg in `OpenSpreadSim.Legs` and using it in PriceSpreadSim.
- **FtSpreadPnl simplification:** Removed incorrect ternary; at expiry P&L = NetCredit (linear approx).

### Known limitations (accepted, not bugs)

- ForwardTestEngine uses linear DTE decay (not B-S) for per-bar spread value — far leg vega not captured.
- StrikeInterval hardcoded to 50 (NIFTY default) in ForwardTestEngine. BANKNIFTY needs 100.
  Override by adding `"StrikeInterval": 100` to strategy ParametersJson.
- BacktestEngine.NearestMonthlyExpiry (last Thursday of next month from bar date) differs from
  OptionChainService.GetNearestMonthlyExpiry (last Thursday of current month if not yet passed).
  No correctness impact for backtesting; divergence acknowledged.

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



