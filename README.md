Production-focused algo trading platform for Indian markets.

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
