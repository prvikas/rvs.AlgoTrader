# CLAUDE.md
<!-- always loaded — keep under 200 lines -->

## Identity
repo: rvs.AlgoTrader | ns: rvs.AlgoTrader.*
stack: .NET 9 / C# 13 / React 19 / PostgreSQL+TimescaleDB / Redis / RabbitMQ
primary broker: mStock Type B API

## Session start (every session, no exceptions)
1. read this file
2. read docs/IMPLEMENTATION_STATUS.md — must stay under 50 lines
3. read docs/PLAN.md — identify current phase and next step
4. do not read docs/PROMPT.md unless explicitly requested
5. use docs/STRATEGY_SPECS.md as the only authoritative strategy source
6. state current phase + gaps + proposed next step
7. wait for confirmation before writing any code


## Model routing
use the right model per task to reduce token cost:

haiku:
- reading files for context
- updating IMPLEMENTATION_STATUS.md
- updating REQUIREMENTS_DELTA.md
- writing git commit messages
- doc-update skill tasks
- session-summary hook output
- simple search/replace edits

sonnet (default):
- implementing features
- writing tests
- refactoring existing code
- strategy implementation
- reviewing anti-patterns

opus (escalate only):
- designing new bounded contexts
- resolving complex architecture conflicts
- debugging hard multi-system issues
- planning a new phase from scratch

switch model: /model haiku | /model sonnet | /model opus
reduce thinking: /effort low (for simple tasks) | /effort high (for complex)
disable thinking for doc-only tasks: /config thinking false

## Priority workflow
research -> backtest -> forward test -> approval gate -> live deploy -> monitor

## Product focus
- prioritize backtest/forward test/live lifecycle
- mStock Type B is primary data + execution source
- no DB schema changes unless explicitly requested
- improve existing code; no greenfield rewrites
- future: screener, news, events, analytics

## Hard rules
- no DateTime.Now / DateTimeOffset.UtcNow — use IClock
- no partial candle in IStrategy.EvaluateAsync
- no cross-context direct calls
- backtesting never calls broker APIs
- forward testing never places real orders
- no hardcoded secrets — ever
- no secrets in logs or output
- no business config in appsettings
- no naked short options in any strategy
- no DB schema changes unless explicitly asked
- no MediatR for trivial CRUD
- standard business API envelope on all endpoints
- audit_log INSERT-only
- Idempotency-Key required on all order placement
- kill switch dual-writes Redis + DB
- capital reservation via atomic Redis Lua script
- never echo .env values, API keys, or tokens in output

## Architecture
layers: Domain <- Application <- Infrastructure <- API
contexts: TradingExecution | DataIngestion | Backtesting
execution engines:
  BacktestExecutionEngine — historical simulation only
  SimulatedExecutionEngine — forward test, no real orders
  LiveExecutionEngine — real broker execution

rule: same IStrategy + IIndicatorService logic across all 3 modes; only IExecutionEngine differs

## Strategy targets
STRAT-001: VCP swing (daily equity)
STRAT-002: Fibonacci hedged option spread
STRAT-003: Intraday PCR/OI/VWAP/gamma options
authoritative strategy definitions: docs/STRATEGY_SPECS.md
docs/STRATEGY.md is legacy background/reference only; do not use it for implementation decisions

## Data sources
primary: mStock Type B API
breadth: NSE Bhavcopy daily CSV (BreadthService)
events: NSE corporate calendar (EventCalendarService)
IV verification: mStock get_option_chain_data — must verify live response schema
see: docs/DATA_SOURCES.md

## Approval gate
live deployment requires:
- backtest CAGR >= threshold
- backtest max drawdown <= threshold
- min forward test days met
- manual approval recorded in strategy_approvals table
see: docs/WORKFLOW.md

## Anti-patterns
AP-001 DateTime.Now -> use IClock
AP-002 backtesting injecting broker -> use historical data only
AP-003 MediatR on trivial CRUD -> direct service call
AP-004 missing Idempotency-Key -> enforce before order processing
AP-005 non-atomic capital reserve -> single Redis Lua script
AP-006 hardcoded secret -> ISecretsProvider
AP-007 partial candle in strategy -> closed candle events only
AP-008 missing correlation ID in logs -> Serilog enrichment
AP-009 UPDATE/DELETE on audit_log -> INSERT only
AP-010 broker HTTP without Polly -> retry + circuit + timeout
AP-011 order without market calendar check -> validate session first
AP-012 frontend submit without idempotency key -> crypto.randomUUID() per submit
AP-013 schema change without migration -> not allowed unless requested
AP-014 timeseries query without time range -> always bound timestamps
AP-015 kill switch ignored on restart -> always blocks auto-resume
AP-016 candle aggregation using static clock -> use IClock
AP-017 silent cold restart -> surface event in UI
AP-018 secret or API key echoed in output -> never log or print secrets

## Reference repos
see: docs/REFERENCES.md

## Docs map
docs/PLAN.md | docs/ARCHITECTURE.md | docs/WORKFLOW.md
docs/IMPLEMENTATION_STATUS.md | docs/REQUIREMENTS_DELTA.md
docs/STRATEGY_SPECS.md | docs/DATA_SOURCES.md
docs/APPROVAL_CRITERIA.md | docs/REFERENCES.md
SELF_LEARNING.md

## Docs loading rules
- do not read docs/STRATEGY.md for implementation; it is legacy/reference only
- keep docs/IMPLEMENTATION_STATUS.md under 50 lines
- prefer linked detailed docs over repeating large content here

## Post-change updates
after meaningful changes always update:
- docs/IMPLEMENTATION_STATUS.md
- docs/REQUIREMENTS_DELTA.md if requirements changed
- docs/STRATEGY_SPECS.md if strategy rules changed
- SELF_LEARNING.md if repeatable mistake found
