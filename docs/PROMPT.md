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
## PROMPT-009 — Re-Audited 2026-04-08

### Context
Full re-audit performed 2026-04-08. Sources: all open GitHub issues (#144–#203)
plus PROMPT-003 carry-overs. Items ordered by severity within each group.
Issues labelled `Resolved` are confirmed done in code but issues are still open
on GitHub — close those issues after reading this prompt.

---

## TIER-0 — 🔴 CRITICAL (data-corruption / startup-breaking)

### T0-1 — audit_log duplicate table (issue #170)
File: `InitialMigration.sql` + `010_StubRepositoriesSchema.sql`
- `InitialMigration` creates `audit_log` with `event_type`, `details jsonb`.
- `010` re-creates it with `action`, `details_json text` (incompatible names).
- `CREATE TABLE IF NOT EXISTS` → InitialMigration wins on fresh install →
  `SELECT action FROM audit_log` → `ERROR 42703` at runtime.
- Fix: add migration `025_audit_log_fix.sql`:
  ```sql
  ALTER TABLE audit_log
    ADD COLUMN IF NOT EXISTS action VARCHAR(100),
    ADD COLUMN IF NOT EXISTS entity_id VARCHAR(200),
    ADD COLUMN IF NOT EXISTS details_json TEXT;
  UPDATE audit_log SET action = event_type WHERE action IS NULL;
  ALTER TABLE audit_log DROP COLUMN IF EXISTS event_type;
  ```
- Update `AuditLogConfiguration.cs` to map `action` and `details_json`.

### T0-2 — signal_journal duplicate table (issue #171)
File: same as T0-1
- `signal_journal` (from InitialMigration) and `signal_journal_entries`
  (from `010`) track the same concept but are split.
- `signal VARCHAR(10)` in `signal_journal` silently truncates `HOLD_FILTER_FAILED`.
- Fix: add migration `026_signal_journal_consolidate.sql`:
  ```sql
  INSERT INTO signal_journal_entries
    (strategy_run_id, signal, created_at)
  SELECT strategy_run_id, signal::VARCHAR(20), created_at
  FROM signal_journal
  ON CONFLICT DO NOTHING;
  DROP TABLE signal_journal;
  ```
- Remove all code referencing `signal_journal`; redirect to `signal_journal_entries`.

### T0-3 — forward_test_sessions no FK to strategy_instances (issue #172)
File: `004_BacktestAndForwardTestTrades.sql`
- `forward_test_sessions.strategy_instance_id UUID NOT NULL` has no `REFERENCES`.
- Fix: migration `027_fts_fk.sql`:
  ```sql
  ALTER TABLE forward_test_sessions
    ADD CONSTRAINT fk_fts_strategy_instance
    FOREIGN KEY (strategy_instance_id)
    REFERENCES strategy_instances(id)
    ON DELETE CASCADE;
  ```

---

## TIER-1 — 🔥 HIGH (wrong financial results)

### T1-1 — Sharpe ratio computed on per-trade not daily returns (issue #180)
File: `src/rvs.AlgoTrader.Backtesting/Engine/BacktestEngine.cs` → `ComputeMetrics`
- `√252` annualisation factor is correct for daily returns only.
- Current: per-trade NetPnl / InitialCapital × √252 → wrong for any frequency.
- Fix: group trades by `ExitDate.Date`, compute daily P&L, then apply √252.
  Use the already-computed `DailySharpe` field and promote it to primary.
  Remove the per-trade Sharpe path entirely.

### T1-2 — ForwardTestEngine hardcoded Quantity=1 (issue #176)
File: `src/rvs.AlgoTrader.Backtesting/Engine/ForwardTestEngine.cs` line ~97
- `new ForwardTestOpenTrade(..., 1, ...)` ignores `allocated_capital` and `lot_size`.
- Fix: call `PositionSizingEngine.Calculate(instance.AllocatedCapital,
  instance.LotSize, signal.StopLoss, entryPrice)` and pass result as quantity.
  Mirror `BacktestEngine.CalculatePositionSize` exactly.

### T1-3 — CalculatePositionSize allows trades when equity ≤ 0 (issue #177)
File: `BacktestEngine.cs` line ~248
- `Math.Max(1, maxByCapital)` forces at least 1 unit even after bankruptcy.
- Fix: add `if (equity <= 0) return 0;` at top of `CalculatePositionSize`.

### T1-4 — VWAP resets on wrong date for UTC candles (issue #178)
File: `src/rvs.AlgoTrader.Strategies/EmaVwapMomentum/EmaVwapMomentumStrategy.cs` line ~188
- `c.OpenTime.Date` uses stored timezone (UTC). 18:30 UTC = 00:00 IST resets too early.
- Fix:
  ```csharp
  private static readonly DateTimeZone Ist =
    DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
  var date = c.OpenTime.InZone(Ist).Date;
  ```

### T1-5 — backtest_runs.scenario_id no FK (issues #162, #174, #153)
File: `007_StrategyScenarios.sql` lines 23-24
- Fix (if not already done by migration 028/029):
  ```sql
  ALTER TABLE backtest_runs
    ADD CONSTRAINT fk_br_scenario_id
    FOREIGN KEY (scenario_id)
    REFERENCES strategy_scenarios(id)
    ON DELETE SET NULL;
  ```

### T1-6 — strategy_scenarios.last_backtest_run_id no FK (issues #161, #173, #152)
File: `007_StrategyScenarios.sql` line 13
- Fix:
  ```sql
  ALTER TABLE strategy_scenarios
    ADD CONSTRAINT fk_ss_last_backtest_run
    FOREIGN KEY (last_backtest_run_id)
    REFERENCES backtest_runs(id)
    ON DELETE SET NULL;
  ```

### T1-7 — ForwardTestEngine never persists FinalCapital (issue #184)
File: `ForwardTestEngine.cs` → `StopSessionAsync` line ~120
- `session.FinalCapital` never assigned → always 0 in DB.
- Fix: `session.FinalCapital = state.InitialCapital + state.TotalPnl;`

---

## TIER-2 — 🟡 MEDIUM (correctness / reliability)

### T2-1 — SL and TP both trigger on same bar, no gap-fill logic (issue #179)
File: `BacktestEngine.cs` → `TryClosePosition`
- When `Low ≤ SL` AND `High ≥ TP` on same bar, SL always wins → pessimistic bias.
- Fix: when both trigger, use `candle.Open` as fill price (gap-at-open heuristic).
  Document policy in `docs/BacktestAssumptions.md`.

### T2-2 — EmaVwapMomentum NoTradeAfterMinutes broken for UTC candles (issue #185)
File: `EmaVwapMomentumStrategy.cs` lines ~132-139
- `LocalDateTime.TimeOfDay` returns UTC time → 15:00 IST becomes 09:30 UTC → cut-off never fires.
- Fix: same IST zone conversion as T1-4:
  ```csharp
  var istTime = current.OpenTime.InZone(Ist).TimeOfDay;
  var barMinutes = istTime.Hour * 60 + istTime.Minute;
  ```

### T2-3 — warmupBars hardcoded to 50 in BacktestEngine (issue #187)
File: `BacktestEngine.cs` lines ~57, ~67
- Fix:
  1. Add `int MinWarmupBars { get; }` to `IStrategy`.
  2. Implement on each strategy (EmaVwap = 25, Fibonacci = 60, PCR = 10).
  3. Replace `warmupBars = 50` with `strategy.MinWarmupBars`.
  4. Replace `allCandles.Count < 50` guard with same value.

### T2-4 — Migration 009 missing from RepairStaleRecordsAsync (issue #186)
File: `DatabaseMigrationRunner.cs` → `RepairStaleRecordsAsync`
- Fix: add entry:
  ```csharp
  { "009_ScenarioCapital.sql",
    ("SELECT 1 FROM information_schema.columns WHERE"
     + " table_name='strategy_scenarios' AND column_name='initial_capital'",
     "ALTER TABLE strategy_scenarios ADD COLUMN IF NOT EXISTS"
     + " initial_capital NUMERIC(18,4) NOT NULL DEFAULT 0;") }
  ```

### T2-5 — SeedAppConfigAsync swallows all exceptions (issue #181)
File: `DatabaseMigrationRunner.cs` line ~171
- `catch { }` silently hides DB-unreachable errors at startup.
- Fix:
  ```csharp
  catch (NpgsqlException ex) when (ex.SqlState == "42P01") { /* table not found */ }
  // all other exceptions rethrow naturally
  ```

### T2-6 — orders/positions.quantity INT → should be BIGINT (issue #183)
File: `InitialMigration.sql` lines ~72, 75
- Fix migration:
  ```sql
  ALTER TABLE orders ALTER COLUMN quantity TYPE BIGINT;
  ALTER TABLE orders ALTER COLUMN filled_quantity TYPE BIGINT;
  ALTER TABLE positions ALTER COLUMN quantity TYPE BIGINT;
  ```

### T2-7 — forward_test_trades: dual PnL columns (issue #203)
File: `forward_test_trades` table
- `pnl` (NOT NULL DEFAULT 0) and `realized_pnl` (nullable) are redundant/conflicting.
- Fix:
  ```sql
  ALTER TABLE forward_test_trades RENAME COLUMN pnl TO gross_pnl;
  ALTER TABLE forward_test_trades
    ALTER COLUMN realized_pnl SET NOT NULL,
    ALTER COLUMN realized_pnl SET DEFAULT 0;
  ALTER TABLE forward_test_trades
    ADD CONSTRAINT chk_ftt_realized_pnl_on_close
    CHECK (realized_pnl = 0 OR exit_price IS NOT NULL);
  ```

### T2-8 — positions.average_price vs entry_price ambiguity (issue #202)
File: `positions` table
- Use Option B: add COMMENT to both columns; audit all PnL code to use `average_price`.
  ```sql
  COMMENT ON COLUMN public.positions.average_price IS
    'Volume-weighted average entry price after all scaling. Used for PnL.';
  COMMENT ON COLUMN public.positions.entry_price IS
    'Price of the first/initial entry leg only. Informational.';
  ```
- Search codebase for `entry_price` in PnL expressions and replace with `average_price`.

### T2-9 — ComputeReproducibilityHash allocates 10MB+ string (issue #182)
File: `BacktestEngine.cs` lines ~349-357
- Fix: replace `StringBuilder + SHA256.HashData(bytes)` with `IncrementalHash`:
  ```csharp
  using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
  Span<byte> buf = stackalloc byte[64];
  foreach (var c in candles)
  {
      BinaryPrimitives.WriteDecimalToSpan(buf, c.Open); // encode fields
      hash.AppendData(buf);
  }
  return Convert.ToHexString(hash.GetCurrentHash());
  ```

---

## TIER-3 — ⚡ PERFORMANCE

### T3-1 — CandleRepository.UpsertAsync: 2 round-trips per bar (issue #145)
File: `src/rvs.AlgoTrader.Infrastructure/Repositories/CandleRepository.cs`
- Replace SELECT + UPDATE/INSERT with:
  ```sql
  INSERT INTO candles (...) VALUES (...)
  ON CONFLICT (internal_symbol, timeframe, open_time)
  DO UPDATE SET close = EXCLUDED.close, high = GREATEST(candles.high, EXCLUDED.high),
    low = LEAST(candles.low, EXCLUDED.low), ..., is_closed = EXCLUDED.is_closed;
  ```
- Add `UpsertBatchAsync(IEnumerable<Candle>)` for end-of-bar burst writes.
- Requires unique constraint on `(internal_symbol, timeframe, open_time)`
  (see T3-3 migration).

### T3-2 — WalkForward + MonteCarlo are sequential (issue #146)
Files: `WalkForwardEngine.cs`, `MonteCarloSimulator.cs`
- Walk-forward: replace `for` loop with
  `Parallel.ForEachAsync(windows, MaxDegreeOfParallelism = ProcessorCount, ...)`.
  Each window resolves its own `IBacktestEngine` from `IServiceScopeFactory`.
- MonteCarlo: replace `for` loop with `Parallel.For(0, simulations, i => {...})`.
  Each thread gets its own `new Random(seed: i)` for determinism.
- Add `SemaphoreSlim` capping concurrent backtests at `ProcessorCount`.

### T3-3 — Missing composite indexes on candles table (issue #147)
File: new migration `009_CandleIndexes.sql`
```sql
CREATE INDEX IF NOT EXISTS ix_candles_symbol_tf_opentime_asc
  ON candles (internal_symbol, timeframe, open_time ASC)
  WHERE is_closed = true;

CREATE INDEX IF NOT EXISTS ix_candles_symbol_tf_opentime_desc
  ON candles (internal_symbol, timeframe, open_time DESC)
  WHERE is_closed = true;

ALTER TABLE candles
  ADD CONSTRAINT uq_candles_symbol_tf_opentime
  UNIQUE (internal_symbol, timeframe, open_time);
```
- Add matching `HasIndex` / `HasAlternateKey` in `CandleConfiguration.cs`.

### T3-4 — BacktestEngine.ComputeMetrics: 17+ LINQ passes (issue #144)
File: `BacktestEngine.cs` → `ComputeMetrics`
- Collapse all `.Where().Sum()` / `.Where().Count()` / `.Average()` /
  `.StdDev()` enumerations into a single `foreach` loop that accumulates:
  `winCount, lossCount, grossProfit, grossLoss, sumReturns, sumSqReturns,
   maxRunup, maxDrawdown, consecutiveWins, consecutiveLosses`.
- Eliminates ~8 intermediate `ToList()` allocations per backtest run.

### T3-5 — CandleCache: no TTL on Redis keys (issue #148)
Files: `src/rvs.AlgoTrader.Infrastructure/Cache/CandleCache.cs`
- `WarmAsync`: wrap `KeyDeleteAsync` + `SortedSetAddAsync` in a Redis transaction;
  add `KeyExpireAsync(key, TimeSpan.FromHours(60))` in same transaction.
- `AppendAsync`: call `KeyExpireAsync(key, TimeSpan.FromHours(60))` after ZADD
  to refresh TTL.
- `HasDataAsync` bug: the `date` parameter is silently ignored.
  Fix:
  ```csharp
  var start = date.AtStartOfDayInZone(Ist).ToInstant();
  var end   = date.PlusDays(1).AtStartOfDayInZone(Ist).ToInstant();
  return await db.Candles.AnyAsync(c =>
    c.InternalSymbol == symbol && c.Timeframe == timeframe
    && c.OpenTime >= start && c.OpenTime < end, ct);
  ```

---

## TIER-4 — 🗃️ DB CLEANUP (migrations 028–031)

Migration files go in: `src/rvs.AlgoTrader.Infrastructure/Migrations/`

### 028 — CHECK constraints on enum/status columns
```sql
-- fx_rates
ALTER TABLE fx_rates ADD CONSTRAINT chk_fx_currency_pair
  CHECK (currency_pair ~ '^[A-Z]{3}/[A-Z]{3}$');
-- instruments
ALTER TABLE instruments ADD CONSTRAINT chk_inst_type
  CHECK (instrument_type IN ('EQUITY','FUTURES','CE','PE','ETF','INDEX'));
-- strategy_instances
ALTER TABLE strategy_instances ADD CONSTRAINT chk_si_status
  CHECK (status IN ('Active','Paused','Stopped','Draft'));
-- risk_profiles
ALTER TABLE risk_profiles ADD CONSTRAINT chk_rp_model
  CHECK (model IN ('Fixed','Kelly','AtrBased'));
-- spread_positions
ALTER TABLE spread_positions ADD CONSTRAINT chk_sp_status
  CHECK (status IN ('Open','Closed','Failed'));
-- alert_log
ALTER TABLE alert_log ADD CONSTRAINT chk_al_severity
  CHECK (severity IN ('Info','Warning','Error','Critical'));
-- broker_sessions: expiry must be after stored_at
ALTER TABLE broker_sessions ADD CONSTRAINT chk_bs_expiry
  CHECK (expires_at IS NULL OR expires_at > stored_at);
CREATE INDEX ix_broker_sessions_expires_at
  ON broker_sessions(expires_at) WHERE expires_at IS NOT NULL;
```

### 029 — Missing foreign keys (issues #174, #173, #172 — may overlap T0-3, T1-5, T1-6)
```sql
-- orders → strategy_runs
ALTER TABLE orders ADD CONSTRAINT fk_orders_strategy_run
  FOREIGN KEY (strategy_run_id) REFERENCES strategy_runs(id) ON DELETE CASCADE;
-- positions → strategy_runs
ALTER TABLE positions ADD CONSTRAINT fk_positions_strategy_run
  FOREIGN KEY (strategy_run_id) REFERENCES strategy_runs(id) ON DELETE CASCADE;
-- forward_test_trades → forward_test_sessions
ALTER TABLE forward_test_trades ADD CONSTRAINT fk_ftt_session
  FOREIGN KEY (session_id) REFERENCES forward_test_sessions(id) ON DELETE CASCADE;
-- alert_log → strategy_instances
ALTER TABLE alert_log ADD CONSTRAINT fk_al_strategy_instance
  FOREIGN KEY (strategy_instance_id) REFERENCES strategy_instances(id) ON DELETE SET NULL;
```

### 030 — Uniqueness / perf indexes (issue #198, #200)
```sql
-- scenario name uniqueness per instance
ALTER TABLE strategy_scenarios
  ADD CONSTRAINT uq_scenario_name_per_instance
  UNIQUE (strategy_instance_id, name);
-- backtest run deduplication
CREATE UNIQUE INDEX IF NOT EXISTS uq_backtest_run_dedup
  ON backtest_runs(strategy_instance_id, scenario_id, started_at)
  WHERE scenario_id IS NOT NULL;
```

### 031 — Column cleanup (issues #189, #191, #199, #193, #196)
```sql
-- Drop orphaned Id column from candles (issue #191)
ALTER TABLE candles DROP COLUMN IF EXISTS "Id";
-- Drop PascalCase trailing stop dupes in orders (issue #189)
ALTER TABLE orders DROP COLUMN IF EXISTS "TrailingSl";
ALTER TABLE orders DROP COLUMN IF EXISTS "TrailingTp";
-- Drop duplicate candles index (issue #199)
DROP INDEX IF EXISTS public.idx_candles_symbol_tf;
-- Drop 13 duplicate idx_ indexes (issue #193 — full list):
DROP INDEX IF EXISTS public.idx_orders_broker_order_id;
DROP INDEX IF EXISTS public.idx_orders_idempotency;
DROP INDEX IF EXISTS public.idx_orders_strategy_run;
DROP INDEX IF EXISTS public.idx_positions_broker_open;
DROP INDEX IF EXISTS public.idx_positions_symbol_open;
DROP INDEX IF EXISTS public.idx_positions_strategy_run;
DROP INDEX IF EXISTS public.idx_strategy_instances_status;
DROP INDEX IF EXISTS public.idx_strategy_instances_symbol;
DROP INDEX IF EXISTS public.idx_strategy_runs_status;
DROP INDEX IF EXISTS public.idx_strategy_runs_instance;
DROP INDEX IF EXISTS public.idx_audit_log_entity;
DROP INDEX IF EXISTS public.idx_audit_log_occurred;
DROP INDEX IF EXISTS public.idx_instrument_universe_category;
-- Drop triple-unique on orders.idempotency_key (issue #200)
DROP INDEX IF EXISTS public.idx_orders_idempotency; -- already above
-- Standardise JSON columns from TEXT → JSONB (issue #196)
ALTER TABLE strategy_scenarios
  ALTER COLUMN parameters_json_override TYPE jsonb
  USING parameters_json_override::jsonb;
ALTER TABLE backtest_runs
  ALTER COLUMN trades_json TYPE jsonb USING trades_json::jsonb,
  ALTER COLUMN extended_stats_json TYPE jsonb USING extended_stats_json::jsonb,
  ALTER COLUMN effective_parameters_json TYPE jsonb USING effective_parameters_json::jsonb;
ALTER TABLE user_preferences
  ALTER COLUMN preferences_json TYPE jsonb USING preferences_json::jsonb;
```

---

## TIER-5 — Strategy Deferred Items (carry-over from P008)

### IC-2 / VS-1 — OptionLegSelector: FromStrike anchor
File: `src/rvs.AlgoTrader.Infrastructure/Services/OptionLegSelector.cs`
- Add `case StrikeSelectionMode.FromStrike: return spec.FixedStrike ?? spotPrice;`
  in the strike-picking switch before OTM percentage path.

### BS-2 — atmIv wired into BlackScholes callers
File: `src/rvs.AlgoTrader.Infrastructure/Services/BlackScholesEngine.cs`
- Replace all `0.18` hardcoded volatility with `signal.AtmIv ?? strategyContext.AtmIv ?? 0.18m`.
- Add `// TODO-BS-2: falling back to 0.18 — no AtmIv available` log when falling back.

### SS-1 — ShortStrangle MaxLossMultiple stop
File: `src/rvs.AlgoTrader.Strategies/ShortStrangle/ShortStrangleStrategy.cs`
- Check `unrealisedLoss >= premium * MaxLossMultiple` in `EvaluateAsync`;
  emit `CloseSpread` signal with `Reason = "MaxLossMultiple breached"`.

### SS-3 — ShortStrangle DTE filter
File: same as SS-1
- Filter expiry list to `DaysToExpiry in [MinDte, MaxDte]` before
  `GetNearestWeeklyExpiry()` call.

### CS-1 — CalendarSpread dual-expiry StrategyContext
File: `src/rvs.AlgoTrader.Strategies/CalendarSpread/CalendarSpreadStrategy.cs`
- Add `FarExpiryDate` (nullable `LocalDate?`) to `SpreadSignalResult`.
- `SpreadOrderManager.ExecuteSpreadAsync` picks near vs far expiry per leg via
  `leg.NearestWeekly` flag.

### CS-2 — CalendarSpread IV slope filter
File: same as CS-1
- Compute `nearIv - farIv` from `IOptionIvRankService`;
  skip signal if slope ≤ 0 (backwardation). Log to `SkippedSignalCount`.

### FIB-2 — FibOptionSpread UnderlyingStopLevel in SpreadSignalResult
File: `src/rvs.AlgoTrader.Strategies/FibOptionSpread/FibOptionSpreadStrategy.cs`
- Add `decimal? UnderlyingStopLevel` to `SpreadSignalResult` record.
- `SpreadOrderManager` sets OCO on underlying when field is populated.

### FIB-3 — BacktestEngine doesn't populate AtmIv from iv_history
File: `src/rvs.AlgoTrader.Backtesting/BacktestEngine.cs`
- Inject `IOptionIvRankService`; in `BuildContextAsync` call `GetAsync(symbol)`
  and populate `context.AtmIv`. Guard null so backtests without iv_history still run.

### FIB-4 — BacktestEngine UpcomingEvents always empty
File: same as FIB-3
- Inject `IEventCalendarRepository`; query events in `[barDate, barDate+7d]`.
  Cache per-day to avoid per-bar DB calls.

### ALL-SPREADS-1 — SpreadBacktestEngine
Files: `BacktestEngine.cs`, new `SpreadBacktestSimulator.cs`
- Add `ISpreadBacktestSimulator` interface.
- Implement: per leg, find option row in `option_iv_history`, price with
  Black-Scholes at entry/exit bar, compute premium P&L, apply lot size.
- In `RunAsync`: if result is `SpreadSignalResult` → route to simulator;
  if `SignalResult` → use existing equity path.
- Accumulate spread trades in `BacktestResult.Trades` with `Direction="SPREAD"`.

---

## TIER-6 — P7 Data Services

### D1 — NSE Bhavcopy downloader
- New: `NseBhavCopyDownloadService : IBhavCopyService`
- URL: `https://nsearchives.nseindia.com/content/cm/BhavCopy_NSE_CM_0_0_0_{DDMMYYYY}_F_0000.csv`
- Columns: SYMBOL, SERIES, CLOSE, TOTTRDQTY, TOTTRDVAL
- Upserts into `market_breadth` via `IMarketBreadthRepository`
- Hangfire job at 18:30 IST daily

### D2 — NSE event calendar CSV import
- Parse NSE Corporate Actions CSV: symbol, ex-date, purpose
- Map to `event_calendar`: `(internal_symbol, event_date, event_type, description)`
- `EventCalendarImportJob` runs nightly, idempotent upsert

### D3 — IvHistoryService + iv_history migration
- Migration `032_iv_history.sql`:
  ```sql
  CREATE TABLE option_iv_history (
    id         BIGSERIAL PRIMARY KEY,
    underlying TEXT NOT NULL,
    date       DATE NOT NULL,
    atm_iv     NUMERIC(8,4) NOT NULL,
    UNIQUE (underlying, date)
  );
  SELECT create_hypertable('option_iv_history','date');
  ```
- `IvHistoryJob`: calls `IOptionChainService.GetSnapshotAsync`, extracts ATM
  strike IV, calls `IOptionIvRankService.RecordAsync`. Runs daily at 15:45 IST.

---

## TIER-7 — Broker Stubs

### E1 — ZerodhaBrokerClient stub
- Implement `IOrderClient`, `IMarketDataClient` for Zerodha Kite Connect v3
- Auth: OAuth2 + `request_token` exchange via `IBrokerAuthService`
- `PlaceOrder`: `POST /orders/{variety}` with Polly retry (3× exp backoff on 502/503)
- Register: `services.AddZerodhaBroker(config)`

### E2 — UpstoxBrokerClient stub
- Same pattern as E1 but Upstox v2: `POST /v2/order/place`
- Auth: OAuth2 `auth_code` exchange

---

## TIER-8 — Test Coverage Gaps

### UT-1 — PositionSizingEngine Kelly edge cases
File: `tests/.../PositionSizingEngineTests.cs`
- `WinRate=0` → returns `FixedLots=1`
- `AvgWinLossRatio=0` → clamp to 0, return min lots
- `KellyFraction > MaxCapitalPct` → hard-cap applied

### UT-2 — BacktestJobManager 24h eviction
File: `tests/.../BacktestJobManagerTests.cs`
- Enqueue 3 jobs with `StartedAt = 25h ago`; enqueue new job;
  assert evicted jobs absent from `GetStatus`.

### UT-3 — FibOptionSpread fib1618 regression
File: `tests/.../FibOptionSpreadStrategyTests.cs`
- Uptrend: `swingHigh=100, swingLow=80` → expected `112.36`
- Downtrend: `swingLow=80, swingHigh=100` → expected `67.64`

### IT-1 — SpreadOrderManager rollback on rejected leg
File: `tests/.../SpreadOrderManagerIntegrationTests.cs`
- Mock: leg 1 fills, leg 2 rejects.
- Assert: `SpreadPosition.Status = Failed`; reverse order placed for leg 1.

### IT-2 — CandleCache TTL expiry
- Assert Redis key auto-expires after 60h (FakeRedis/mock).
- Assert `HasDataAsync(symbol, tf, yesterday)` returns false when only today's candles exist.

### IT-3 — Parallel WalkForward determinism
- Run 4-window walk-forward sequentially and in parallel.
- Assert both produce identical `WindowResults` arrays.

---

## TIER-9 — Architecture Doc

### G1 — MCP design doc
File: `docs/MCP_DESIGN.md`
- Mermaid sequence diagram: candle ingestion → context build → strategy eval →
  signal dispatch → order manager → broker client → fill callback.
- Include: `StrategyEvaluationQueue → IStrategy.EvaluateAsync → SignalResult` pipeline.

---

## Close These GitHub Issues (code already merged, labels confirm Resolved)

The following issues carry the `Resolved` label — close them after PROMPT-009 is actioned:
#196, #193, #191, #189, #187, #186, #185, #184, #183, #182, #181, #180, #179, #178,
#177, #176, #174, #173, #172, #171, #170, #200, #199, #198, #203, #202, #147, #145.

> Note: Close via GitHub CLI: `gh issue close <number> -c "Resolved in PROMPT-009 implementation"`
