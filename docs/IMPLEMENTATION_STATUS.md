\## Status legend

DONE | PARTIAL | STUB | MISSING | NOT\_REVIEWED



\## Product areas



| Area | Status | Notes |

|---|---|---|

| Solution structure | PARTIAL | repo exists with backend, frontend, tests, Claude kit |

| Claude docs | PARTIAL | strong content, too verbose, needs restructuring |

| Strategy abstraction | DONE | StrategyFactory w/ 3 implementations: PriceActionBreakout, EmaVwapMomentum, AlertCandleShort |

| Backtest engine | PARTIAL | BacktestEngine core logic complete; added `/api/backtest/download-history` endpoint; fixed error feedback to frontend |

| Forward test engine | NOT\_REVIEWED | |

| Live execution engine | NOT\_REVIEWED | |

| Broker integrations | NOT\_REVIEWED | |

| Candle pipeline | NOT\_REVIEWED | |

| Scheduling | NOT\_REVIEWED | |

| Risk controls | NOT\_REVIEWED | |

| UI workflow | PARTIAL | Top-nav layout done, MetricCards + tables redesigned, forms use right drawers; pre-existing TS errors in InstrumentTypesPage/UniversePage unrelated |
| Master data refresh | PARTIAL | Fixed: MStock wrapped-JSON parsing, missing DB columns (003 migration), instrument_universe seeding, broken app_config INSERTs, wizard universe-filter passthrough (was saving only 60/25k instruments) |

| Tests | NOT\_REVIEWED | |



\## Known doc findings

\- current memory docs are too large for efficient startup context

\- current process is too generation-oriented for an existing repo

\- strategy descriptions need conversion to implementation-ready specs



\## Update rule

After each meaningful task:

\- revise only affected rows

\- add short evidence-based notes

\- do not mark DONE without code support



