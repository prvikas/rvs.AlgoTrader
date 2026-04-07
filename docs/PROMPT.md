# PROMPT.md — Precision AI Coding Prompt

> Read this file only when explicitly requested via `docs/PROMPT.md`.
> Delete or stub each entry immediately after implementation is confirmed done.

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
