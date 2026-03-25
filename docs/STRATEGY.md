# STRATEGY.md — Product Strategy & Acceptance Criteria

## Product Vision

AlgoTrader is the go-to open-source platform for serious Indian retail traders who want to run systematic strategies across multiple brokers without paying for expensive proprietary platforms. It bridges the gap between institutional-grade infrastructure and individual traders.

## Target Users

| Persona | Description | Key Need |
|---|---|---|
| Systematic Trader | Runs quantitative strategies across NSE/BSE | Reliability, backtesting parity, minimal slippage |
| Developer-Trader | Builds custom strategies | Clean IStrategy API, unit-testable code |
| Risk-Conscious Trader | Manages multiple strategies + capital | Kill switch, capital locking, drawdown alerts |
| Analyst | Reviews performance post-market | Audit trail, signal journal, comparison panel |

## Definition of Done — Project Level

The project is considered production-ready when ALL of the following pass:

### Functional Requirements
- [ ] Multi-broker support: at least Zerodha + Upstox working end-to-end
- [ ] PriceActionBreakoutStrategy generates correct signals on historical fixture data
- [ ] Backtest produces reproducible results (same hash + metrics on re-run)
- [ ] Forward test session follows same scheduling rules as live instance
- [ ] Kill switch cancels all open orders and stops all instances within 5 seconds
- [ ] Capital locking prevents over-leverage across 2 concurrent strategy instances
- [ ] EOD PnL report delivered at 15:35 IST
- [ ] All 18 React panels functional with role-based visibility

### Non-Functional Requirements
- [ ] API response time < 200ms (p95) for non-backtest endpoints
- [ ] Broker order latency measured and visible in dashboard
- [ ] Zero data loss on Redis restart (AOF-persisted keys survive)
- [ ] Graceful shutdown completes within 30 seconds
- [ ] Cold restart auto-resumes within 60 seconds for configured instances
- [ ] All secrets loaded from ISecretsProvider (no hardcoded values in source)

### Quality Requirements
- [ ] `dotnet build` zero warnings with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- [ ] Unit test coverage ≥ 80% on Domain + Application + Strategies
- [ ] All NetArchTest architecture rules pass
- [ ] All integration tests pass with Testcontainers (real databases)
- [ ] All Playwright UI tests pass

### Compliance Requirements
- [ ] `audit_log` is append-only (no UPDATE/DELETE in any migration or code)
- [ ] All order placements in `audit_log` with actor + entity
- [ ] All config changes in `audit_log` with before/after JSON
- [ ] Kill switch activation in `audit_log` with CRITICAL severity
- [ ] Role-based access: Viewer cannot place orders, non-Admin cannot see kill switch

---

## Feature Acceptance Criteria

### AC-01: Order Placement
**Given** a logged-in Trader user  
**When** they submit an order form with a valid instrument, quantity, and order type  
**Then** an Idempotency-Key is auto-generated client-side  
**And** the order is written to `orders` table with status PENDING  
**And** the order is placed with the configured broker  
**And** an `audit_log` entry is created  
**And** a success toast notification appears  

**Edge case:** Submitting the same form twice (double-click) within 24 hours with the same Idempotency-Key returns the cached response without placing a duplicate order.

---

### AC-02: Kill Switch
**Given** an Admin user  
**When** they click the Kill Switch button and confirm  
**Then** Redis `killswitch:active` is set (no expiry)  
**And** `app_config KillSwitchActive = true` is written to PostgreSQL  
**And** all OPEN orders are cancelled across all brokers  
**And** all RUNNING strategy instances are stopped  
**And** a CRITICAL alert is sent via all configured channels  
**And** a toast notification appears for all connected users  
**And** `audit_log` entry created with actor, timestamp, list of affected instances/orders  

**Edge case:** On the next cold restart, kill switch is read from DB (Redis may have recovered), and NO instances auto-resume regardless of `auto_resume_on_restart` setting.

---

### AC-03: Backtest Reproducibility
**Given** a completed backtest run with `runId`  
**When** the user clicks "Reproduce"  
**Then** the engine re-runs with the exact same `config_snapshot` and `data_snapshot_id`  
**And** if `data_hash` matches, result metrics are identical  
**And** if `data_hash` differs (data was updated), the user sees a warning: "Data has changed since the original run"  
**And** a diff view shows any metric differences

---

### AC-04: Strategy Scheduling — Auto-Resume
**Given** a strategy instance with `auto_resume_on_restart = true` and `schedule_json` configured  
**And** the instance was in status RUNNING at shutdown  
**When** the system restarts during the scheduled session window  
**Then** `IStartupOrchestrator` Step 6 auto-resumes the instance  
**And** status transitions to RUNNING  
**And** `StrategyAutoResumed` event is published  
**And** an INFO alert is sent with reason "Auto-resumed: within scheduled session, prior status was RUNNING"  
**And** the Scheduling Panel shows the auto-resume event in history  

**Edge case:** If the instance was manually PAUSED before shutdown, it is NOT auto-resumed regardless of `auto_resume_on_restart`.

---

### AC-05: Strategy Scheduling — Missed Session
**Given** a strategy instance with `auto_resume_on_restart = true` and `missed_session_behavior = SKIP`  
**And** the instance was in status RUNNING at shutdown  
**When** the system restarts AFTER `session_start` time for the day  
**Then** the instance is NOT started  
**And** `StrategyMissedSessionWindow` event is published  
**And** a WARN alert is sent  
**And** the instance is scheduled for the NEXT eligible session day  
**And** signal_journal records `skipped_reason = OUTSIDE_SCHEDULE` for any evaluations during the missed window

---

### AC-06: Capital Locking Under Concurrent Load
**Given** two strategy instances both configured for the same broker  
**And** total available capital = ₹100,000  
**And** both instances simultaneously evaluate a BUY signal requiring ₹80,000 each  
**When** both call `ICapitalAllocator.TryReserveAsync(₹80,000)` concurrently  
**Then** exactly ONE succeeds and ONE fails with `INSUFFICIENT_CAPITAL`  
**And** the failing instance writes `skipped_reason = INSUFFICIENT_CAPITAL` to `signal_journal`  
**And** no over-leverage occurs

---

### AC-07: Partial Candle Guard
**Given** a live market feed is streaming ticks  
**When** `CandleAggregatorService` receives a tick that does NOT close a bar  
**Then** `IStrategy.EvaluateAsync()` is NOT called for that tick  
**And** the current open bar is only visible in the Chart Panel (labelled "In progress — not used for signals")  

**When** a tick crosses the bar boundary (bar is now closed)  
**Then** `CandleClosedEvent` is emitted  
**And** the strategy queue receives only the closed bar  
**And** `IStrategy.EvaluateAsync()` is called with the closed candle in context

---

### AC-08: Redis Degraded Mode
**Given** Redis is unavailable  
**When** a request comes in for order placement  
**Then** candle cache misses fall through to TimescaleDB  
**And** rate limiting is disabled (logged as WARN in structured logs)  
**And** kill switch is read from `app_config` DB table  
**And** capital reservation falls back to pessimistic DB locking (no orders if uncertain)  
**And** a WARN health check appears on `/health/ready`

---

### AC-09: Broker Latency Alerting
**Given** a monitoring alert rule: `broker.latency.p95.Zerodha > 500ms, severity WARN`  
**When** p95 broker latency exceeds 500ms for the configured window  
**Then** the alert fires once (not repeatedly within `window_seconds`)  
**And** an entry appears in `alert_log`  
**And** `MonitoringAlertTriggered` event is published  
**And** the React Alert Rules Manager shows the active breach badge  

**Edge case:** If the same rule fires again within `window_seconds`, Redis dedup key `alert:dedup:{ruleId}` prevents re-fire.

---

### AC-10: EOD PnL Report
**Given** it is a trading day (not a holiday)  
**And** it is 15:35 IST  
**When** the Hangfire EOD report job fires  
**Then** a report is generated with: gross PnL, net PnL (after costs), win rate, open positions  
**And** the report is sent to all users with `telegram_enabled = true` or `email_enabled = true`  
**And** the report includes the `SCHEDULING` and `MONITORING` alert categories if configured  
**And** nothing is sent on a market holiday

---

## Performance Benchmarks (Target)

| Metric | Target |
|---|---|
| API p95 latency (non-backtest) | < 200ms |
| Strategy evaluation throughput | ≥ 100 evaluations/second across all instances |
| Candle cache read (Redis) | < 5ms p99 |
| Order placement end-to-end (API → broker ACK) | < 500ms p95 |
| Backtest speed (1 year, 1 symbol, 1m candles) | < 30 seconds |
| Cold restart to READY | < 60 seconds |
| Kill switch full activation | < 5 seconds |

---

## SEBI Compliance Checklist

- [ ] `audit_log` is append-only — no UPDATE or DELETE permitted at DB or application level
- [ ] Every order placement logged with actor, timestamp, instrument, quantity, price, broker
- [ ] All config changes logged with before_json and after_json
- [ ] Login/logout events logged
- [ ] Kill switch activation logged
- [ ] Token refresh logged as `TOKEN_REFRESHED`
- [ ] Position mismatch logged as `RECONCILIATION_MISMATCH`
- [ ] Strategy auto-resume logged as `STRATEGY_AUTO_RESUMED`
- [ ] Audit log exported for any 30-day window (Admin only)
- [ ] Roles enforced: Viewer cannot place orders
- [ ] Market calendar: no trades on NSE/BSE holidays
- [ ] Market hours: no trades outside 09:15–15:30 IST (configurable per exchange)

---

## Risk Management Rules

All enforced at `LiveExecutionEngine` before every order:

1. **Position size**: `quantity = min(MaxCapitalPerTrade / price, MaxOpenTradesPerSymbol limit)`
2. **Daily drawdown**: if current day PnL < -MaxDailyDrawdownPercent × allocated_capital → block new orders, alert WARN
3. **Total capital deployed**: reserved capital must not exceed `MaxTotalCapitalDeployed`
4. **Max trades per day**: `orders placed today < MaxTradesPerDay`
5. **Kill switch**: checked before every order
6. **Market hours**: `IMarketCalendarService.IsWithinMarketHours()` must return true
7. **Capital reservation**: `ICapitalAllocator.TryReserveAsync()` must succeed

Any failure writes `skipped_reason` to `signal_journal` and an `alert_log` entry.
