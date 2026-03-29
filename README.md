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
- An **Execution Researcher** models realistic slippage, bid-ask spreads, and market impact before deployment
- A **Quant Developer** extends the platform via clean interfaces (`IStrategy`, `IIndicatorLibrary`, `IBrokerClient`) without touching core infrastructure

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

---

## Core Lifecycle

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
| 4. Paper Trade | 30+ paper trades match expected win rate |
| 5. Risk Review | Portfolio delta, correlation, margin all within limits |

---

## Roadmap — Milestones

> Track progress at: **[github.com/prvikas/rvs.AlgoTrader/milestones](https://github.com/prvikas/rvs.AlgoTrader/milestones)**

### v0.1 — Core Architecture Foundation
**Status: 🔨 In Progress**

All shared contracts, DI wiring, indicator library, candlestick pattern detector, broker abstraction, alert service, and VWAP/session infrastructure. Everything in v0.2–v0.8 depends on this.

- `IStrategy` / `StrategyContext` / `SignalResult` contracts
- `IIndicatorLibrary` — SMA, EMA, ATR, VWAP, Bollinger, RSI, MACD, Stochastic, ADX, OBV, MFI, Williams %R, CCI, Pivot Points, Swing Point detection, Fibonacci levels
- `CandlePatternDetector` — 25+ patterns (Doji, Engulfing, Hammer, Inside Bar, 3 Soldiers, etc.)
- Broker abstraction (`IBrokerClient`, `IOrderManager`, `IInstrumentTokenResolver`)
- `IAlertService` — Telegram, email, webhooks
- DI registration framework for strategies
- VWAP session anchor fix (09:15 IST reset)
- `ITradingCalendarService` — NSE holidays, expiry dates

### v0.2 — Data Layer & Market Data
**Status: 📋 Planned**

Data quality and completeness is the foundation of all research. Garbage data = garbage backtests.

- `IHistoricalDataManager` — bulk OHLCV import, gap detection, corporate action adjustments (splits, dividends, bonus)
- `IEventCalendarService` — RBI MPC, FOMC, earnings, expiry dates, union budget
- `IMarketBreadthService` — % stocks above SMA20/50/200, Advance/Decline line, New Highs/Lows, Market Regime (Bull/Bear/CrashMode)
- `IDataFeedHealthMonitor` — WebSocket reconnection, exponential backoff, stale data detection, strategy pause on feed loss
- Option chain service — live + historical OI, PCR, IV per strike, multi-expiry support

### v0.3 — Options Engine
**Status: 📋 Planned**

Options are first-class instruments. Greek calculation, IV modeling, and multi-leg execution are prerequisites for all options strategies.

- `IBlackScholesEngine` — European options pricing, full Greeks (Delta, Gamma, Theta, Vega, Rho)
- `IImpliedVolatilityCalculator` — Newton-Raphson IV solver, IV surface, IV rank, IVP
- `IOptionLegSelector` — strike selection by Delta / ATM / OTM offset / fixed price
- `ISpreadOrderManager` — atomic multi-leg placement (`SpreadLeg`, `SpreadOrderRequest`, `CorrelationId`)
- `spread_positions` + `spread_legs` DB tables
- `OptionChainSnapshot` — full OI, PCR, IV, PCR(ΔOI), multi-expiry support

### v0.4 — Risk & Execution Engine
**Status: 📋 Planned**

Capital preservation is non-negotiable. No live strategy runs without passing every risk check on every order.

- `IPortfolioRiskManager` — Daily Loss Limit circuit breaker, max positions, margin utilisation, net delta cap, per-symbol exposure cap
- `IPositionSizingEngine` — Fixed Fractional, ATR-based, Kelly Criterion (half-Kelly capped), Volatility Targeting
- `ISlippageModel` — None / Fixed / Percentage / Bid-Ask / ATR-fraction / Volume impact
- `ICommissionModel` — Zerodha, Upstox, mStock with full Indian charge breakdown (STT, exchange fee, GST, SEBI turnover, stamp duty)
- `ITrailingStopManager` — Fixed / Trailing % / Trailing ATR / Break-Even / Time Stop / Chandelier
- Paper Trading Mode (`ExecutionMode.Paper`) — simulated fills at next bar open, virtual P&L, LIVE badge vs PAPER badge in UI
- `IScalingManager` — pyramid-in tranches (70/30 VCP default), partial profit-taking, break-even after first exit

### v0.5 — Strategy Implementations
**Status: 📋 Planned** *(Depends on v0.1–v0.4)*

| Strategy | Type | Key Signal |
|----------|------|------------|
| STRAT-001 VCP Swing | Equity swing | Volatility Contraction Pattern breakout on daily TF |
| STRAT-002 Fib Spread | Options credit spread | Fibonacci retracement zone + IV rank filter |
| STRAT-003 Intraday PCR/VWAP | Intraday options | PCR(ΔOI) bias + delta-targeted strike + VWAP entry |
| STRAT-004 Iron Condor | Options range | 4-leg defined-risk, theta decay, IVR filter |
| STRAT-005 Short Straddle | Options premium | ATM CE+PE, IVR > 50, naked selling gate |
| STRAT-006 Short Strangle | Options premium | OTM 15-delta, naked selling gate, circuit breaker |
| STRAT-007 Calendar Spread | Options vol | Near-expiry sell + far-expiry buy, IV term structure |
| STRAT-008 Vertical Spreads | Options directional | Bull Call / Bear Put / Bull Put / Bear Call |

### v0.6 — Multi-Timeframe & Advanced Signals
**Status: 📋 Planned**

- `ICandleAggregator` — 5m → 15m → 1H → Daily aggregation
- `StrategyContext.Candles15Min / Candles1Hour / CandlesDaily` — higher TF arrays pre-populated
- MTF alignment filter — block 5m buy if 15m price below EMA21
- `IStrategyCorrelationAnalyser` — Pearson correlation matrix, high-correlation warnings on deploy
- Market Regime context injected into every `EvaluateAsync` call

### v0.7 — Research & Analytics
**Status: 📋 Planned**

- `IPerformanceCalculator` — Sharpe, Sortino, Calmar, Omega, Information Ratio, MAE/MFE, Edge Ratio, VaR 95%, CVaR, Ulcer Index, Return Skewness/Kurtosis
- `IMonteCarloSimulator` — 5000 randomised trade sequences, P(ruin), P95 drawdown, RobustnessVerdict
- `PortfolioConstructionResult` — Markowitz efficient frontier (10,000 Monte Carlo portfolios), diversification ratio, optimal weights
- Deployment Rating auto-generated: **Strong / Acceptable / Risky / Do Not Deploy**
- Walk-Forward Optimiser (anchored + rolling windows)

### v0.8 — Trade Journal & Production Readiness
**Status: 📋 Planned**

- `TradeJournalEntry` — per-trade R-multiple, MAE/MFE, entry/exit reason, manual notes + tags
- P&L Attribution — by strategy, symbol, month, day-of-week, session (morning/afternoon), exit type
- Tax Lot Report — ITR-3 Schedule CG / Business Income export, STT per trade, speculative vs non-speculative classification (April–March FY)
- Admin UI complete: Trade Journal page, Portfolio Analysis page, Event Calendar page, Data Manager page, Risk Dashboard
- Smoke tests, health checks, production deployment scripts

---

## Tech Stack

| Layer | Technology |
|-------|------------|
| Backend API | ASP.NET Core 8, C# 12, minimal APIs |
| ORM / DB | PostgreSQL, Dapper, raw SQL migrations |
| Real-time | WebSocket (broker feeds), SignalR (UI push) |
| Options Math | Custom Black-Scholes engine, Newton-Raphson IV solver |
| Broker APIs | Zerodha Kite v3, Upstox v3, mStock |
| Frontend | React / TypeScript, Recharts, Tailwind CSS |
| Testing | xUnit, FluentAssertions, Moq |
| CI/CD | GitHub Actions |
| AI Tooling | Claude (Anthropic) for code generation and PR reviews |

---

## Repository Layout

```
rvs.AlgoTrader/
├── src/
│   ├── rvs.AlgoTrader.Api/              # ASP.NET Core host, endpoints, middleware
│   ├── rvs.AlgoTrader.Core/             # Domain models, IStrategy, SignalResult, contracts
│   ├── rvs.AlgoTrader.Strategies/       # All IStrategy implementations
│   ├── rvs.AlgoTrader.Indicators/       # IIndicatorLibrary, CandlePatternDetector
│   ├── rvs.AlgoTrader.Backtesting/      # BacktestEngine, PerformanceCalculator, MonteCarlo
│   ├── rvs.AlgoTrader.LiveExecution/    # LiveEngine, PaperEngine, OrderManager, RiskManager
│   ├── rvs.AlgoTrader.Options/          # BlackScholes, Greeks, IV, OptionLegSelector
│   ├── rvs.AlgoTrader.Infrastructure/   # PostgreSQL repos, migrations, broker clients
│   └── rvs.AlgoTrader.Notifications/   # Telegram, email, webhook alerts
├── frontend/                            # React admin UI
├── tests/
│   ├── unit/                            # Strategy logic, indicator calculations
│   ├── integration/                     # API + DB integration tests
│   └── smoke/                           # End-to-end smoke tests
├── docs/
│   ├── ARCHITECTURE.md
│   ├── STRATEGY_SPECS.md                # Per-strategy detailed specs
│   ├── UI_DESIGN_SPEC.md
│   ├── BACKTEST_WORKFLOW.md
│   ├── IMPLEMENTATION_STATUS.md
│   ├── PLAN.md
│   └── AGENT_INSTRUCTIONS.md            # How AI agents must work on this repo
├── scripts/
│   └── setup-milestones.sh
└── .github/
    └── workflows/
        ├── setup-milestones.yml
        └── ci.yml
```

---

## Database Migrations

Migrations are plain SQL files in `src/rvs.AlgoTrader.Infrastructure/Persistence/Migrations/`. They **run automatically on every API startup** — no CLI, no manual steps.

### Adding a migration

```sql
-- 009_YourDescription.sql  (always next number, zero-padded to 3 digits)
-- Idempotent — always use IF NOT EXISTS
ALTER TABLE some_table ADD COLUMN IF NOT EXISTS new_col TEXT;
CREATE INDEX IF NOT EXISTS ix_some_table_col ON some_table (new_col);
```

Restart the API. Done. `Program.cs` never changes.

### Current migration history

| File | What it does |
|------|--------------|
| `InitialMigration.sql` | Baseline schema — all core tables |
| `002_InstrumentUniverse.sql` | Instrument universe + seed rows |
| `003_FixInstrumentColumns.sql` | Derivative instrument columns |
| `004_BacktestAndForwardTestTrades.sql` | Backtest runs + forward test sessions/trades |
| `005_BacktestExtendedStats.sql` | Extended stats on backtest_runs |
| `006_StrategyInstancePnl.sql` | Intraday P&L on strategy_instances |
| `007_StrategyScenarios.sql` | strategy_scenarios table |
| `008_BrokerSessions.sql` | broker_sessions for token persistence |

---

## For AI Agents — How to Work on This Repo

> See `docs/AGENT_INSTRUCTIONS.md` for full agent protocol.

### Golden Rule
**Inspect code first. Update docs second. Implement third. Never assume a feature exists — verify in `src/`.**

### Working from milestones

1. Check [GitHub Milestones](https://github.com/prvikas/rvs.AlgoTrader/milestones) for current active milestone
2. Pick the **lowest-numbered open milestone** (v0.1 before v0.2, etc.)
3. Within the milestone, pick issues tagged `bug` before `enhancement`
4. Read the issue's **Acceptance Criteria** checklist — implement every checkbox
5. Write unit tests first (TDD preferred), then implementation
6. All new code must have: XML doc comments, unit tests, DB migration (if schema changes)
7. Open a PR linked to the issue (`Closes #N`)
8. PR must pass CI before merge

### Do not
- Skip milestones (v0.3 Options Engine cannot be worked on until v0.1 contracts are done)
- Implement without reading the issue body — every issue has design sketches and acceptance criteria
- Add new dependencies without architectural justification
- Break existing passing tests

---

## Start Here (New Developer / Agent)

1. [`CLAUDE.md`](CLAUDE.md) — AI agent instructions and code conventions
2. [`docs/IMPLEMENTATION_STATUS.md`](docs/IMPLEMENTATION_STATUS.md) — What is built vs planned
3. [`docs/PLAN.md`](docs/PLAN.md) — Ordered implementation plan
4. [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — System design
5. [`docs/STRATEGY_SPECS.md`](docs/STRATEGY_SPECS.md) — Per-strategy detailed specs
6. [`docs/AGENT_INSTRUCTIONS.md`](docs/AGENT_INSTRUCTIONS.md) — Agent workflow protocol
7. [GitHub Issues](https://github.com/prvikas/rvs.AlgoTrader/issues) — All tracked work items
8. [GitHub Milestones](https://github.com/prvikas/rvs.AlgoTrader/milestones) — Ordered delivery plan
