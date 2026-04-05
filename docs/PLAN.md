# PLAN.md

## Current objective
Build reliable strategy lifecycle:
research -> backtest -> forward test -> approval -> live deploy

## Phases

### P1 Repo audit
status: DONE
goals:
- inspect current implementation
- update IMPLEMENTATION_STATUS.md
- identify gaps vs lifecycle goal

### P2 Backtest foundation
status: DONE
goals:
- verify historical data path via mStock
- verify BacktestExecutionEngine flow
- reproducibility, metrics, trade log output

### P3 Forward test foundation
status: DONE
goals:
- SimulatedExecutionEngine parity with backtest
- same signal logic verified via parity tests

### P4 Approval gate
status: DONE
goals:
- ApprovalService with threshold checks
- strategy_approvals table
- manual approval UI
- block live deployment without approval record

### P5 Live deployment
status: TODO
goals:
- LiveExecutionEngine with mStock broker
- capital controls, kill switch, idempotent orders

### P6 Strategy implementation
status: TODO
goals:
- STRAT-001 VCP
- STRAT-002 Fibonacci option spread
- STRAT-003 Intraday PCR/OI/VWAP

### P7 Data services
status: TODO
goals:
- BreadthService via NSE Bhavcopy
- EventCalendarService via NSE corporate calendar
- IVHistoryService for IVP computation
- verify mStock option chain IV/Greeks live

### P8 MCP integration (placeholder)
status: PLACEHOLDER
goals:
- expose key trading operations via MCP server
- enable Claude Code to query strategy status
- enable Claude Code to check backtest results
- reference: https://github.com/marketcalls/openalgo-mcp

### P9 Expansion
status: TODO
goals:
- screener
- news
- events
- analytics

## Resume rule
session start: read CLAUDE.md -> IMPLEMENTATION_STATUS.md -> this plan
confirm phase and next step before any code

## Done criteria
phase done only when: code + tests + docs + status all updated
