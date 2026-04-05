# CLAUDE.md
<!-- always loaded — keep under 80 lines -->

## Identity
repo: rvs.AlgoTrader | stack: .NET 9 / C# 13 / React 19 / TimescaleDB / Redis / RabbitMQ

## Session start (every session)
1. read docs/IMPLEMENTATION_STATUS.md
2. read docs/PLAN.md — current phase + next step
3. do NOT read docs/PROMPT.md unless requested
4. use docs/STRATEGY_SPECS.md as only authoritative strategy source
5. state phase + gaps + proposed next step; wait for confirmation before writing code

## Commands
```
dotnet build rvs.AlgoTrader.sln
dotnet run --project src/rvs.AlgoTrader.API
cd frontend && npm run dev        # :3000
./run-tests.sh [unit|arch|integration|e2e|all]
docker compose up -d
```

## Assemblies
Domain | Application | Infrastructure | API | Backtesting | Strategies
Brokers: Abstractions | MStock | Zerodha | Upstox
Tests: Unit | UnitTests | Architecture | IntegrationTests | UI

## Git
- commit directly to master — no feature branches, no PRs

## Model routing
haiku: reads, doc updates, commit messages | sonnet: features, tests, refactoring (default) | opus: new bounded contexts, arch conflicts

## Frontend rules (full spec: docs/UI_DESIGN_SPEC.md)
- colors from `frontend/src/styles/tokens.ts` — no raw hex (AP-020)
- top-nav 36px, no left sidebar (AP-021); right drawer 520px for forms (AP-022)
- bg `#090910` / surface `#0d0d17`; green `#00d07a` / red `#ff4757`
- table row `5px 10px`; metric values monospace 22px max

## Hard rules (full list: docs/ANTI_PATTERNS.md)
- no DateTime.Now — IClock (AP-001, AP-016)
- no partial candle in EvaluateAsync (AP-007)
- no hardcoded secrets (AP-006, AP-018)
- no MediatR for trivial CRUD (AP-003)
- audit_log INSERT-only (AP-009); kill switch dual-writes Redis+DB (AP-015)
- Idempotency-Key on orders (AP-004); capital reserve via Redis Lua (AP-005)
- standard API envelope; no schema change unless asked (AP-013)
- migrations: numbered *.sql in Persistence/Migrations/ — never edit applied file
- broker HTTP via Polly (AP-010); timeseries must bound timestamps (AP-014)
- validate market calendar before orders (AP-011); all logs carry correlation ID (AP-008)
- frontend order submit: crypto.randomUUID() (AP-012)
- no naked short options; no business config in appsettings
- scenario drawer: no adding indicators, param list locked to parent strategy (AP-023)
- strategy creation: no symbol/broker/capital fields — deployment layer only (AP-024)
- Compare tab is mandatory on Strategies page (AP-025)

## Architecture
layers: Domain <- Application <- Infrastructure <- API
engines: BacktestExecutionEngine | SimulatedExecutionEngine (fwd) | LiveExecutionEngine
same IStrategy + IIndicatorService across all 3; only IExecutionEngine differs
shared: IClock | ICandleCache | ICapitalAllocator | IStrategyScheduler | ISecretsProvider
full detail: docs/ARCHITECTURE.md

## Domain model (Strategies)
Strategy (logic) → Scenario (param overrides) → Deployment (symbol/broker/capital) → Run
Spec: docs/STRATEGY_SPECS.md | Legacy docs/STRATEGY.md: do not use

## Workflow
research → backtest → forward test → approval gate → live → monitor
live requires CAGR/DD thresholds + min fwd days + manual approval in strategy_approvals
detail: docs/WORKFLOW.md | docs/APPROVAL_CRITERIA.md

## Docs map
PLAN.md | ARCHITECTURE.md | WORKFLOW.md | IMPLEMENTATION_STATUS.md
REQUIREMENTS_DELTA.md | STRATEGY_SPECS.md | DATA_SOURCES.md
APPROVAL_CRITERIA.md | REFERENCES.md | UI_DESIGN_SPEC.md | ANTI_PATTERNS.md
BACKTEST_WORKFLOW.md | PROMPT.md | SELF_LEARNING.md
- IMPLEMENTATION_STATUS.md: keep under 50 lines
- STRATEGY.md: legacy, do not use

## Post-change updates
update: IMPLEMENTATION_STATUS.md | REQUIREMENTS_DELTA.md (if reqs changed) | STRATEGY_SPECS.md (if strategy rules changed) | SELF_LEARNING.md (if repeatable mistake)

## Doc hygiene rules (token cost — enforce always)
- CLAUDE.md: hard limit 80 lines; no inline code samples — rules only; code examples live in the relevant spec doc
- IMPLEMENTATION_STATUS.md: hard limit 50 lines; status + one-line note only — no narrative paragraphs
- PROMPT.md: each prompt entry must be deleted (or replaced with a one-line stub) immediately after implementation is confirmed done; stale prompts waste tokens on every read
- dead doc references: if a file is listed in the Docs map but deleted or fully superseded, remove it from the map in the same commit
- no duplicate content: if a rule exists in ANTI_PATTERNS.md, do not restate it verbatim in any other doc — reference the AP code only
- no speculative docs: do not create a new doc file for a feature that has not started; use REQUIREMENTS_DELTA.md for pending decisions
- SELF_LEARNING.md: one bullet per lesson; never exceed 30 lines; prune resolved lessons on each update
