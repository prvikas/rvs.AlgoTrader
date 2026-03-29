# CLAUDE.md
<!-- always loaded — keep under 100 lines; full detail lives in docs/ -->

## Identity
repo: rvs.AlgoTrader | ns: rvs.AlgoTrader.* | stack: .NET 9 / C# 13 / React 19 / TimescaleDB / Redis / RabbitMQ | broker: mStock Type B API

## Session start (every session, no exceptions)
1. read this file
2. read docs/IMPLEMENTATION_STATUS.md — must stay under 50 lines
3. read docs/PLAN.md — identify current phase and next step
4. do not read docs/PROMPT.md unless explicitly requested
5. use docs/STRATEGY_SPECS.md as the only authoritative strategy source
6. state current phase + gaps + proposed next step
7. wait for confirmation before writing any code

## Commands
```bash
dotnet build rvs.AlgoTrader.sln
dotnet run --project src/rvs.AlgoTrader.API
cd frontend && npm run dev        # :3000, proxies API to :62318
./run-tests.sh [unit|arch|integration|e2e|all]
dotnet test tests/rvs.AlgoTrader.Tests.Unit --filter "FullyQualifiedName~MyTestName"
docker compose up -d              # TimescaleDB, Redis, RabbitMQ, Vault, Prometheus, Grafana
```

## Git workflow
- **never create feature branches** — all edits go directly to master
- no PRs; no `git checkout -b` / `git switch -c` under any circumstances

## Model routing
haiku: reading files, doc/status updates, commit messages, simple edits
sonnet (default): features, tests, refactoring, strategy code
opus (escalate only): new bounded contexts, architecture conflicts, multi-system debug, new phase planning
switch: /model haiku | /model sonnet | /model opus | /effort low | /effort high

## Frontend UI rules
spec: docs/UI_DESIGN_SPEC.md — read before any frontend change
- colors/spacing from `frontend/src/styles/tokens.ts` — never raw hex inline (AP-020)
- top-nav (36px) + full-width — NO left sidebar (AP-021); right-side drawer for forms 520px (AP-022)
- padding: `12px 16px` content, `5px 10px` table rows; metric values: monospace 22px max
- bg: `#090910` page / `#0d0d17` surfaces; green `#00d07a` / red `#ff4757`
- no emoji in nav; min-width 1280px; no mobile breakpoints

## Hard rules
- no DateTime.Now — use IClock (AP-001, AP-016)
- no partial candle in EvaluateAsync (AP-007); backtesting never calls broker; fwd test never places real orders (AP-002)
- no hardcoded secrets; never echo secrets/keys in output (AP-006, AP-018)
- no cross-context direct calls; no MediatR for trivial CRUD (AP-003)
- audit_log INSERT-only (AP-009); kill switch dual-writes Redis+DB (AP-015)
- Idempotency-Key on all orders (AP-004); capital reserve via atomic Redis Lua (AP-005)
- standard API envelope on all endpoints; no DB schema changes unless asked (AP-013)
- DB migrations: `*.sql` in `src/.../Persistence/Migrations/` auto-discovered in numeric order by `DatabaseMigrationRunner`; never modify an applied migration — add a new numbered file
- broker HTTP must use Polly (AP-010); timeseries queries must bound timestamps (AP-014)
- frontend order submit: crypto.randomUUID() per submit (AP-012)
- no naked short options in any strategy; no business config in appsettings
see full list: docs/ANTI_PATTERNS.md

## Architecture
layers: Domain <- Application <- Infrastructure <- API
contexts: TradingExecution | DataIngestion | Backtesting
engines: BacktestExecutionEngine (historical) | SimulatedExecutionEngine (fwd, no real orders) | LiveExecutionEngine (broker)
rule: same IStrategy + IIndicatorService across all 3 modes; only IExecutionEngine differs
see: docs/ARCHITECTURE.md

## Strategy targets
STRAT-001: VCP swing (daily equity) | STRAT-002: Fibonacci hedged option spread | STRAT-003: Intraday PCR/OI/VWAP/gamma options
authoritative: docs/STRATEGY_SPECS.md — docs/STRATEGY.md is legacy, do not use for implementation

## Workflow & approval gate
research → backtest → forward test → approval gate → live deploy → monitor
live requires: CAGR/drawdown thresholds + min fwd-test days + manual approval in strategy_approvals
see: docs/WORKFLOW.md | docs/APPROVAL_CRITERIA.md | docs/DATA_SOURCES.md

## Docs map
docs/PLAN.md | docs/ARCHITECTURE.md | docs/WORKFLOW.md | docs/IMPLEMENTATION_STATUS.md
docs/REQUIREMENTS_DELTA.md | docs/STRATEGY_SPECS.md | docs/DATA_SOURCES.md
docs/APPROVAL_CRITERIA.md | docs/REFERENCES.md | docs/UI_DESIGN_SPEC.md | docs/ANTI_PATTERNS.md | SELF_LEARNING.md
- keep docs/IMPLEMENTATION_STATUS.md under 50 lines
- docs/STRATEGY.md is legacy — never use for implementation decisions

## Post-change updates
update after meaningful changes: docs/IMPLEMENTATION_STATUS.md | docs/REQUIREMENTS_DELTA.md (if reqs changed) | docs/STRATEGY_SPECS.md (if strategy rules changed) | SELF_LEARNING.md (if repeatable mistake found)
