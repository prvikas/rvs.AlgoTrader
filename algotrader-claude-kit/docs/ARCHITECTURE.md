# Architecture Decision Record — AlgoTrader

## ADR Index

| # | Decision | Status |
|---|---|---|
| ADR-001 | Modular Monolith over Microservices | Accepted |
| ADR-002 | IClock abstraction over system clock | Accepted |
| ADR-003 | MediatR scoped to high-value CQRS only | Accepted |
| ADR-004 | Redis Lua scripts for capital reservation | Accepted |
| ADR-005 | Dual-write kill switch (Redis + DB) | Accepted |
| ADR-006 | MVC for business; Minimal API for infra | Accepted |
| ADR-007 | TimescaleDB for time-series data | Accepted |
| ADR-008 | Incremental indicators (O(1)) for live | Accepted |
| ADR-009 | NetArchTest in CI for architecture enforcement | Accepted |
| ADR-010 | Mapperly (source-gen) over AutoMapper | Accepted |
| ADR-011 | Three-layer timezone model (IST / UTC / User local) | Accepted |

---

## ADR-001: Modular Monolith over Microservices

**Context:** The platform needs three logically separate domains. Microservices would add network overhead and operational complexity premature for a single-team trading system.

**Decision:** Build as a modular monolith with explicit bounded context boundaries. Each context communicates only via domain events (MassTransit) or shared Redis cache. Zero direct service-to-service calls across contexts.

**Future:** When a context needs to scale independently, it can be extracted into a microservice by:
1. Moving the project out of the solution
2. Adding an HTTP/gRPC API surface
3. Replacing in-process calls with HTTP or MassTransit over RabbitMQ

**Context isolation rules:**
```
Trading Execution   ──MassTransit──▶  Data Ingestion
Trading Execution   ◀──Redis Cache──  Data Ingestion (candles)
Backtesting Engine  ◀──TimescaleDB──  (reads only, no writes during backtest)
Backtesting Engine  NEVER calls       any broker interface
Data Ingestion      NEVER calls       IBrokerOrderClient.PlaceOrderAsync()
```

---

## ADR-002: IClock Abstraction

**Context:** Trading logic depends on current IST time for: market hours validation, bar boundary detection, scheduling, force-exit timing. Using `DateTime.Now` makes backtest determinism impossible.

**Decision:** All time access goes through `IClock`. Two implementations:
- `SystemClock` — production, reads real time in IST via NodaTime
- `SimulatedClock` — backtest/forward-test/unit-test, engine controls time advance

**Enforcement:** Code review rule + optional Roslyn analyzer that flags `DateTime.Now`, `DateTimeOffset.UtcNow`, `NodaTime.SystemClock.Instance` usages in business code.

---

## ADR-003: MediatR Scoped to High-Value CQRS

**Context:** MediatR adds a dispatch layer that benefits complex, side-effect-heavy operations. Applying it universally creates ceremony without benefit.

**Decision:**
- **Use MediatR for:** `PlaceOrderCommand`, `StartStrategyInstanceCommand`, `RunBacktestCommand`, `PauseStrategyInstanceCommand`, `StopStrategyInstanceCommand`, `GenerateForwardTestReportQuery`
- **Use direct service for:** Watchlist CRUD, instrument search, notification preferences, app_config reads, market calendar queries

**Rule:** A handler is justified when it has: validators, multiple repository calls, domain event publishing, or cross-cutting concerns. A 3-line DB read does not qualify.

---

## ADR-004: Redis Lua Scripts for Capital Reservation

**Context:** Two concurrent strategy instances can independently pass an availability check and both reserve capital, causing over-leveraging.

**Decision:** All capital reservation uses a single Redis Lua script that atomically reads available capital, checks against requested amount, and increments reserved — in one round-trip. No application-level locking.

```lua
-- atomic_reserve.lua
local key_allocated = KEYS[1]  -- capital:{broker}:allocated
local key_reserved  = KEYS[2]  -- capital:{broker}:reserved
local amount        = tonumber(ARGV[1])
local allocated     = tonumber(redis.call('GET', key_allocated) or 0)
local reserved      = tonumber(redis.call('GET', key_reserved) or 0)
local available     = allocated - reserved
if available >= amount then
    redis.call('INCRBY', key_reserved, amount)
    return 1
end
return 0
```

---

## ADR-005: Dual-Write Kill Switch

**Context:** Kill switch must work even if Redis is down (Redis failure = no trading, not open trading).

**Decision:** On kill switch activation:
1. Write `killswitch:active = true` to Redis (AOF-persisted, no expiry)
2. Write `KillSwitchActive = true` to `app_config` table in PostgreSQL

On every order placement path: check Redis first (fast), fall back to `app_config` DB if Redis unavailable.

---

## ADR-006: MVC for Business; Minimal API for Infrastructure

**Context:** MVC controllers provide richer features: model binding, filters, `[Authorize]`, `[EnableRateLimiting]`, Swagger XML comments, global exception handler. Minimal APIs are simpler for lightweight endpoints.

**Decision:**
- All `/api/v1/...` business routes → `[ApiController]` MVC Controllers
- `/health/live`, `/health/ready`, `/metrics`, Hangfire webhook triggers → Minimal APIs

---

## ADR-007: TimescaleDB for Time-Series Data

**Context:** Candle data (OHLCV) has time as the natural partition key. Queries are always time-range-bounded. Standard PostgreSQL with a large TIMESTAMPTZ index would degrade at scale.

**Decision:** Use TimescaleDB extension on PostgreSQL. Hypertables for `candles` and `forward_test_equity_curve`. All queries against these tables **must** include a time range filter.

**Important:** `SELECT create_hypertable('candles', 'timestamp')` must be run after table creation, before first insert.

---

## ADR-008: Incremental Indicators for Live Trading

**Context:** Re-computing EMA/ATR/VWAP from the full candle series on every tick would be O(N). For 500 candles with multiple indicators, this becomes a bottleneck at 1-minute intervals across 100 symbols.

**Decision:** Maintain `IIncrementalIndicator<T>` instances per strategy per symbol per timeframe. `Update(candle)` is O(1). Full batch `IIndicatorService` is used only for backtesting chart rendering.

**Rule:** `IIncrementalIndicator.Update(candle)` is called **before** `IStrategy.EvaluateAsync()`. Indicators are not recomputed from history on each evaluation.

---

## ADR-009: NetArchTest in CI

**Decision:** Architecture rules are machine-enforced, not just documented. The following tests run in CI and block merge if they fail:

```csharp
// Domain has no infrastructure dependencies
Types.InAssembly(domain).Should().NotHaveDependencyOn("rvs.AlgoTrader.Infrastructure");
Types.InAssembly(domain).Should().NotHaveDependencyOn("Microsoft.EntityFrameworkCore");

// Application has no EF Core (repositories are interfaces only)
Types.InAssembly(application).Should().NotHaveDependencyOn("Microsoft.EntityFrameworkCore");

// IStrategy implementations have no broker dependencies
Types.InAssembly(strategies)
    .That().ImplementInterface(typeof(IStrategy))
    .Should().NotHaveDependencyOn("rvs.AlgoTrader.Brokers");

// Backtesting context has no broker dependencies  
Types.InAssembly(backtesting).Should().NotHaveDependencyOn("rvs.AlgoTrader.Brokers.Zerodha");
Types.InAssembly(backtesting).Should().NotHaveDependencyOn("rvs.AlgoTrader.Brokers.Upstox");
Types.InAssembly(backtesting).Should().NotHaveDependencyOn("rvs.AlgoTrader.Brokers.MStock");
```

---

## ADR-010: Mapperly over AutoMapper

**Context:** AutoMapper uses reflection at runtime. Mapperly generates source code at compile time, producing zero-allocation mapping with IDE-navigable code.

**Decision:** Use `Riok.Mapperly` for all DTO ↔ Domain mapping. No `AutoMapper`. No manual property assignment.

---

## Bounded Context Detail

### Trading Execution Context

**Owns:**
- `orders`, `positions`, `capital_allocations`, `risk_profiles`, `signal_journal`, `strategy_instances`, `strategy_runs`

**Publishes:** `OrderPlaced`, `PositionClosed`, `PositionMismatchDetected`, `SignalGenerated`, `AlertTriggered`

**Consumes:** `CandleClosedEvent` (from Data Ingestion)

**Never calls:** Any Data Ingestion or Backtesting service directly

### Data Ingestion Context

**Owns:**
- `candles` (TimescaleDB), `instruments`, `watchlists`, `watchlist_symbols`, `download_jobs`, `download_job_chunks`, `symbol_data_preferences`, `data_quality_log`

**Publishes:** `CandleClosedEvent`, `StreamDisconnected`, `StreamReconnected`

**Never calls:** `IBrokerOrderClient.PlaceOrderAsync()`

### Backtesting Context

**Owns:**
- `strategy_runs`, `forward_test_sessions`, `forward_test_trades`, `forward_test_equity_curve`, `backtest_data_snapshots`, `backtest_cost_profiles`

**Never calls:** any broker interface

**Reads:** `candles` from TimescaleDB (read-only, own repository)

---

## Strategy Execution Pipeline (Data Flow)

```
BrokerWebSocket
    │ BrokerTick { Symbol, LTP, Volume, Timestamp }
    ▼
CandleAggregatorService
    │ Uses IClock.Now() for bar boundary detection
    │ On bar boundary: emits CandleClosedEvent
    │ CurrentBar (partial) → display only, NEVER to strategy
    ▼
MassTransit CandleClosedEvent
    │
    ▼
StrategyEvaluationQueue (System.Threading.Channels, per instance)
    │ Checks IStrategyScheduler.IsWithinScheduledSession()
    │ Throttled by IStrategyExecutionThrottler
    ▼
IStrategy.EvaluateAsync(StrategyContext)
    │ StrategyContext.CandlesByTimeframe = closed candles ONLY
    │ IIncrementalIndicator.Update(closedCandle) called first
    ▼
SignalResult → signal_journal (ALL outcomes: BUY/SELL/HOLD/SKIPPED)
    │
    ▼ (if BUY/SELL)
IExecutionEngine.ExecuteAsync()
    ├── LiveExecutionEngine → ICapitalAllocator.TryReserveAsync() → IBrokerOrderClient.PlaceOrderAsync()
    ├── SimulatedExecutionEngine → IForwardTestFillSimulator
    └── BacktestExecutionEngine → BacktestFillSimulator + SimulatedClock.Advance()
```

---

## Database Schema Overview

### Core Tables
| Table | Type | Notes |
|---|---|---|
| `instruments` | Regular | tsvector for full-text search |
| `candles` | TimescaleDB hypertable | Partition by timestamp |
| `strategy_instances` | Regular | config_json, schedule_json, failure_behavior_json |
| `orders` | Regular | idempotency_key, trailing SL/TP |
| `positions` | Regular | linked to strategy_run_id |
| `signal_journal` | Regular | ALL evaluations including SKIPPED |
| `forward_test_equity_curve` | TimescaleDB hypertable | Partition by timestamp |
| `audit_log` | Append-only | SEBI compliance — no UPDATE/DELETE |
| `app_config` | Key-value | DB-driven runtime config, Redis-cached 60s |
| `idempotency_keys` | Regular | 24h TTL, Redis-primary |

### Redis Key Patterns
| Pattern | Purpose | Persistence |
|---|---|---|
| `candles:{symbol}:{timeframe}` | Sorted set, 500 bars | Warm from DB on miss |
| `killswitch:active` | Boolean flag | AOF required |
| `session:{broker}:{userId}` | Encrypted broker token | AOF required |
| `capital:{broker}:reserved` | Atomic reservation counter | AOF required |
| `idempotency:{userId}:{key}` | 24h dedup | AOF required |
| `ratelimit:{userId}:{window}` | Sliding window counter | Disposable |
| `alert:dedup:{ruleId}` | Alert dedup tracker | Disposable |
| `instruments:{exchange}:{symbol}` | Instrument metadata | Warm from DB |

---

## Security Model

### Authentication
- ASP.NET Core Identity + JWT bearer (access + refresh)
- Access token: 60 min expiry
- Refresh token: 30 day expiry, rotated on every use
- All auth events → `audit_log`

### Authorization Roles
| Role | Permissions |
|---|---|
| Admin | All panels, kill switch, user management, broker credentials, audit log |
| Trader | Trade execution, strategy management, backtest/forward-test, watchlists |
| Viewer | Read-only: positions, signals, charts, audit log (no order placement) |

### Secrets Hierarchy (Priority Order)
1. HashiCorp Vault (production preferred)
2. Environment variables (Docker Compose, CI/CD)
3. `appsettings.Development.json` (local dev, git-ignored)

### Sensitive Fields (AES-256 at rest)
- Broker session tokens
- OAuth refresh tokens
- API keys stored in DB
- Applied via EF Core value converters on `[Encrypted]` annotated columns

---

## Resilience Patterns

### Per External Dependency
| Dependency | Strategy | Config |
|---|---|---|
| Broker REST | Polly: Retry 3× exponential + Circuit Breaker + Timeout 10s | Per broker |
| Broker WebSocket | ReconnectingBrokerStreamClient: exponential backoff reconnect | Per broker |
| PostgreSQL | Health check → 503 if down; Hangfire pauses | Built-in |
| Redis | Graceful degradation: cache miss → DB fallback; rate limit disabled with WARN | Built-in |
| RabbitMQ | MassTransit retry + backoff; fallback to in-process sync for critical paths | Built-in |
| Hangfire jobs | Auto-retry configurable; max retries → FAILED + alert | Per job |

### Graceful Shutdown (SIGTERM)
1. Stop accepting new orders and signal evaluations
2. Complete in-flight strategy evaluations (with timeout)
3. Persist SimulatedClock state for running forward tests
4. Write shutdown event to `audit_log` with list of running instances
5. Close all broker WebSocket connections
6. Flush Serilog buffers

---

## Monitoring & Observability

### Metrics (OpenTelemetry → Prometheus → Grafana)
- `broker.latency.p50/p95/p99.{broker}` — measured on every broker call
- `strategy.evaluations.per_second` — throughput gauge
- `capital.utilization.{broker}` — percentage of allocated capital reserved
- `orders.placement_success_rate` — rolling 5-min window
- `stream.tick_age_seconds.{symbol}` — staleness detection

### Health Checks
- `/health/live` — process alive (Minimal API)
- `/health/ready` — all deps healthy, startup complete (Minimal API)
  - Checks: PostgreSQL, Redis + AOF status, RabbitMQ, broker WebSocket per broker, Hangfire heartbeat, kill-switch state

### Alert Categories
`ORDER_FILL`, `SL_HIT`, `DRAWDOWN`, `SIGNAL`, `SYSTEM`, `RECONCILIATION`, `SCHEDULING`, `MONITORING`

All alerts: written to `alert_log` → MassTransit event → React alert feed (SignalR) → Telegram/email per user preferences

---

## 🔭 Future Extensibility (Zero Breaking Changes)

The architecture is explicitly designed for these extension points — no existing code should need modification:

| Extension | How to Add |
|---|---|
| **New broker** | Implement `IFullBrokerClient` (and sub-interfaces), register in DI via `BrokerClientFactory` — zero changes to engine |
| **New strategy** | Implement `IStrategy`, register in DI, add `config_json` schema to DB — zero changes to execution engine |
| **Microservice split** | Each bounded context is already isolated — extract by moving project, adding API surface, replacing in-process calls with HTTP or MassTransit over RabbitMQ |
| **Custom schedule types** | Implement `IScheduleEvaluator` for cron-based or event-based triggers |
| **News sentiment** | Add `INewsSource` interface, implement in Infrastructure, plug into `StrategyContext.ExternalSignals` |
| **Option chain analysis** | Add `IOptionChainService` + dedicated React dashboard panel |
| **Multi-account** | Extend `BrokerClientFactory` to resolve by `(brokerName, accountId)` |
| **AI signal overlay** | Add `IAiSignalProvider` interface — plug in local LLM or API without touching `IExecutionEngine` |
| **Strategy optimizer** | Grid-search over `config_json` params by wrapping `IStrategy.EvaluateAsync` in a loop — no engine changes |

**Design principle:** All extension points use the existing DI container and interface contracts. The bounded context boundaries mean any context can be extracted to a microservice in the future without refactoring the remaining contexts.

---

## ADR-011: Three-Layer Timezone Model

**Context:** The platform's users may operate from any timezone (e.g. Vikas in CST = UTC-6/UTC-5),
while the Indian broker APIs and all market operations run exclusively on IST (UTC+5:30).
IST has no daylight-saving offset — it is always UTC+5:30. CST/CDT offset changes twice a year,
creating an 11.5h (CDT) or 12.5h (CST) delta from IST. A single-timezone approach forces either
storing wrong times or confusing users.

**Decision:** Three distinct timezone layers, each with a strict scope:

| Layer | Timezone | Scope |
|---|---|---|
| **Layer 1: IST** | `Asia/Kolkata` (UTC+5:30, no DST) | All trading logic: `IClock.Now()`, market hours, bar boundaries, session schedules, Hangfire job crons |
| **Layer 2: UTC** | UTC | All persistence (`TIMESTAMPTZ`), all API wire format (`DateTimeOffset`), MassTransit headers, Serilog timestamps |
| **Layer 3: User local** | Per-user IANA key (e.g. `America/Chicago`) | React display only; notification formatting; never used in backend trading logic |

**Key consequences:**
- `schedule_json.session_start` and `session_stop` are **always IST time-of-day strings** (`"09:20"`, `"15:10"`). Never store them in the user's local timezone.
- `user_preferences.timezone` stores the user's IANA display timezone. Included as `"tz"` JWT claim on login so the React frontend can render local times immediately without a separate API call.
- The frontend `SessionTimeDisplay` component renders both the IST time (canonical) and the user's local time (hint). A CST user sees: `"09:20 IST (22:50 CST prev day)"`.
- `INotificationService` uses `TimezoneHelper.FormatDualTime(utcInstant, userIanaTimezone)` for all alerts and the EOD report — both IST and local time are shown.
- IST has no daylight saving — market session times never shift. Users in DST-observing zones (US, EU) will see their local conversion shift twice a year; the IST canonical time never changes.

**Rejected alternatives:**
- *Store schedule in UTC*: Confusing for operators — "why does my 09:20 session show as 03:50 UTC?"
- *Store schedule in user's local time*: Breaks `IStrategyScheduler` which compares against `IClock.Now()` in IST; requires timezone conversion on every evaluation tick.
- *Single timezone (IST only) everywhere*: Poor UX for users outside India; signal journal, audit log, notifications are unreadable at e.g. 23:15 IST = yesterday afternoon CST.
