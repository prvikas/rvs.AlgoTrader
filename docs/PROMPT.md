# PROMPT.md — Next Actions Backlog

> **Rule:** Delete or mark DONE each item immediately after implementation is confirmed.
> Add new items at the bottom under a new PROMPT-NNN block.
> Do NOT keep completed history here — move done items to `CRITICAL_BUGS_FIXED.md` if needed.

---

## PROMPT-011 — Broker Auth & Live Mode Unblock
**Priority: 🔥 CRITICAL — Live/Forward mode is blocked without this**

### E1 — Zerodha Kite Token Refresh
- Complete OAuth TOTP login flow end-to-end
- Persist access token via `ITokenStore` (RedisEncryptedTokenStore, AES-256-GCM)
- Auto-refresh before 09:00 IST each trading day via Hangfire recurring job
- Return HTTP 423 with clear message when token missing/expired
- Acceptance: `BrokerRequired=true` mode starts successfully; LiveEngine gates pass

### E2 — Upstox v3 OAuth Flow
- Complete auth code → access token exchange
- Persist + auto-refresh same pattern as Zerodha
- `ITokenStore` must support multi-broker key namespacing
- Acceptance: Upstox broker client passes broker connection health check at `/api/readiness/pre-market`

### E3 — mStock Instrument Parsing Fix
- Fix mStock master data parsing (broken columns per IMPLEMENTATION_STATUS)
- Add missing DB columns via migration 039 if needed
- Acceptance: mStock instrument seeding populates `instrument_universe` without errors

---

## PROMPT-012 — Deferred Architecture Items (from PROMPT-008/010)
**Priority: 🔥 HIGH — required for spread strategies to work correctly end-to-end**

### SS-1 — MaxLossMultiple Enforcement
- Wire `MaxLossMultiple` check into `SpreadOrderManager` polling loop
- Hook into `ForwardTestEngine` to close spread when unrealised loss exceeds `MaxLoss × NetCredit`
- Acceptance: Short Straddle/Strangle auto-closes on max loss breach in both Paper and Live mode

### CS-1 — Dual-Expiry StrategyContext for Calendar Spread
- Extend `StrategyEvaluationQueue` to pre-fetch both near and far expiry chains
- Add `NearExpiryChain` + `FarExpiryChain` to `StrategyContext`
- Migration 039/040 if new columns needed on context snapshot
- Acceptance: CalendarSpreadStrategy receives both chains in `EvaluateAsync`

### CS-2 — IV Term-Structure Slope Filter
- Compute `FarIv - NearIv` slope in CalendarSpreadStrategy
- Only enter when slope > configurable threshold (default: +2%)
- Acceptance: CalendarSpread unit test verifies slope filter blocks flat-IV entries

### ALL-SPREADS-1 — SpreadBacktestEngine (Bounded Context)
- Current spread simulation is synthetic; need realistic per-bar leg P&L
- New `SpreadBacktestEngine` that tracks each leg separately with real expiry + mark-to-market
- Acceptance: Iron Condor backtest equity curve matches manual calculation on 3-month NIFTY data

### IT-1 — SpreadOrderManager Integration Test
- Build `MockBrokerClient` for integration tests
- Cover: place spread → partial fill → rollback → close full cycle
- Acceptance: integration test in `tests/integration/` passes in CI

---

## PROMPT-013 — Data Layer Completions (v0.2)
**Priority: ⚡ HIGH — multi-year backtests are inaccurate without this**

### DL-1 — Corporate Action Adjustments
- `IHistoricalDataManager` must apply splits, dividends, bonus adjustments to OHLCV history
- Source: NSE corporate actions CSV from bhavcopy
- Acceptance: RELIANCE 2022 2:1 split reflected correctly in pre-split candle prices

### DL-2 — Data Manager UI Page
- React page: gap detection report, CSV bulk import trigger, data quality dashboard
- Show `ohlcv_bars` row count per symbol, last updated date, gap list
- Acceptance: Admin can import NSE bhavcopy CSV and see gap report without CLI

### DL-3 — Option Chain WebSocket Feed (Forward Mode)
- Replace polling with WebSocket subscription for live option chain in `ForwardTestEngine`
- Acceptance: Option chain latency in Forward mode < 500ms (currently up to 5s on polling)

---

## PROMPT-014 — Multi-Timeframe & Signal Quality (v0.6)
**Priority: ⚡ MEDIUM — improves strategy win rate significantly**

### MTF-1 — MTF Alignment Filter
- Block 5m buy signal if 15m price is below EMA21
- Block 5m sell signal if 15m price is above EMA21
- Config flag `MtfAlignmentRequired` on each strategy (default: false, opt-in)
- Acceptance: VCP unit test shows MTF filter blocks counter-trend entries

### MTF-2 — Strategy Regime Filter
- Inject `MarketRegime` (Bull / Bear / CrashMode) into every `EvaluateAsync` call
- Each strategy declares `AllowedRegimes`; signals blocked outside allowed regimes
- Acceptance: Short Straddle blocks entry when `MarketRegime = CrashMode`

---

## PROMPT-015 — Test Coverage Gaps
**Priority: ⚡ MEDIUM — CI reliability**

### TEST-1 — Live Spread Routing Integration Test
- `LiveExecutionEngine` spread path: gates 0–2 → `ISpreadOrderManager` → live chain lookup
- Mock `IBrokerClient` returning realistic fills
- Acceptance: `tests/integration/LiveSpreadRoutingTests.cs` passes in CI

### TEST-2 — StrategyEvaluationQueue Integration Test
- Full candle → queue → context build → strategy evaluate → signal → order path
- Cover both single-leg and spread signal types
- Acceptance: `tests/integration/StrategyEvaluationQueueTests.cs` passes in CI

### TEST-3 — Broker Auth Unit Tests
- Token store encrypt/decrypt round-trip
- Token refresh job triggers before 09:00 IST
- HTTP 423 returned when token missing
- Acceptance: 10+ unit tests covering all token lifecycle states

---

## PROMPT-016 — UI Completion (v0.8 Admin UI)
**Priority: 📋 NORMAL — needed for platform to be demo/production-ready**

### UI-1 — Data Manager Page
- Visual gap detection, CSV import, row count per symbol, data quality score
- Trigger `IHistoricalDataManager` jobs from UI

### UI-2 — Event Calendar Page
- Show RBI MPC, FOMC, earnings, NSE F&O expiry in calendar view
- Mark upcoming events on backtest chart as vertical dashed lines

### UI-3 — Risk Dashboard Completion
- Real-time portfolio delta, margin utilisation, daily P&L vs DLL circuit breaker
- Strategy-level P&L breakdown, kill-switch button per strategy

### UI-4 — Smoke Tests
- End-to-end: login → run backtest → view results → promote to paper
- Acceptance: `tests/smoke/` suite passes against staging environment

---

## PROMPT-013 — DONE (2026-04-17)

- CAP-1: `AllocateCapitalHandler` was a stub returning `true` without writing to DB.
  Fix: uses `ICapitalAllocationRepository` — upserts a `CapitalAllocation` record (create on first
  call, `UpdateAllocation` on subsequent). `IClock` injected for `CreatedAt`/`UpdatedAt` (AP-001).
  Added `AllocateCapitalValidator` (StrategyInstanceId NotEmpty, Amount > 0).
- CAP-2: `DeallocateCapitalHandler` was a stub. Fix: calls new `DeleteByInstanceAsync` on
  `ICapitalAllocationRepository`. Interface + `EfCapitalAllocationRepository` + stub updated.
- SDP-2: `SymbolDataPreferencesService.BuildDefault` used `DateTime.UtcNow` (AP-001). Fix: inject
  `IClock`; use `clock.NowInstant().ToDateTimeUtc().AddYears(-1)` for default from-date.
- DEAD-1: `SetAppConfigHandler` used `IAppConfigRepository` (in-memory singleton stub), silently
  discarding config writes made via `SetAppConfigCommand`. Fix: routes through `IAppConfigService`
  (DB + Redis write-through, same as `SettingsController` path).

---

## PROMPT-012 — DONE (2026-04-17)

- AC-1: `AppConfigService.GetAsync` returned `default` on Redis miss — config lost after Redis
  restart. Fix: write-through to `app_config` table (migration 040); `GetAsync` falls back to DB
  on Redis miss and warms Redis with 5-min TTL. `SetAsync` now writes DB first, then Redis.
- SDP-1: `SymbolDataPreferencesService` was an in-memory singleton — data lost on app restart.
  Fix: persist to `symbol_data_preferences` table (migration 040); full CRUD via raw Npgsql.
  Registration changed from Singleton to Scoped.
- BS-2: Stale `TODO-BS-2` comment removed from `OptionLegSelector.SelectByDelta` — callers
  (SpreadOrderManager) already pass `atmIvFraction` extracted from DiagnosticsJson (fixed PROMPT-009).

---
## PROMPT-011 — DONE (2026-04-17)

Implemented two Npgsql 9 InvalidCastException bugs on raw NpgsqlConnection paths:

- SCN-1: `StrategyDefinitionScenarioService.MapRow` — `GetFieldValue<DateTimeOffset>` on `timestamptz`
  columns 10/12/13 (last_run_at, created_at, updated_at) throws InvalidCastException because Npgsql 9
  maps `timestamptz` → `DateTime` (UTC kind) on raw connections, not `DateTimeOffset`.
  Fix: `new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(N), DateTimeKind.Utc))`.

- OCS-1: `OptionChainSnapshotRepository.GetRangeAsync` — `GetFieldValue<LocalDate>` on `date`
  columns 0/1 (snapshot_date, expiry_date) throws InvalidCastException because raw `NpgsqlConnection`
  has no NodaTime type mapper (only EF Core connection uses `.UseNodaTime()`).
  Fix: read as `DateOnly` (Npgsql 9 native mapping for `date`), convert via
  `private static LocalDate ToLocalDate(DateOnly d) => new(d.Year, d.Month, d.Day)`.

---

## PROMPT-009 — DONE (2026-04-07)

### PERF-2 — TimescaleDB Compression Verification
- Confirm compression job running for chunks older than 7 days
- Target: compression ratio > 80%
- Add Grafana panel showing compression ratio trend

### PERF-3 — Redis Unbounded Key Audit
- Run `SCAN` + `TTL` audit; any key without TTL must be documented or given expiry
- Acceptance: zero TTL-less keys in Redis after fix
