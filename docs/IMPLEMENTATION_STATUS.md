## Legend
DONE | PARTIAL | STUB | MISSING | NOT_REVIEWED

## Core infrastructure
| Area | Status |
|---|---|
| Solution structure | PARTIAL |
| Backtest engine | DONE |
| Forward test engine | PARTIAL — FinalCapital persisted; candle-feed wiring NOT_REVIEWED |
| Live execution engine | NOT_REVIEWED |
| Broker integrations | NOT_REVIEWED |
| Candle pipeline | NOT_REVIEWED |
| Scheduling | NOT_REVIEWED |
| DB migrations (001–024) | DONE |
| Health checks | DONE |

## Strategies
| Area | Status |
|---|---|
| Strategy abstraction + factory | DONE — 10 strategies (3 equity + 7 options) |
| STRAT-001 VCP | DONE |
| STRAT-002 Fib spread | DONE |
| STRAT-003 PCR/OI/VWAP | DONE |
| Iron Condor / Straddle / Strangle / Calendar | DONE |
| Vertical spreads (all 4 types) | DONE |
| Scenarios (entity + overrides + parallel run + compare) | DONE |

## Quant / risk
| Area | Status |
|---|---|
| Position sizing (5 models) | DONE |
| Slippage + commission (IndianMarket) | DONE |
| Portfolio risk manager + kill switch | DONE |
| Greeks engine (B-S) | DONE |
| IV Rank service | DONE |
| Multi-timeframe context | DONE |
| Monte Carlo simulator | DONE |
| Strategy correlation analyser | DONE |
| Performance analytics (VaR, CVaR, Omega, MAE/MFE) | DONE |
| Trailing stop + scaling managers | DONE |
| Approval gate (P4) | DONE |
| Risk controls (portfolio-level) | PARTIAL — strategy-level not in LiveEngine |

## Data
| Area | Status |
|---|---|
| Historical data manager | DONE |
| Market breadth | DONE |
| Event calendar | DONE |
| Data-feed health monitor | DONE |
| Master data refresh | PARTIAL |

## Frontend + UI
| Area | Status |
|---|---|
| Top-nav layout | DONE |
| Strategy 4-tab layout (Definition/Scenarios/Deployments/Compare) | MISSING — see PROMPT.md PROMPT-001 |
| Strategy Definition Drawer | MISSING |
| Scenario Drawer (locked param overrides) | MISSING |
| Deployment Drawer (replaces Strategy Instance modal) | MISSING |
| Compare Tab (first-class research surface) | MISSING |
| Backtest replay + SignalR streaming | DONE |
| Trade Journal + P&L Analysis | DONE |
| Correlation heatmap + efficient frontier | DONE |
| Tests | PARTIAL |

## Update rule: revise affected rows only; never mark DONE without code support.
