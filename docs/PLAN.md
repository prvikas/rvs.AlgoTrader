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
status: DONE
goals:
- LiveExecutionEngine with mStock broker
- capital controls, kill switch, idempotent orders

### P6 Strategy implementation
status: DONE
goals:
- STRAT-001 VCP
- STRAT-002 Fibonacci option spread
- STRAT-003 Intraday PCR/OI/VWAP

### P7 Data services
status: DONE
goals:
- BreadthService via NSE Bhavcopy — NseBhavcopyCandleSource + BreadthCalculatorJob wired
- EventCalendarService via NSE corporate calendar — NseEventCalendarImporter + POST /api/event-calendar/import
- IVHistoryService for IVP computation — migration 034, IvHistoryService, SQL PERCENT_RANK()
- mStock option chain field mappings documented in DATA_SOURCES.md (VERIFY_LIVE protocol)

### P8 MCP integration
status: DONE
goals:
- expose key trading operations via MCP server
- enable Claude Code to query strategy status
- enable Claude Code to check backtest results
- reference: https://github.com/marketcalls/openalgo-mcp
- design: docs/MCP_DESIGN.md — 3 endpoints: GET /mcp/strategy-status, GET /mcp/backtest-results/{id}, POST /mcp/kill-switch
- implemented: McpController.cs — all 3 endpoints, JWT auth, RiskManager policy on kill-switch, standard ApiResponse envelope

### P9 Expansion
status: DONE
goals:
- Screener: IScreenerService + ScreenerService (SQL CTE over candles table); GET /api/screener; ScreenerJob (daily 5PM IST); signals: VCP_BREAKOUT/NEAR_BREAKOUT/UPTREND; RS score ranking
- News: migration 039 (market_news table); INewsService + NewsService + NewsController (GET/POST/DELETE /api/news); screenerApi + newsApi in client.ts; external NSE feed marked VERIFY_LIVE
- Events: FnoExpirySeedJob (monthly Hangfire job auto-seeds current+next year F&O expiries via existing SeedFnoExpiriesAsync)
- Analytics: ByStrategy added to PnlAttributionDto; GetAttributionAsync joins strategy_instances for strategy name labels; PortfolioAnalysisPage gains date range filter + By Strategy BarChart

## Resume rule
session start: read CLAUDE.md -> IMPLEMENTATION_STATUS.md -> this plan
confirm phase and next step before any code

## Done criteria
phase done only when: code + tests + docs + status all updated
