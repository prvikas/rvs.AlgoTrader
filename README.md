Production-focused algo trading platform for Indian markets.

## Core concepts

### Strategy
A reusable trading logic definition built from one or more technical indicators, with explicit rules for entries, exits, and risk management. A strategy defines *what* to trade and *how* — it does not specify which parameter values to use for a given run.

In code: `StrategyInstance` entity (table: `strategy_instances`). Holds the strategy type (e.g. `PriceActionBreakout`), a base `ParametersJson` object with sensible defaults, and execution context (symbol, timeframe, capital).

### Scenario
A specific, enumerable parameter configuration attached to a strategy. Each scenario stores only the keys that *differ* from the strategy's base parameters (`ParametersJsonOverride`). The engine computes **effective parameters** = `merge(strategy.ParametersJson, scenario.ParametersJsonOverride)` before every run.

Examples for a strategy using EMA + MACD:
| Scenario | Override |
|---|---|
| Default | `{}` (use base) |
| EMA-200 | `{"EmaLength":200}` |
| Fast MACD | `{"MacdFast":21,"MacdSlow":55,"MacdSignal":13}` |
| Conservative | `{"EmaLength":200,"MacdFast":21,"MacdSlow":55,"AtrStopMultiple":3.0}` |

In code: `StrategyScenario` entity (table: `strategy_scenarios`). One strategy → one or many scenarios.

### Relationship
```
Strategy (StrategyInstance)
  └── Scenario A (ParametersJsonOverride: {})
  └── Scenario B (ParametersJsonOverride: {"EmaLength":200})
  └── Scenario C (ParametersJsonOverride: {"EmaLength":200,"MacdFast":21})
        └── BacktestRun  (EffectiveParametersJson: merged snapshot, immutable)
        └── BacktestRun  ...
```

Scenarios are the unit of lifecycle promotion: `Draft → Backtested → ForwardTest → Live → Archived`. A scenario cannot go to `ForwardTest` without a completed backtest, and cannot go `Live` without passing `ForwardTest`.

## Core lifecycle
research -> backtest -> forward test -> approve -> live deploy -> monitor

## Repo intent
This is an evolving implementation, not a greenfield architecture exercise.
Claude must inspect code first, then update docs, then implement the smallest safe change.

## Current focus
- backtesting
- forward testing
- live deployment workflow
- strategy parity across modes

## Future modules
- screener
- news
- events
- analytics

## Main paths
- `src/` backend
- `frontend/` UI
- `tests/` test suites
- `docs/` roadmap, architecture, workflow, status, deltas, strategy specs
- `.claude/` Claude Code skills and hooks

## Start here
1. `CLAUDE.md`
2. `docs/IMPLEMENTATION_STATUS.md`
3. `docs/PLAN.md`
4. `docs/REQUIREMENTS_DELTA.md`

## Rule
Do not assume missing features.
Verify in code first.

---

## Database migrations

Migrations are plain SQL files. They **run automatically on every API startup** — no CLI commands, no manual steps.

### Location

```
src/rvs.AlgoTrader.Infrastructure/Persistence/Migrations/
```

### How the runner works

`DatabaseMigrationRunner` runs before the app accepts any requests:

1. Ensures the database exists.
2. Creates a `schema_migrations` tracking table (once, ever).
3. Sorts all `*.sql` files by filename (lexicographic — numeric prefix determines order).
4. For each file **not yet recorded** in `schema_migrations`: executes it, then records it.
5. Already-applied files are skipped on every subsequent startup.

### Adding a migration

Create a new file with the next number in sequence:

```
008_YourDescription.sql
```

Write idempotent SQL — always use `IF NOT EXISTS` / `ADD COLUMN IF NOT EXISTS`:

```sql
-- 008_YourDescription.sql
-- Idempotent — safe to run multiple times.

ALTER TABLE some_table
    ADD COLUMN IF NOT EXISTS new_column TEXT;

CREATE INDEX IF NOT EXISTS ix_some_table_new_column
    ON some_table (new_column);
```

Restart the API. Done. **`Program.cs` never changes.**

### Naming convention

Use zero-padded 3-digit prefixes so sort order is always correct:
`008_`, `009_`, `010_`, `011_` …

### Rules

| Do | Don't |
|---|---|
| Create a new numbered file for every schema change | Edit an already-applied migration file |
| Use `IF NOT EXISTS` / `ADD COLUMN IF NOT EXISTS` | Use `dotnet ef migrations add` (EF auto-migrations are disabled) |
| Let the runner apply it on startup | Add migration code to `Program.cs` |

### Inspect applied migrations

```sql
SELECT name, applied_at FROM schema_migrations ORDER BY applied_at;
```

### Current migration history

| File | What it does |
|---|---|
| `InitialMigration.sql` | Baseline schema — all core tables (idempotent; sets `gen_random_uuid()` defaults for EF compat) |
| `002_InstrumentUniverse.sql` | Instrument universe table + default seed rows |
| `003_FixInstrumentColumns.sql` | Derivative instrument columns (underlying, strike, expiry) |
| `004_BacktestAndForwardTestTrades.sql` | Backtest runs + forward test sessions/trades (incl. max_drawdown, sharpe_ratio, source_backtest_id) |
| `005_BacktestExtendedStats.sql` | Extended stats columns on `backtest_runs` |
| `006_StrategyInstancePnl.sql` | Intraday P&L columns on `strategy_instances` |
| `007_StrategyScenarios.sql` | `strategy_scenarios` table + scenario columns on `backtest_runs` |
| `008_BrokerSessions.sql` | `broker_sessions` table for token persistence across restarts |
