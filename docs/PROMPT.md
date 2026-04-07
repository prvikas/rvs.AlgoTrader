# PROMPT.md — Precision AI Coding Prompt

> Read this file only when explicitly requested via `docs/PROMPT.md`.
> Delete or stub each entry immediately after implementation is confirmed done.

---
## PROMPT-007 — DONE (2026-04-07)

Implemented: BJ-1 24h eviction, BJ-2 RunStartedAt, BJ-3 correct downloadTf,
PS-1 Kelly formula, PS-2 AtrMultiplier, BS-1 expired-option delta=0,
SO-1 rollback→reverse PlaceOrder, SO-2 CloseSpread→reverse PlaceOrder, SO-3 lot size,
IC-1 spread detection in BacktestEngine, IC-3 IronCondor AtmIv==0 guard,
CM-1 STT sell-side only, CM-2 options exchange fee 0.053%.

Deferred (need OptionLegSelector re-arch): IC-2/VS-1 FromStrike anchor, BS-2 atmIv param.

---
## PROMPT-008 — DONE (2026-04-07)

Implemented: SS-2 StrangleCallDelta/StranglePutDelta, SS-4 DiagnosticsJson typed dict,
FIB-1 fib1618 formula corrected (uptrend=swingHigh+range*0.618, downtrend=swingLow-range*0.618),
PAB-1 trendEma 0m warmup guard, EV-1 VWAP daily-TF detection (rolling cumulative, no session reset).

Deferred (larger scope): SS-1 MaxLossMultiple enforcement wiring, SS-3 DTE filter,
CS-1 dual-expiry StrategyContext, CS-2 IV term-structure slope filter,
FIB-2 UnderlyingStopLevel in SpreadSignalResult, FIB-3/FIB-4 BacktestEngine iv_history/event-calendar population,
ALL-SPREADS-1 SpreadBacktestEngine, IC-2/VS-1 FromStrike (deferred from P007), EV-3 PCR validation.

---
## PROMPT-009 — Open (2026-04-07)

### Context
All P007 and P008 fixes confirmed in source. The following items were verified
open by direct file review on 2026-04-07.

---

### A — Deferred Carry-Overs (must fix before live)

**IC-2 / VS-1 — OptionLegSelector: FromStrike anchor**
File: `src/rvs.AlgoTrader.Infrastructure/Services/OptionLegSelector.cs`
- `StrikeSelectionMode.FromStrike` is referenced in `OptionsLegSpec` but
  `ResolveAsync` has no branch for it — falls through to ATM.
- Fix: add `case StrikeSelectionMode.FromStrike: return spec.FixedStrike ?? spotPrice;`
  in the strike-picking switch before the OTM percentage path.
- Required by: IronCondor (IC-2) upper wing anchor + VerticalSpread (VS-1).

**BS-2 — atmIv param wired into BlackScholes**
File: `src/rvs.AlgoTrader.Infrastructure/Services/BlackScholesEngine.cs`
- `IBlackScholesEngine.Compute(...)` signature has `volatility` param, but
  callers in `OptionLegSelector` pass `0.18` hardcoded when atmIv is null.
- Fix: callers must pass `signal.AtmIv` (or `strategyContext.AtmIv`) when
  available; fall back to the 0.18 constant only when truly unavailable.
  Add a `// TODO-BS-2` comment when falling back so it surfaces in logs.

---

### B — Spread Strategy Gaps

**SS-1 — ShortStrangle: MaxLossMultiple stop not enforced**
File: `src/rvs.AlgoTrader.Strategies/ShortStrangle/ShortStrangleStrategy.cs`
- `MaxLossMultiple` is parsed from params but never checked in `EvaluateAsync`.
- Fix: after receiving current spread P&L from `StrategyContext.SpreadPnl`,
  if `unrealisedLoss >= premium * MaxLossMultiple` emit a `CloseSpread` signal
  with `Reason = "MaxLossMultiple breached"`.

**SS-3 — ShortStrangle: DTE filter not applied**
File: same as SS-1
- `MinDte` / `MaxDte` parsed but the expiry selection in `EvaluateAsync` uses
  `GetNearestWeeklyExpiry()` unconditionally.
- Fix: filter `OptionChainExpiry` list to those with `DaysToExpiry` in
  `[MinDte, MaxDte]`; pick the nearest that qualifies.

**CS-1 — CalendarSpread: dual-expiry StrategyContext**
File: `src/rvs.AlgoTrader.Strategies/CalendarSpread/CalendarSpreadStrategy.cs`
- Strategy emits a near-leg and a far-leg, but `StrategyContext` only carries
  one `ExpiryDate`. The far-leg expiry is not surfaced.
- Fix: add `FarExpiryDate` to `SpreadSignalResult` (nullable `LocalDate?`).
  `SpreadOrderManager.ExecuteSpreadAsync` must use `leg.NearestWeekly` flag
  to pick near vs far expiry per leg.

**CS-2 — CalendarSpread: IV term-structure slope filter**
File: same as CS-1
- Calendar trades are only profitable when near-term IV > far-term IV
  (contango). No check exists.
- Fix: compute `nearIv - farIv` from `IOptionIvRankService`; skip signal
  if slope ≤ 0 (backwardation). Log skipped count to `SkippedSignalCount`.

**FIB-2 — FibOptionSpread: UnderlyingStopLevel missing from SpreadSignalResult**
File: `src/rvs.AlgoTrader.Strategies/FibOptionSpread/FibOptionSpreadStrategy.cs`
- The underlying price stop is calculated but never written to `SpreadSignalResult`.
- Fix: add `decimal? UnderlyingStopLevel` to `SpreadSignalResult` record.
  `SpreadOrderManager` must set a conditional OCO on the underlying when this
  field is populated.

**FIB-3 — FibOptionSpread: BacktestEngine doesn't populate iv_history**
File: `src/rvs.AlgoTrader.Backtesting/BacktestEngine.cs`
- `StrategyContext` built inside BacktestEngine does not hydrate `AtmIv` from
  `option_iv_history` table — always null in backtest runs.
- Fix: inject `IOptionIvRankService`; in `BuildContextAsync` call
  `GetAsync(symbol)` and populate `context.AtmIv` when available.
  Guard with null check so backtests with no iv_history still run.

**FIB-4 — FibOptionSpread: event-calendar not populated in backtest**
File: same as FIB-3
- `StrategyContext.UpcomingEvents` always empty in backtest.
- Fix: inject `IEventCalendarRepository`; query events within
  `[barDate, barDate + 7 days]` and populate context. Cache per-day to
  avoid per-bar DB calls.

---

### C — SpreadBacktestEngine (ALL-SPREADS-1)

**ALL-SPREADS-1 — No spread P&L simulation in BacktestEngine**
Files: `src/rvs.AlgoTrader.Backtesting/BacktestEngine.cs`,
       new file `src/rvs.AlgoTrader.Backtesting/SpreadBacktestEngine.cs`
- Current engine handles `SignalResult` (equity trades). `SpreadSignalResult`
  returned by FibOptionSpread / ShortStrangle / IronCondor / CalendarSpread /
  VerticalSpread is silently ignored — no simulated P&L.
- Fix (phased):
  1. Add `ISpreadBacktestSimulator` interface with
     `SimulateSpreadAsync(SpreadSignalResult, StrategyContext, BacktestRequest)`.
  2. Implement `SpreadBacktestSimulator`: for each leg, find the option row in
     `option_iv_history` nearest to `expiryDate`; price using Black-Scholes at
     entry and exit bar; compute premium collected/paid; apply lot size.
  3. In `BacktestEngine.RunAsync`, after strategy evaluates, check result type:
     if `SpreadSignalResult` route to simulator; if `SignalResult` use existing path.
  4. Accumulate spread trades in `BacktestResult.Trades` with
     `Direction = "SPREAD"` and individual leg rows for drill-down.

---

### D — DB Migrations (still open from PROMPT-003)

These were deferred from the DB_FIXES_ROADMAP and are NOT yet applied:

**028** — CHECK constraints on status/enum columns
  (`fx_rates.currency_pair`, `instruments.instrument_type`, `strategy_instances.status`,
   `risk_profiles.model`, `spread_positions.status`, `alert_log.severity` + 3 more)

**029** — Referential integrity
  - `instruments.internal_symbol` nullable → NOT NULL migration
  - 14 missing FKs on `backtest_runs`, `orders`, `positions`,
    `forward_test_trades`, `alert_log`

**030** — Uniqueness / perf indexes
  - Broker session expiry index, scenario name uniqueness,
    backtest run deduplication unique index

**031** — Column cleanup
  - Drop orphaned PascalCase columns on `instruments` and `forward_test_trades`
  - Deduplicate 13 redundant index pairs

Migration files go in: `src/rvs.AlgoTrader.Infrastructure/Migrations/`
Naming: `028_check_constraints.sql` … `031_column_cleanup.sql`

---

### E — Test Coverage Gaps

**UT-1 — PositionSizingEngine: Kelly formula edge cases**
File: `tests/rvs.AlgoTrader.Tests.Unit/Services/PositionSizingEngineTests.cs`
- Add: `WinRate=0` → returns `FixedLots=1`; `AvgWinLossRatio=0` → clamp;
  `KellyFraction > MaxCapitalPct` → hard cap applied.

**UT-2 — BacktestJobManager: 24h eviction verified**
File: `tests/.../BacktestJobManagerTests.cs`
- Add: enqueue 3 completed jobs with `StartedAt = 25h ago`; enqueue new job;
  assert evicted jobs not in `GetStatus`.

**UT-3 — FibOptionSpread: fib1618 formula regression**
File: `tests/.../FibOptionSpreadStrategyTests.cs`
- Uptrend case: `swingHigh=100, swingLow=80` → fib1618 target = `100 + 20*0.618 = 112.36`.
- Downtrend case: `swingLow=80, swingHigh=100` → fib1618 = `80 - 20*0.618 = 67.64`.
- Both must assert exact values to prevent regression.

**IT-1 — SpreadOrderManager: rollback on rejected leg**
File: `tests/.../SpreadOrderManagerIntegrationTests.cs`
- Mock broker: leg 1 fills, leg 2 rejects.
- Assert: `SpreadPosition.Status = Failed`; a reverse order was placed for leg 1.

---

### F — P7 Data Services (from PROMPT-003, still not started)

**D1 — NSE Bhavcopy downloader**
- New service: `NseBhavCopyDownloadService : IBhavCopyService`
- Downloads `https://nsearchives.nseindia.com/content/cm/BhavCopy_NSE_CM_0_0_0_{DDMMYYYY}_F_0000.csv`
- Parses SYMBOL, SERIES, CLOSE, TOTTRDQTY, TOTTRDVAL per row
- Upserts into `market_breadth` table via `IMarketBreadthRepository`
- Triggered by Hangfire daily job at 18:30 IST

**D2 — NSE event calendar CSV import**
- Parse `NSE Corporate Actions` CSV (columns: symbol, ex-date, purpose)
- Map to `event_calendar` table: `(internal_symbol, event_date, event_type, description)`
- Job: `EventCalendarImportJob` runs nightly, idempotent upsert

**D3 — IvHistoryService + iv_history migration**
- New migration: `032_iv_history.sql`
  ```sql
  CREATE TABLE option_iv_history (
    id            BIGSERIAL PRIMARY KEY,
    underlying    TEXT NOT NULL,
    date          DATE NOT NULL,
    atm_iv        NUMERIC(8,4) NOT NULL,
    UNIQUE (underlying, date)
  );
  SELECT create_hypertable('option_iv_history','date');
  ```
- `IvHistoryJob`: calls `IOptionChainService.GetSnapshotAsync`, extracts ATM strike
  IV, calls `IOptionIvRankService.RecordAsync`. Runs daily at 15:45 IST.

---

### G — Broker Stubs (from PROMPT-003)

**E1 — ZerodhaBrokerClient stub**
- Implement `IOrderClient`, `IMarketDataClient` for Zerodha (Kite Connect v3)
- Auth: OAuth2 + `request_token` exchange (already handled by `IBrokerAuthService`)
- `PlaceOrder`: `POST /orders/{variety}` with Polly retry (3× exp backoff on 502/503)
- Register in DI: `services.AddZerodhaBroker(config)`

**E2 — UpstoxBrokerClient stub**
- Same pattern as E1 but Upstox v2 API endpoints
- Auth: OAuth2, `auth_code` exchange
- `PlaceOrder`: `POST /v2/order/place`

---

### H — MCP / Architecture Doc

**G1 — MCP design doc**
File: `docs/MCP_DESIGN.md`
- Document the StrategyEvaluationQueue → IStrategy.EvaluateAsync → SignalResult
  pipeline with sequence diagram (Mermaid).
- Include: candle ingestion → context build → strategy eval → signal dispatch
  → order manager → broker client → fill callback.

