# rvs.AlgoTrader

> **A professional-grade algorithmic trading platform for Indian markets** — built for Quant Traders, Systematic Traders, Portfolio Managers, Quant Researchers, Strategy Architects, Risk Managers, Execution Researchers, Quant Developers, and Fund Managers.

---

## Vision

The long-term goal is an **Argus-class institutional trading platform** purpose-built for Indian markets (NSE/BSE/MCX/CDS). Not a retail scanner. Not a webhook-based signal copier. A full-stack quantitative research and execution engine where:

- A **Quant Researcher** designs, backtests, and stress-tests strategies with institutional-grade analytics (Sharpe, Sortino, Calmar, Monte Carlo, Walk-Forward)
- A **Strategy Architect** configures multi-strategy portfolios with correlation controls, position sizing, and regime filters
- A **Risk Manager** enforces portfolio-level circuit breakers, margin limits, delta caps, and daily loss limits — across every live strategy simultaneously
- A **Systematic Trader** runs live strategies in Paper or Live mode, with full trailing stop / break-even / partial exit management
- A **Portfolio Manager** monitors P&L attribution by strategy, symbol, session, exit type — and exports tax lots for ITR-3 filing
- A **Fund Manager** promotes paper-tested strategies to live with a single approval gate, with full audit trail

---

## Tech Stack

| Layer | Technology |
|-------|------------|
| Backend API | .NET 9, C# 13, ASP.NET Core minimal APIs |
| ORM / DB | PostgreSQL (TimescaleDB), EF Core + raw SQL migrations |
| Cache / Queue | Redis (AES-256-GCM token store, Lua capital reserve), RabbitMQ |
| Real-time | SignalR (backtest progress push), WebSocket (broker feeds) |
| Options Math | Custom Black-Scholes engine, Newton-Raphson IV solver |
| Broker APIs | mStock Type B, Zerodha Kite v3, Upstox v3 (pluggable via `IBrokerClient`) |
| Frontend | React 19, TypeScript, Recharts, React Query |
| Job Scheduling | Hangfire (9 recurring jobs, IST-aware session windows) |
| Testing | xUnit, FluentAssertions, Moq, Vitest (frontend) |
| Auth | Local JWT (`LocalAuth`), 6-tier RBAC |

---

## Platform Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                        rvs.AlgoTrader Platform                      │
├────────────────┬────────────────┬───────────────┬───────────────────┤
│  Research      │  Execution     │  Risk         │  Analytics        │
│  Layer         │  Engine        │  Engine       │  Layer            │
├────────────────┼────────────────┼───────────────┼───────────────────┤
│ Backtester     │ Live Engine    │ Portfolio     │ Performance       │
│ Walk-Forward   │ Paper Mode     │ Risk Manager  │ Analytics         │
│ Monte Carlo    │ Order Manager  │ Circuit       │ Trade Journal     │
│ Optimiser      │ Spread Orders  │ Breaker       │ P&L Attribution   │
│ Scenario Sim   │ Trailing Stops │ DLL / Margin  │ Tax Export (ITR3) │
├────────────────┼────────────────┼───────────────┼───────────────────┤
│  Strategy Layer (IStrategy)                                         │
│  VCP Swing | Fib Spread | Intraday PCR/VWAP | Iron Condor          │
│  Short Straddle | Short Strangle | Calendar Spread | Verticals      │
├────────────────┬────────────────┬───────────────┬───────────────────┤
│  Options Engine│  Indicator Lib │  Data Layer   │  Broker Adapters  │
│  Black-Scholes │  50+ indicators│  OHLCV+OI     │  Zerodha Kite     │
│  Greeks (δγθυ) │  Candle Patt.  │  Option Chain │  Upstox v3        │
│  IV Rank/IVP   │  MTF Analysis  │  Breadth Svc  │  mStock           │
│  Strike Select │  Swing Points  │  Event Cal.   │  (pluggable)      │
└────────────────┴────────────────┴───────────────┴───────────────────┘
```

### Execution engine modes

The same `IStrategy` runs identically across all three modes — only `IExecutionEngine` differs:

| Mode | Class | Description |
|------|-------|-------------|
| Historical backtest | `BacktestExecutionEngine` | Replays OHLCV from DB; synthetic option chain; no broker calls |
| Forward test (paper) | `SimulatedExecutionEngine` | Live market data; simulated fills; no real orders |
| Live | `LiveExecutionEngine` | Real broker orders; 8-gate pre-flight check; approval gate required |

---

## Strategy Lifecycle Flow

```
Strategy Definition
      │
      ├─ Scenario 1 (Draft)
      │      │
      │      ├─ [Backtest] → BacktestJobManager → SignalR push → Results stored
      │      │      │
      │      │      └─ Status: Backtested
      │      │
      │      ├─ [→ Fwd Test] → SimulatedExecutionEngine (paper fills, live data)
      │      │      │
      │      │      └─ Status: FwdTesting
      │      │
      │      ├─ [Approval Gate] → CAGR/drawdown checks + manual review
      │      │      │
      │      │      └─ Status: LiveCandidate
      │      │
      │      └─ [Deploy Live] → LiveExecutionEngine (real broker, real capital)
      │             │
      │             └─ Status: Live
      │
      └─ Scenario 2 (alternate params, side-by-side comparison)
```

### Scenario status lifecycle

`Draft → Running → Backtested → FwdTesting → LiveCandidate → Live → Archived`

### Backtest pipeline detail

1. `BacktestJobManager` receives a run request, saves job state to DB
2. Checks candle cache (`ICandleCache`) — if insufficient, triggers `HistoricalDownloadService`
3. **On 401 broker auth error**: retries the engine with existing cached DB candles; only fails if cache is also insufficient — surfaces actionable "Re-authenticate via Settings → Broker Login" message
4. Engine runs `IStrategy.EvaluateAsync` per bar (no partial candles — AP-007)
5. Progress pushed via SignalR (`BacktestProgressHub`) and polled via HTTP every 1500ms
6. On completion: `backtest_runs` row updated, trades persisted, results available in Results tab

---

## Strategies UI

The **Strategies** page (`/strategies`) is organised around **strategy definitions**, not running instances:

- **Left sidebar** — lists all strategy definitions (name, type, instrument, timeframe)
- **Scenarios tab** — create and manage scenarios per definition; lifecycle-aware action buttons per status
- **Compare tab** — side-by-side metric comparison between two scenarios of the same definition
- **Results tab** — trade-level analysis (17-column sortable table, MAE/MFE scatter, R-multiple histogram, streak chart)

Scenario row shows instrument symbol + timeframe stacked below the scenario name, inherited from the strategy definition.

---

## Repository Layout

```
rvs.AlgoTrader/
├── src/
│   ├── rvs.AlgoTrader.API/              # ASP.NET Core host, controllers, middleware, SignalR hubs
│   ├── rvs.AlgoTrader.Domain/           # Domain models, IStrategy, SignalResult, value objects
│   ├── rvs.AlgoTrader.Application/      # MediatR handlers, FluentValidation, use-case services
│   ├── rvs.AlgoTrader.Infrastructure/   # EF Core repos, SQL migrations, broker clients, Redis, Hangfire
│   ├── rvs.AlgoTrader.Backtesting/      # BacktestExecutionEngine, PerformanceCalculator, WalkForward
│   ├── rvs.AlgoTrader.Strategies/       # All IStrategy implementations (11 strategies)
│   ├── rvs.AlgoTrader.Brokers.Abstractions/  # IBrokerClient, IOrderManager contracts
│   ├── rvs.AlgoTrader.Brokers.MStock/   # mStock Type B API adapter
│   ├── rvs.AlgoTrader.Brokers.Zerodha/  # Zerodha Kite v3 adapter
│   └── rvs.AlgoTrader.Brokers.Upstox/   # Upstox v3 adapter
├── frontend/                            # React 19 admin UI (React Query, Recharts)
├── tests/
│   ├── rvs.AlgoTrader.Tests.Unit/       # 117 unit tests (strategy logic, indicators, handlers)
│   ├── rvs.AlgoTrader.UnitTests/        # 88 legacy unit tests
│   ├── rvs.AlgoTrader.Tests.Architecture/  # 14 architecture boundary tests (ArchUnitNET)
│   ├── rvs.AlgoTrader.IntegrationTests/ # Integration tests (require Docker)
│   └── rvs.AlgoTrader.Tests.UI/         # 22 Vitest frontend component tests
├── docs/
│   ├── ARCHITECTURE.md
│   ├── PLAN.md
│   ├── IMPLEMENTATION_STATUS.md
│   ├── STRATEGY_SPECS.md                # Authoritative per-strategy specifications
│   ├── UI_DESIGN_SPEC.md
│   ├── BACKTEST_WORKFLOW.md
│   ├── ANTI_PATTERNS.md
│   └── WORKFLOW.md
├── run-tests.sh                         # Test runner: unit | arch | integration | e2e | all
└── CLAUDE.md                            # AI agent instructions and hard rules
```

---

## Quick Start

```bash
# 1. Start infrastructure (TimescaleDB, Redis, RabbitMQ, Vault, Prometheus, Grafana)
docker compose up -d

# 2. Start API (runs DB migrations automatically on startup)
dotnet run --project src/rvs.AlgoTrader.API

# 3. Start frontend (proxies API to :62318)
cd frontend && npm run dev    # opens :3000

# 4. Run tests
./run-tests.sh unit           # fast — no Docker needed
./run-tests.sh arch           # architecture boundary checks
./run-tests.sh all            # requires Docker for integration/E2E
```

---

## Database Migrations

Migrations are plain SQL files in `src/rvs.AlgoTrader.Infrastructure/Persistence/Migrations/`. They **run automatically on every API startup** — no CLI, no manual steps.

### Adding a migration

```sql
-- 042_YourDescription.sql  (always next number, zero-padded to 3 digits)
-- Idempotent — always use IF NOT EXISTS
ALTER TABLE some_table ADD COLUMN IF NOT EXISTS new_col TEXT;
CREATE INDEX IF NOT EXISTS ix_some_table_col ON some_table (new_col);
```

Restart the API. `DatabaseMigrationRunner` auto-discovers all `*.sql` files in numeric order.

### Migration history (001–041)

| Range | What it covers |
|-------|----------------|
| 001–010 | Core schema: instruments, strategies, backtest_runs, broker_sessions, scenarios |
| 011–020 | Orders, spread positions/legs, forward test sessions, audit_log |
| 021–030 | Risk controls, capital allocation, alerts, scenario versions |
| 027 | TimescaleDB hypertable for `ohlcv_bars` |
| 031–040 | Strategy definitions/instances, IV history, option snapshots, market_news, app_config, symbol_data_prefs |
| 034 | `iv_history` table |
| 038 | Option chain snapshots |
| 039 | `market_news` feed |
| 040 | `app_config` + `symbol_data_prefs` |
| 041 | `app_config` schema — adds `value_json` + `actor` columns, backfills, repair check |

---

## Core Lifecycle (with Approval Gate)

```
Idea → Research → Backtest → Monte Carlo → Walk-Forward
     → Forward Test (Paper) → Approval Gate
     → Live Deploy → Risk Monitor → Trade Journal → Tax Export
```

Every strategy must pass **all five gates** before capital is deployed:

| Gate | Criteria |
|------|----------|
| 1. Backtest | Sharpe ≥ 1.0, PF ≥ 1.2, MaxDD < 25%, Trades ≥ 30 |
| 2. Monte Carlo | P(ruin) < 5%, P95 drawdown < daily loss limit × 15 |
| 3. Walk-Forward | Out-of-sample Sharpe ≥ 0.7× in-sample |
| 4. Paper Trade | 30+ paper trades, win rate matches expected |
| 5. Risk Review | Portfolio delta, correlation, margin all within limits |

---

## Monthly Engineering Review — Last Business Day of Every Month

> Every role below must complete their review checklist before the end of each month.

### SRE Review

- [ ] Prometheus metrics collecting? Grafana dashboards loading?
- [ ] All alerts firing correctly? No alert fatigue?
- [ ] SLO error budgets — any SLO below target for the month?
- [ ] Pre-market 08:45 IST readiness check passing every trading day?
- [ ] DB backup completed successfully? Restore tested in 30 days?
- [ ] Any new migrations without a `.down.sql` rollback?
- [ ] `ohlcv_bars` row count and compressed size vs. last month?
- [ ] Circuit breaker triggered this month? Root cause documented?

**Labels:** `sre` `bug` `data-integrity` `observability` `resilience`

### Performance Review

- [ ] `EXPLAIN ANALYZE` on top-5 slowest queries — any new seq scans?
- [ ] 1-year 5-min NIFTY backtest completing < 5s?
- [ ] 10k candle bulk insert < 400ms?
- [ ] Redis memory — any keys without TTL growing unbounded?
- [ ] Walk-forward 8-window completing < 10s?
- [ ] API p95 latency: `/api/backtest` < 10s, `/api/strategies` < 100ms?
- [ ] TimescaleDB compression running? Chunks > 7 days compressed?

**Labels:** `performance` `bug` `database` `cache`

---

## Start Here (New Developer / Agent)

1. [`CLAUDE.md`](CLAUDE.md) — AI agent hard rules, commands, architecture constraints
2. [`docs/IMPLEMENTATION_STATUS.md`](docs/IMPLEMENTATION_STATUS.md) — What is built (all P1–P9 complete)
3. [`docs/PLAN.md`](docs/PLAN.md) — Ordered implementation plan and current phase
4. [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — System design and layer boundaries
5. [`docs/STRATEGY_SPECS.md`](docs/STRATEGY_SPECS.md) — Authoritative per-strategy specifications
6. [`docs/BACKTEST_WORKFLOW.md`](docs/BACKTEST_WORKFLOW.md) — Backtest pipeline and data flow
7. [`docs/ANTI_PATTERNS.md`](docs/ANTI_PATTERNS.md) — What not to do (AP-001 through AP-022)
